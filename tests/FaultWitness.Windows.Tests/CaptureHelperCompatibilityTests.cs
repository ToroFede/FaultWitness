using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using FaultWitness.Core;
using FaultWitness.ElevatedHelper;
using FaultWitness.Platform;
using FaultWitness.Platform.Windows;

namespace FaultWitness.Windows.Tests;

public sealed class CaptureHelperCompatibilityTests : IDisposable
{
    private const string Name = "FaultWitness.ElevatedHelper";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "FaultWitness-helper-test-" + Guid.NewGuid().ToString("N"));
    private static string Build => CaptureHelperCompatibility.CurrentBuild;
    private static string Rid => CaptureHelperCompatibility.CurrentRid;

    public CaptureHelperCompatibilityTests()
    {
        Directory.CreateDirectory(directory);
        // A real apphost supplies a native PE header; no helper operation is executed.
        File.Copy(Environment.ProcessPath!, Path.Combine(directory, Name + ".exe"));
        foreach (var name in new[] { Name, "FaultWitness.Core", "FaultWitness.Platform", "FaultWitness.Platform.Windows" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, name + ".dll"), Path.Combine(directory, name + ".dll"));
        WriteContract(1, Build, Rid);
        File.WriteAllText(Path.Combine(directory, Name + ".runtimeconfig.json"), "{\"runtimeOptions\":{\"tfm\":\"net10.0\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"}}}");
        File.WriteAllText(Path.Combine(directory, Name + ".deps.json"), "{\"runtimeTarget\":{\"name\":\"net10.0/" + Rid + "\"},\"targets\":{\"net10.0/" + Rid + "\":{\"" + Name + "/0.9.0-beta.1\":{\"runtime\":{\"" + Name + ".dll\":{}}}}}}");
    }
    private void WriteContract(int schema, string build, string rid) => File.WriteAllText(Path.Combine(directory, Name + ".payload.json"), JsonSerializer.Serialize(new { SchemaVersion = schema, Build = build, Rid = rid }));
    private CaptureResultCode Validate() => CaptureHelperCompatibility.Validate(directory, Build, Rid);
    [Fact] public void MatchingPayloadIsCompatible() => Assert.Equal(CaptureResultCode.Success, Validate());
    [Theory]
    [InlineData(".exe")] [InlineData(".dll")] [InlineData(".deps.json")] [InlineData(".runtimeconfig.json")] [InlineData(".payload.json")]
    public void MissingPayloadFailsSafely(string extension)
    {
        File.Delete(Path.Combine(directory, Name + extension));
        Assert.Equal(CaptureResultCode.HelperUnavailable, Validate());
    }
    [Fact] public void MissingDependencyFailsSafely()
    {
        File.Delete(Path.Combine(directory, "FaultWitness.Core.dll"));
        Assert.Equal(CaptureResultCode.HelperUnavailable, Validate());
    }
    [Fact] public void StaleBuildIsRejected() { WriteContract(1, "0.8.0+old", Rid); Assert.Equal(CaptureResultCode.HelperIncompatible, Validate()); }
    [Fact] public void WrongSchemaIsRejected() { WriteContract(2, Build, Rid); Assert.Equal(CaptureResultCode.HelperIncompatible, Validate()); }
    [Fact] public void WrongRidIsRejected() { WriteContract(1, Build, "win-x86"); Assert.Equal(CaptureResultCode.HelperIncompatible, Validate()); }
    [Fact] public void WrongNativeArchitectureIsRejected()
    {
        var other = Rid == "win-x64" ? "win-arm64" : "win-x64";
        WriteContract(1, Build, other);
        Assert.Equal(CaptureResultCode.HelperIncompatible, CaptureHelperCompatibility.Validate(directory, Build, other));
    }
    [Fact] public void MismatchedAssemblyIsRejected()
    {
        File.Copy(typeof(JsonDocument).Assembly.Location, Path.Combine(directory, Name + ".dll"), true);
        Assert.Equal(CaptureResultCode.HelperIncompatible, Validate());
    }
    [Theory]
    [InlineData(".payload.json")] [InlineData(".deps.json")] [InlineData(".runtimeconfig.json")] [InlineData(".exe")]
    public void MalformedPayloadFailsSafely(string extension)
    {
        File.WriteAllText(Path.Combine(directory, Name + extension), "malformed");
        Assert.Equal(CaptureResultCode.HelperIncompatible, Validate());
    }
    [Fact] public void DuplicateContractFieldIsRejected()
    {
        var path = Path.Combine(directory, Name + ".payload.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"SchemaVersion\":1", "\"SchemaVersion\":1,\"SchemaVersion\":1", StringComparison.Ordinal));
        Assert.Equal(CaptureResultCode.HelperIncompatible, Validate());
    }
    public void Dispose() => Directory.Delete(directory, true);
}
