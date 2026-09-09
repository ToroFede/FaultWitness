using FaultWitness.Core;
using FaultWitness.Platform.Windows;

namespace FaultWitness.Windows.Tests;

public sealed class ReadinessSourceTests
{
    [Fact]
    public async Task ReturnsStableSourcesAndReadyStatuses()
    {
        var sources = new WindowsReadinessSources(new FakeProbe(_ => DiagnosticCapabilityStatus.Ready));
        var result = await sources.GetAsync(CancellationToken.None);

        Assert.Equal(["system-event-log", "application-event-log", "wer", "reliability", "crash-artifacts"], result.Select(x => x.Id));
        Assert.All(result, item => Assert.Equal(DiagnosticCapabilityStatus.Ready, item.Status));
        Assert.All(result, item => Assert.Equal("ReadinessSourceReady", item.DetailKey));
    }

    [Fact]
    public async Task PreservesAccessDeniedAndElevationAction()
    {
        var result = await new WindowsReadinessSources(new FakeProbe(id =>
            id is "system-event-log" or "application-event-log" ? DiagnosticCapabilityStatus.AccessDenied : DiagnosticCapabilityStatus.Ready))
            .GetAsync(CancellationToken.None);

        var denied = result.Where(x => x.Status == DiagnosticCapabilityStatus.AccessDenied).ToArray();
        Assert.Equal(2, denied.Length);
        Assert.All(denied, item =>
        {
            Assert.True(item.ElevationMayHelp);
            Assert.Equal("ReadinessElevationHelp", item.NextActionKey);
        });
    }

    [Theory]
    [InlineData("wer", DiagnosticCapabilityStatus.Limited, "ReadinessSourceLimited")]
    [InlineData("reliability", DiagnosticCapabilityStatus.Unavailable, "ReadinessSourceUnavailable")]
    [InlineData("crash-artifacts", DiagnosticCapabilityStatus.Limited, "ReadinessSourceLimited")]
    public async Task MapsIndependentSourceStates(string id, DiagnosticCapabilityStatus status, string detail)
    {
        var result = await new WindowsReadinessSources(new FakeProbe(candidate => candidate == id ? status : DiagnosticCapabilityStatus.Ready))
            .GetAsync(CancellationToken.None);
        var item = Assert.Single(result, x => x.Id == id);
        Assert.Equal(status, item.Status);
        Assert.Equal(detail, item.DetailKey);
    }

    [Fact]
    public async Task FailedProbeDoesNotHideOtherSources()
    {
        var result = await new WindowsReadinessSources(new FakeProbe(id =>
            id == "wer" ? throw new InvalidOperationException() : DiagnosticCapabilityStatus.Ready))
            .GetAsync(CancellationToken.None);
        Assert.Equal(DiagnosticCapabilityStatus.Unavailable, Assert.Single(result, x => x.Id == "wer").Status);
        Assert.Equal(4, result.Count(x => x.Status == DiagnosticCapabilityStatus.Ready));
    }

    [Fact]
    public async Task ProbeCancellationIsObserved()
    {
        using var cancellation = new CancellationTokenSource();
        var task = new WindowsReadinessSources(new BlockingProbe(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return DiagnosticCapabilityStatus.Ready;
        })).GetAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task UnknownProbeIdIsNotSupported()
    {
        var probe = new WindowsReadinessSourceProbe();
        Assert.Equal(DiagnosticCapabilityStatus.NotSupported,
            await probe.ProbeAsync("future-source", CancellationToken.None));
    }

    [Fact]
    public void DirectoryProbeClassifiesMissingAndReadableEmptyRoots()
    {
        var fs = new FakeFileSystem(("missing", ReadinessDirectoryState.Missing), ("empty", ReadinessDirectoryState.Accessible));
        Assert.Equal(DiagnosticCapabilityStatus.Limited, WindowsReadinessSourceProbe.ProbeDirectories(["missing", "empty"], _ => { }, fs, false, CancellationToken.None));
        Assert.Equal(DiagnosticCapabilityStatus.Unavailable, WindowsReadinessSourceProbe.ProbeDirectories(["missing"], _ => { }, fs, false, CancellationToken.None));
    }

    [Fact]
    public void DirectoryProbeClassifiesDeniedAndInaccessibleFiles()
    {
        var fs = new FakeFileSystem(("denied", ReadinessDirectoryState.Denied, []), ("root", ReadinessDirectoryState.Accessible,
            [new ReadinessFileSystemEntry("root/a.dmp", false, false)]));
        Assert.Equal(DiagnosticCapabilityStatus.AccessDenied, WindowsReadinessSourceProbe.ProbeDirectories(["denied"], _ => { }, fs, false, CancellationToken.None));
        Assert.Equal(DiagnosticCapabilityStatus.Limited, WindowsReadinessSourceProbe.ProbeDirectories(["root"], _ => throw new UnauthorizedAccessException(), fs, true, CancellationToken.None));
    }

    [Fact]
    public void DirectoryProbeBoundsTraversalAndSkipsReparseEntries()
    {
        var entries = Enumerable.Range(0, 257).Select(i => new ReadinessFileSystemEntry($"root/{i}.dmp", false, false)).ToArray();
        var fs = new FakeFileSystem(("root", ReadinessDirectoryState.Accessible, entries));
        Assert.Equal(DiagnosticCapabilityStatus.Limited, WindowsReadinessSourceProbe.ProbeDirectories(["root"], _ => { }, fs, true, CancellationToken.None));
        var reparse = new FakeFileSystem(("root", ReadinessDirectoryState.Accessible,
            [new ReadinessFileSystemEntry("root/link", true, true)]));
        Assert.Equal(DiagnosticCapabilityStatus.Limited, WindowsReadinessSourceProbe.ProbeDirectories(["root"], _ => { }, reparse, false, CancellationToken.None));
    }

    private sealed class FakeProbe(Func<string, DiagnosticCapabilityStatus> callback) : IWindowsReadinessSourceProbe
    {
        private readonly Func<string, DiagnosticCapabilityStatus> _callback = callback;
        public Task<DiagnosticCapabilityStatus> ProbeAsync(string id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(_callback(id));
        }
    }

    private sealed class BlockingProbe(Func<string, CancellationToken, Task<DiagnosticCapabilityStatus>> callback) : IWindowsReadinessSourceProbe
    {
        public Task<DiagnosticCapabilityStatus> ProbeAsync(string id, CancellationToken token) => callback(id, token);
    }

    private sealed class FakeFileSystem : IWindowsReadinessFileSystem
    {
        private readonly Dictionary<string, ReadinessDirectoryResult> _items;
        public FakeFileSystem(params (string Path, ReadinessDirectoryState State, ReadinessFileSystemEntry[] Entries)[] items) =>
            _items = items.ToDictionary(x => x.Path, x => new ReadinessDirectoryResult(x.State, x.Entries));
        public FakeFileSystem(params (string Path, ReadinessDirectoryState State)[] items) =>
            _items = items.ToDictionary(x => x.Path, x => new ReadinessDirectoryResult(x.State, []));
        public ReadinessDirectoryResult Read(string path, CancellationToken token) =>
            _items.TryGetValue(path, out var result) ? result : new(ReadinessDirectoryState.Missing, []);
    }
}
