using System.Globalization;
using System.Management;
using System.Security;
using Microsoft.Win32;
using FaultWitness.Core;

namespace FaultWitness.Platform.Windows;

public enum ConfigurationAccess { Available, Unavailable, AccessDenied, NotSupported }
public sealed record SystemDumpConfiguration(ConfigurationAccess Access, int? DumpType = null,
    string? Target = null, bool TargetIsKey = false, bool DedicatedDumpFile = false, bool ActiveMemory = false);
public sealed record PageFileConfiguration(ConfigurationAccess Access, bool? Automatic = null, int? ActiveFiles = null,
    ulong? AllocatedMegabytes = null);
public sealed record LocalDumpSetting(string Scope, int? DumpType, int? DumpCount, string Target, bool TargetIsKey = false);
public sealed record LocalDumpConfiguration(ConfigurationAccess Access, bool Present = false,
    IReadOnlyList<LocalDumpSetting>? Settings = null, bool Partial = false);
public sealed record DumpStorageConfiguration(ConfigurationAccess Access, IReadOnlyList<DiagnosticObservation>? Details = null);

/// <summary>Read-only observations. These facts never establish that Windows will successfully write a future dump.</summary>
public interface IWindowsCrashConfigurationReader
{
    Task<SystemDumpConfiguration> ReadSystemDumpAsync(CancellationToken token);
    Task<PageFileConfiguration> ReadPageFileAsync(CancellationToken token);
    Task<LocalDumpConfiguration> ReadLocalDumpsAsync(CancellationToken token);
    Task<DumpStorageConfiguration> ReadDumpStorageAsync(CancellationToken token);
}

public sealed class WindowsDiagnosticReadiness(IWindowsCrashConfigurationReader? configuration = null, WindowsReadinessSources? sources = null)
{
    private readonly IWindowsCrashConfigurationReader reader = configuration ?? new WindowsCrashConfigurationReader();
    private readonly WindowsReadinessSources sourceReader = sources ?? new WindowsReadinessSources();

    public async Task<IReadOnlyList<DiagnosticReadinessItem>> GetAsync(CancellationToken token)
    {
        var sourceTask = sourceReader.GetAsync(token);
        var configTask = GetConfigurationAsync(token);
        await Task.WhenAll(sourceTask, configTask).ConfigureAwait(false);
        return [.. await sourceTask.ConfigureAwait(false), .. await configTask.ConfigureAwait(false)];
    }

    public async Task<IReadOnlyList<DiagnosticReadinessItem>> GetConfigurationAsync(CancellationToken token)
    {
        var system = ReadSafely(() => reader.ReadSystemDumpAsync(token), access => new SystemDumpConfiguration(access), token);
        var page = ReadSafely(() => reader.ReadPageFileAsync(token), access => new PageFileConfiguration(access), token);
        var local = ReadSafely(() => reader.ReadLocalDumpsAsync(token), access => new LocalDumpConfiguration(access), token);
        var storage = ReadSafely(() => reader.ReadDumpStorageAsync(token), access => new DumpStorageConfiguration(access), token);
        await Task.WhenAll(system, page, local, storage).ConfigureAwait(false);
        return [Evaluate(await system.ConfigureAwait(false)), Evaluate(await page.ConfigureAwait(false)),
            Evaluate(await storage.ConfigureAwait(false)), Evaluate(await local.ConfigureAwait(false))];
    }

    private static async Task<T> ReadSafely<T>(Func<Task<T>> read, Func<ConfigurationAccess, T> failed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { return await read().WaitAsync(TimeSpan.FromSeconds(8), token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        { return failed(ConfigurationAccess.AccessDenied); }
        catch (ManagementException exception) when (exception.ErrorCode == ManagementStatus.AccessDenied)
        { return failed(ConfigurationAccess.AccessDenied); }
        catch (PlatformNotSupportedException) { return failed(ConfigurationAccess.NotSupported); }
        catch (Exception exception) when (exception is IOException or ManagementException or System.Runtime.InteropServices.COMException or TimeoutException or InvalidOperationException)
        { return failed(ConfigurationAccess.Unavailable); }
    }

    private static DiagnosticReadinessItem Inaccessible(string id, string name, ConfigurationAccess access) =>
        new(id, name, access switch
        {
            ConfigurationAccess.AccessDenied => DiagnosticCapabilityStatus.AccessDenied,
            ConfigurationAccess.NotSupported => DiagnosticCapabilityStatus.NotSupported,
            _ => DiagnosticCapabilityStatus.Unavailable
        }, access == ConfigurationAccess.NotSupported ? "ReadinessSourceNotSupported" : "ReadinessConfigurationUnknown",
            ElevationMayHelp: access == ConfigurationAccess.AccessDenied,
            NextActionKey: access == ConfigurationAccess.AccessDenied ? "ReadinessElevationHelp" : null);

    public static DiagnosticReadinessItem Evaluate(SystemDumpConfiguration value)
    {
        if (value.Access != ConfigurationAccess.Available) return Inaccessible("system-dumps", "DumpConfiguration", value.Access);
        var type = value.DumpType switch { 0 => "None", 1 when value.ActiveMemory => "Active", 1 => "Complete", 2 => "Kernel", 3 => "Small", 7 => "Automatic", _ => null };
        var details = new List<DiagnosticObservation>();
        if (type is not null) details.Add(new("ReadinessDumpType", type));
        if (value.Target is not null) details.Add(new("ReadinessDumpTarget", value.Target, value.TargetIsKey));
        details.Add(new("ReadinessDedicatedDump", value.DedicatedDumpFile ? "ReadinessObserved" : "ReadinessNotObserved", true));
        return new("system-dumps", "DumpConfiguration", type == "None" ? DiagnosticCapabilityStatus.Disabled :
            type is null ? DiagnosticCapabilityStatus.Unavailable : DiagnosticCapabilityStatus.Ready,
            type == "None" ? "ReadinessSystemDumpsDisabled" : type is null ? "ReadinessConfigurationUnknown" : "ReadinessSystemDumpsConfigured", details);
    }

    public static DiagnosticReadinessItem Evaluate(PageFileConfiguration value)
    {
        if (value.Access != ConfigurationAccess.Available) return Inaccessible("pagefile-support", "PageFile", value.Access);
        var details = new List<DiagnosticObservation>();
        if (value.Automatic is { } automatic) details.Add(new("ReadinessAutomaticPageFile", automatic ? "ReadinessObserved" : "ReadinessNotObserved", true));
        if (value.ActiveFiles is { } count) details.Add(new("ReadinessActivePageFiles", count.ToString(CultureInfo.InvariantCulture)));
        if (value.AllocatedMegabytes is { } size) details.Add(new("ReadinessAllocatedPageFile", size.ToString(CultureInfo.InvariantCulture) + " MiB"));
        // Static size/configuration cannot prove future kernel requirements, dedicated-file validity or capture success.
        return new("pagefile-support", "PageFile", DiagnosticCapabilityStatus.Limited, "ReadinessPageFileUnknown", details);
    }

    public static DiagnosticReadinessItem Evaluate(LocalDumpConfiguration value)
    {
        if (value.Access != ConfigurationAccess.Available) return Inaccessible("local-dumps", "ApplicationDumps", value.Access);
        if (!value.Present) return new("local-dumps", "ApplicationDumps", DiagnosticCapabilityStatus.Disabled, "ReadinessLocalDumpsAbsent");
        var details = new List<DiagnosticObservation>();
        var valid = value.Settings is { Count: > 0 };
        foreach (var setting in value.Settings ?? [])
        {
            details.Add(new("ReadinessLocalDumpScope", setting.Scope, setting.Scope == "ReadinessDefaultScope"));
            if (setting.DumpType is { } type) details.Add(new("ReadinessDumpType", type switch { 0 => "Custom (0)", 1 => "Mini (1)", 2 => "Full (2)", _ => type.ToString(CultureInfo.InvariantCulture) }));
            if (setting.DumpCount is { } count) details.Add(new("ReadinessDumpCount", count.ToString(CultureInfo.InvariantCulture)));
            details.Add(new("ReadinessDumpTarget", setting.Target, setting.TargetIsKey));
            valid &= setting.DumpType is >= 0 and <= 2 && setting.DumpCount is > 0 && setting.Target != "ReadinessTargetUnknown";
        }
        return new("local-dumps", "ApplicationDumps", valid && !value.Partial ? DiagnosticCapabilityStatus.Ready : DiagnosticCapabilityStatus.Limited,
            valid && !value.Partial ? "ReadinessLocalDumpsConfigured" : "ReadinessLocalDumpsLimited", details);
    }

    public static DiagnosticReadinessItem Evaluate(DumpStorageConfiguration value) => value.Access != ConfigurationAccess.Available
        ? Inaccessible("dump-storage", "FreeSpace", value.Access)
        : new("dump-storage", "FreeSpace", DiagnosticCapabilityStatus.Limited, "ReadinessStorageObserved", value.Details);
}

/// <summary>Only Windows configuration read APIs are used. Custom paths are never retained in output.</summary>
public sealed class WindowsCrashConfigurationReader : IWindowsCrashConfigurationReader
{
    private const string CrashControl = @"SYSTEM\CurrentControlSet\Control\CrashControl";
    private const string LocalDumps = @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps";
    private static RegistryKey OpenMachine() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
    private static int? Number(RegistryKey key, string name, int? fallback = null) => key.GetValue(name) is { } raw
        ? raw is int number ? number : null : fallback;

    public Task<SystemDumpConfiguration> ReadSystemDumpAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return new SystemDumpConfiguration(ConfigurationAccess.NotSupported);
        using var machine = OpenMachine(); using var key = machine.OpenSubKey(CrashControl, writable: false);
        if (key is null) return new SystemDumpConfiguration(ConfigurationAccess.Unavailable);
        var type = Number(key, "CrashDumpEnabled");
        var raw = key.GetValue(type == 3 ? "MinidumpDir" : "DumpFile", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        var target = SafeTarget(raw);
        return new SystemDumpConfiguration(ConfigurationAccess.Available, type, target.Value, target.IsKey,
            !string.IsNullOrWhiteSpace(key.GetValue("DedicatedDumpFile", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string), Number(key, "FilterPages") == 1);
    }, token);

    public Task<PageFileConfiguration> ReadPageFileAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return new PageFileConfiguration(ConfigurationAccess.NotSupported);
        bool? automatic = null; int count = 0; ulong total = 0;
        using (var computer = Search("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem"))
        using (var rows = computer.Get())
            foreach (ManagementBaseObject row in rows) { using (row) { automatic = row["AutomaticManagedPagefile"] as bool?; } break; }
        token.ThrowIfCancellationRequested();
        using var searcher = Search("SELECT AllocatedBaseSize FROM Win32_PageFileUsage"); using var results = searcher.Get();
        foreach (ManagementBaseObject row in results)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                if (++count > 32) return new PageFileConfiguration(ConfigurationAccess.Unavailable);
                if (row["AllocatedBaseSize"] is not uint size) return new PageFileConfiguration(ConfigurationAccess.Available, automatic);
                total += size;
            }
        }
        return new PageFileConfiguration(ConfigurationAccess.Available, automatic, count, total);
    }, token);

    private static ManagementObjectSearcher Search(string query) => new("root\\CIMV2", query,
        new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(3), ReturnImmediately = true });

    public Task<LocalDumpConfiguration> ReadLocalDumpsAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return new LocalDumpConfiguration(ConfigurationAccess.NotSupported);
        using var machine = OpenMachine(); using var key = machine.OpenSubKey(LocalDumps, writable: false);
        if (key is null) return new LocalDumpConfiguration(ConfigurationAccess.Available);
        var settings = new List<LocalDumpSetting>();
        var defaults = ReadSetting(key, "ReadinessDefaultScope", null); settings.Add(defaults);
        var names = key.GetSubKeyNames(); bool partial = names.Length > 32;
        foreach (var name in names.Take(32))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var app = key.OpenSubKey(name, writable: false);
                if (app is null) { partial = true; continue; }
                // Executable names may contain account data; display ordinal scopes instead of retaining arbitrary key names.
                settings.Add(ReadSetting(app, "#" + settings.Count.ToString(CultureInfo.InvariantCulture), defaults));
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException) { partial = true; }
        }
        return new LocalDumpConfiguration(ConfigurationAccess.Available, true, settings, partial);
    }, token);

    private static LocalDumpSetting ReadSetting(RegistryKey key, string scope, LocalDumpSetting? defaults)
    {
        var raw = key.GetValue("DumpFolder", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var target = raw is null && defaults is not null ? (defaults.Target, defaults.TargetIsKey)
            : SafeTarget(raw is null ? @"%LOCALAPPDATA%\CrashDumps" : raw as string);
        return new LocalDumpSetting(scope, Number(key, "DumpType", defaults is null ? 1 : defaults.DumpType),
            Number(key, "DumpCount", defaults is null ? 10 : defaults.DumpCount), target.Item1, target.Item2);
    }

    public Task<DumpStorageConfiguration> ReadDumpStorageAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return new DumpStorageConfiguration(ConfigurationAccess.NotSupported);
        var details = new List<DiagnosticObservation>();
        using var machine = OpenMachine(); using var key = machine.OpenSubKey(CrashControl, writable: false);
        var targets = new List<(string Label, string? Path)> { ("ReadinessSystemDrive", Environment.GetFolderPath(Environment.SpecialFolder.Windows)) };
        if (key is not null) targets.Add(("ReadinessDumpDestination", key.GetValue(Number(key, "CrashDumpEnabled") == 3 ? "MinidumpDir" : "DumpFile") as string));
        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();
            // Never follow UNC paths, mount aliases or user-defined network destinations for a readiness refresh.
            var expanded = target.Path is null ? null : Environment.ExpandEnvironmentVariables(target.Path);
            if (expanded is null || expanded.Length < 3 || !char.IsAsciiLetter(expanded[0]) || expanded[1] != ':' || expanded[2] != '\\') continue;
            var drive = new DriveInfo(expanded[..3]);
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
            details.Add(new(target.Label, (drive.AvailableFreeSpace / 1073741824d).ToString("F1", CultureInfo.InvariantCulture) + " GiB"));
        }
        return new DumpStorageConfiguration(details.Count > 0 ? ConfigurationAccess.Available : ConfigurationAccess.Unavailable, details);
    }, token);

    public static (string Value, bool IsKey) SafeTarget(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ("ReadinessTargetUnknown", true);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[] { (@"%SystemRoot%\MEMORY.DMP", Path.Combine(windows, "MEMORY.DMP")),
            (@"%SystemRoot%\Minidump", Path.Combine(windows, "Minidump")), (@"%LOCALAPPDATA%\CrashDumps", Path.Combine(local, "CrashDumps")) };
        foreach (var (safe, expanded) in candidates)
            if (string.Equals(value, safe, StringComparison.OrdinalIgnoreCase) || string.Equals(value, expanded, StringComparison.OrdinalIgnoreCase)) return (safe, false);
        return ("ReadinessCustomTarget", true);
    }
}
