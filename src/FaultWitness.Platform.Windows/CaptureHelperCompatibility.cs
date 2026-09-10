using System.Diagnostics;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using FaultWitness.Core;

namespace FaultWitness.Platform.Windows;

/// <summary>Deployment compatibility checks, not authentication. Protected installation remains a release gate.</summary>
public static class CaptureHelperCompatibility
{
    public const int SchemaVersion = 1;
    private const string Name = "FaultWitness.ElevatedHelper";
    public static string CurrentBuild => typeof(CaptureHelperCompatibility).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    public static string CurrentRid => "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    public static CaptureResultCode Validate(string directory, string expectedBuild, string expectedRid)
    {
        string[] required = [Name + ".exe", Name + ".dll", Name + ".deps.json", Name + ".runtimeconfig.json", Name + ".payload.json"];
        if (required.Any(file => !File.Exists(Path.Combine(directory, file)))) return CaptureResultCode.HelperUnavailable;
        try
        {
            using var contract = ReadJson(Path.Combine(directory, Name + ".payload.json"));
            var root = contract.RootElement;
            var names = new HashSet<string>(["SchemaVersion", "Build", "Rid"], StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!names.Remove(property.Name)) return CaptureResultCode.HelperIncompatible;
            if (names.Count != 0 || root.GetProperty("SchemaVersion").GetInt32() != SchemaVersion ||
                root.GetProperty("Build").GetString() != expectedBuild || root.GetProperty("Rid").GetString() != expectedRid)
                return CaptureResultCode.HelperIncompatible;
            using var executable = File.OpenRead(Path.Combine(directory, Name + ".exe"));
            using var pe = new PEReader(executable);
            var machine = expectedRid switch { "win-x64" => Machine.Amd64, "win-arm64" => Machine.Arm64, _ => Machine.Unknown };
            if (machine == Machine.Unknown || pe.PEHeaders.CoffHeader.Machine != machine) return CaptureResultCode.HelperIncompatible;
            foreach (var assembly in new[] { Name, "FaultWitness.Core", "FaultWitness.Platform", "FaultWitness.Platform.Windows" })
            {
                var path = Path.Combine(directory, assembly + ".dll");
                if (!File.Exists(path)) return CaptureResultCode.HelperUnavailable;
                if (FileVersionInfo.GetVersionInfo(path).ProductVersion != expectedBuild) return CaptureResultCode.HelperIncompatible;
            }
            using var deps = ReadJson(Path.Combine(directory, Name + ".deps.json"));
            var target = deps.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
            if (target.Contains('/', StringComparison.Ordinal) && !target.EndsWith("/" + expectedRid, StringComparison.Ordinal))
                return CaptureResultCode.HelperIncompatible;
            var assets = deps.RootElement.GetProperty("targets").GetProperty(target);
            if (!assets.EnumerateObject().Any(library => library.Name.StartsWith(Name + "/", StringComparison.Ordinal) &&
                library.Value.GetProperty("runtime").TryGetProperty(Name + ".dll", out _))) return CaptureResultCode.HelperIncompatible;
            foreach (var library in assets.EnumerateObject())
                foreach (var group in library.Value.EnumerateObject())
                    if (group.Name is "runtime" or "native")
                        foreach (var asset in group.Value.EnumerateObject())
                            if (!File.Exists(Path.Combine(directory, Path.GetFileName(asset.Name))))
                                return CaptureResultCode.HelperUnavailable;
            using var runtime = ReadJson(Path.Combine(directory, Name + ".runtimeconfig.json"));
            var options = runtime.RootElement.GetProperty("runtimeOptions");
            if (options.GetProperty("tfm").GetString() != "net10.0") return CaptureResultCode.HelperIncompatible;
            if (options.TryGetProperty("includedFrameworks", out _) &&
                (!File.Exists(Path.Combine(directory, "hostfxr.dll")) || !File.Exists(Path.Combine(directory, "hostpolicy.dll")) || !File.Exists(Path.Combine(directory, "coreclr.dll"))))
                return CaptureResultCode.HelperUnavailable;
            return CaptureResultCode.Success;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or BadImageFormatException or FormatException or OverflowException or ArgumentException)
        {
            return CaptureResultCode.HelperIncompatible;
        }
    }

    private static JsonDocument ReadJson(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 1024 * 1024) throw new JsonException();
        return JsonDocument.Parse(stream);
    }
}
