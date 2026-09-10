using System.Reflection;
using FaultWitness.Core;

namespace FaultWitness.Core.Tests;

public sealed class ReleaseIdentityTests
{
    [Fact]
    public void IdentityUsesParseableAssemblyVersionAndInformationalBuild()
    {
        Assert.Equal("0.9.0-beta.1", ReleaseIdentity.Product);
        Assert.Equal("FaultWitness", ReleaseIdentity.Name);
        Assert.True(Version.TryParse(ReleaseIdentity.Assembly, out var assemblyVersion));
        Assert.Equal(new Version(0, 9, 0, 0), assemblyVersion);
        Assert.StartsWith("0.9.0-beta.1", ReleaseIdentity.Build, StringComparison.Ordinal);
        Assert.StartsWith("FaultWitness 0.9.0-beta.1", ReleaseIdentity.Display, StringComparison.Ordinal);
        Assert.Equal(ReleaseIdentity.Build, typeof(ReleaseIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }

    [Fact]
    public void JournalSchemaRemainsIndependentOfProductVersion()
    {
        var entry = new CaptureJournalEntry(Guid.NewGuid(), CaptureOperation.ConfigureApplicationCrashDump, "demo.exe", DateTimeOffset.UtcNow, false,
            new LocalDumpState(false), new LocalDumpState(true));
        Assert.Equal(1, entry.SchemaVersion);
        Assert.Equal(ReleaseIdentity.Assembly, entry.AppVersion);
    }
}
