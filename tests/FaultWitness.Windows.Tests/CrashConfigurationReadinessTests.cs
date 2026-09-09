using FaultWitness.Core;
using FaultWitness.Platform.Windows;

namespace FaultWitness.Windows.Tests;

public sealed class CrashConfigurationReadinessTests
{
    [Fact]
    public void SystemDumpsDisabled_IsExplicit() => Assert.Equal(DiagnosticCapabilityStatus.Disabled,
        WindowsDiagnosticReadiness.Evaluate(new SystemDumpConfiguration(ConfigurationAccess.Available, 0)).Status);

    [Theory]
    [InlineData(1, "Complete")][InlineData(2, "Kernel")][InlineData(3, "Small")][InlineData(7, "Automatic")]
    public void KnownDumpTypes_DescribeConfigurationOnly(int type, string expected)
    {
        var item = WindowsDiagnosticReadiness.Evaluate(new SystemDumpConfiguration(ConfigurationAccess.Available, type, @"%SystemRoot%\MEMORY.DMP"));
        Assert.Equal(DiagnosticCapabilityStatus.Ready, item.Status);
        Assert.Equal("ReadinessSystemDumpsConfigured", item.DetailKey);
        Assert.Contains(item.Observations!, detail => detail.Value == expected);
    }

    [Fact]
    public void ActiveDumpFilter_IsHonored() => Assert.Contains(
        WindowsDiagnosticReadiness.Evaluate(new SystemDumpConfiguration(ConfigurationAccess.Available, 1, ActiveMemory: true)).Observations!, x => x.Value == "Active");

    [Theory]
    [InlineData(null)][InlineData(99)]
    public void UnknownDumpType_IsNotConfiguredSuccess(int? type) => Assert.Equal(DiagnosticCapabilityStatus.Unavailable,
        WindowsDiagnosticReadiness.Evaluate(new SystemDumpConfiguration(ConfigurationAccess.Available, type)).Status);

    [Theory]
    [InlineData(true, 1, 65536)][InlineData(false, 0, 0)][InlineData(null, null, null)]
    public void PagefileSufficiencyUnknown_NeverBecomesReady(bool? automatic, int? files, int? size)
    {
        var item = WindowsDiagnosticReadiness.Evaluate(new PageFileConfiguration(ConfigurationAccess.Available, automatic, files, (ulong?)size));
        Assert.Equal(DiagnosticCapabilityStatus.Limited, item.Status);
        Assert.Equal("ReadinessPageFileUnknown", item.DetailKey);
    }

    [Fact]
    public void LocalDumpsAbsent_IsNeutralNotConfigured()
    {
        var item = WindowsDiagnosticReadiness.Evaluate(new LocalDumpConfiguration(ConfigurationAccess.Available));
        Assert.Equal(DiagnosticCapabilityStatus.Disabled, item.Status);
        Assert.Equal("ReadinessLocalDumpsAbsent", item.DetailKey);
        Assert.Null(item.NextActionKey); Assert.False(item.ElevationMayHelp);
    }

    [Fact]
    public void LocalDumpsConfigured_IncludesDefaultAndOverrides()
    {
        var item = WindowsDiagnosticReadiness.Evaluate(new LocalDumpConfiguration(ConfigurationAccess.Available, true,
            [new("ReadinessDefaultScope", 1, 10, @"%LOCALAPPDATA%\CrashDumps"), new("#1", 2, 3, "ReadinessCustomTarget", true)]));
        Assert.Equal(DiagnosticCapabilityStatus.Ready, item.Status);
        Assert.Equal(8, item.Observations!.Count);
    }

    [Theory]
    [InlineData(8, 10, false)][InlineData(1, 0, false)][InlineData(2, 10, true)][InlineData(null, 10, false)]
    public void InvalidOrPartialLocalDumps_IsLimited(int? type, int count, bool partial) => Assert.Equal(DiagnosticCapabilityStatus.Limited,
        WindowsDiagnosticReadiness.Evaluate(new LocalDumpConfiguration(ConfigurationAccess.Available, true,
            [new("ReadinessDefaultScope", type, count, @"%LOCALAPPDATA%\CrashDumps")], partial)).Status);

    [Theory]
    [InlineData(ConfigurationAccess.AccessDenied, DiagnosticCapabilityStatus.AccessDenied)]
    [InlineData(ConfigurationAccess.NotSupported, DiagnosticCapabilityStatus.NotSupported)]
    [InlineData(ConfigurationAccess.Unavailable, DiagnosticCapabilityStatus.Unavailable)]
    public void ConfigAccess_IsExplicit(ConfigurationAccess access, DiagnosticCapabilityStatus status)
    {
        Assert.Equal(status, WindowsDiagnosticReadiness.Evaluate(new SystemDumpConfiguration(access)).Status);
        Assert.Equal(status, WindowsDiagnosticReadiness.Evaluate(new PageFileConfiguration(access)).Status);
        Assert.Equal(status, WindowsDiagnosticReadiness.Evaluate(new LocalDumpConfiguration(access)).Status);
        Assert.Equal(status, WindowsDiagnosticReadiness.Evaluate(new DumpStorageConfiguration(access)).Status);
    }

    [Fact]
    public async Task DeniedRegistry_DoesNotKillOtherCapabilities()
    {
        var items = await new WindowsDiagnosticReadiness(new FakeReader { DenySystem = true }).GetConfigurationAsync(CancellationToken.None);
        Assert.Equal(4, items.Count);
        Assert.Equal(DiagnosticCapabilityStatus.AccessDenied, items[0].Status);
        Assert.True(items[0].ElevationMayHelp);
        Assert.Equal(DiagnosticCapabilityStatus.Disabled, items[^1].Status);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WindowsDiagnosticReadiness(new FakeReader()).GetConfigurationAsync(cancellation.Token));
    }

    [Theory]
    [InlineData(@"C:\Users\private-user\secret.dmp")][InlineData(@"\\private-server\share\dump")]
    [InlineData(@"D:\account-id\data")][InlineData(@"%USERNAME%\dump")]
    public void CustomDestinations_NeverExposeIdentifiers(string path)
    {
        var safe = WindowsCrashConfigurationReader.SafeTarget(path);
        Assert.Equal(("ReadinessCustomTarget", true), safe);
    }

    [Fact]
    public void StandardDestination_RemainsUsefulWithoutProfilePath() => Assert.Equal((@"%LOCALAPPDATA%\CrashDumps", false),
        WindowsCrashConfigurationReader.SafeTarget(@"%LOCALAPPDATA%\CrashDumps"));

    [Fact]
    public void CapabilityModel_HasNoAggregateOrHealthScore() => Assert.DoesNotContain(typeof(DiagnosticReadinessItem).GetProperties(),
        property => property.Name.Contains("Score", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Health", StringComparison.OrdinalIgnoreCase));

    private sealed class FakeReader : IWindowsCrashConfigurationReader
    {
        public bool DenySystem { get; init; }
        public Task<SystemDumpConfiguration> ReadSystemDumpAsync(CancellationToken token) => DenySystem
            ? throw new UnauthorizedAccessException("synthetic") : Task.FromResult(new SystemDumpConfiguration(ConfigurationAccess.Available, 7));
        public Task<PageFileConfiguration> ReadPageFileAsync(CancellationToken token) => Task.FromResult(new PageFileConfiguration(ConfigurationAccess.Available, true, 1, 1024));
        public Task<LocalDumpConfiguration> ReadLocalDumpsAsync(CancellationToken token) => Task.FromResult(new LocalDumpConfiguration(ConfigurationAccess.Available));
        public Task<DumpStorageConfiguration> ReadDumpStorageAsync(CancellationToken token) => Task.FromResult(new DumpStorageConfiguration(ConfigurationAccess.Available, []));
    }
}
