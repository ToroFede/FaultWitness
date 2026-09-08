using System.Globalization;
using System.Management;
using System.Security;
using Microsoft.Win32;

namespace FaultWitness.App;

public sealed record WindowsProductInformation(string? Caption, string? DisplayVersion, string? BuildNumber, string? NtVersion)
{
    // Caption/BuildNumber come from the documented Win32_OperatingSystem class. Never map NT 10 to a product edition.
    public static Task<WindowsProductInformation> ReadAsync(CancellationToken token) => Task.Run(() =>
    {
        string? caption = null, build = null, ntVersion = null, displayVersion = null;
        token.ThrowIfCancellationRequested();
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Caption, BuildNumber, Version FROM Win32_OperatingSystem",
                new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(5) });
            using var results = searcher.Get();
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    caption = Convert.ToString(item[nameof(Caption)], CultureInfo.InvariantCulture);
                    build = Convert.ToString(item[nameof(BuildNumber)], CultureInfo.InvariantCulture);
                    ntVersion = Convert.ToString(item["Version"], CultureInfo.InvariantCulture);
                }
                break;
            }
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        token.ThrowIfCancellationRequested();
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var version = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);
            displayVersion = version?.GetValue("DisplayVersion") as string;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException) { }
        return new WindowsProductInformation(caption, displayVersion, build, ntVersion);
    }, token);

    public IReadOnlyDictionary<string, string> ApplyTo(IReadOnlyDictionary<string, string> inventory)
    {
        var result = inventory.Where(pair => pair.Key != "OperatingSystem").ToDictionary(pair => pair.Key, pair => pair.Value);
        if (inventory.TryGetValue("OperatingSystem", out var raw)) result["NtVersion"] = raw;
        if (!string.IsNullOrWhiteSpace(Caption)) result["OperatingSystem"] = Caption;
        if (!string.IsNullOrWhiteSpace(DisplayVersion)) result["DisplayVersion"] = DisplayVersion;
        if (!string.IsNullOrWhiteSpace(BuildNumber)) result["BuildNumber"] = BuildNumber;
        if (!string.IsNullOrWhiteSpace(NtVersion)) result["NtVersion"] = NtVersion;
        return result;
    }
}
