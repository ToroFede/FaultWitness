using FaultWitness.App;
using FaultWitness.Core;
using FaultWitness.Platform;

namespace FaultWitness.UI.Tests;

public sealed class ChangeHistoryEnricherTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task QueriesUseRetainedOnsetAndMergeOverlappingWindows()
    {
        var scan = Scan(At, At.AddDays(2));
        var provider = new FakeProvider();
        var result = await ChangeHistoryEnricher.EnrichAsync(scan,
            [new("signature", IncidentCategory.Graphics, At.AddDays(-20))], provider, TestContext.Current.CancellationToken);
        var window = Assert.Single(provider.Windows);
        Assert.Equal(At.AddDays(-27), window.From);
        Assert.Equal(At.AddDays(-19), window.To);
        Assert.All(result.Incidents, incident => Assert.Equal(FirstObservationBasis.RetainedHistory, incident.ChangeContext!.Basis));
    }

    [Fact]
    public async Task DisjointOnsetsDoNotQueryInterveningLifetimeHistory()
    {
        var scan = Scan(At, At.AddDays(30));
        scan = scan with { Incidents = scan.Incidents.Select((item, index) => item with { Signature = "signature" + index }).ToArray() };
        var provider = new FakeProvider();
        await ChangeHistoryEnricher.EnrichAsync(scan, [], provider, TestContext.Current.CancellationToken);
        Assert.Equal(2, provider.Windows.Count);
        Assert.All(provider.Windows, window => Assert.Equal(TimeSpan.FromDays(8), window.To - window.From));
    }

    [Fact]
    public async Task SourceFailurePreservesDiagnosisAndShowsUnavailable()
    {
        var scan = Scan(At);
        var result = await ChangeHistoryEnricher.EnrichAsync(scan, [], new FakeProvider { Fail = true }, TestContext.Current.CancellationToken, false);
        var incident = Assert.Single(result.Incidents);
        Assert.Same(scan.Incidents[0].Findings, incident.Findings);
        Assert.Contains(incident.ChangeContext!.Coverage, item => item.State == CoverageState.Unavailable);
        Assert.Contains(incident.ChangeContext.Coverage, item => item.Channel == "ChangeSourceFaultWitnessHistory");
    }

    [Fact]
    public async Task EmptyDiagnosisDoesNotCollectHistory()
    {
        var provider = new FakeProvider();
        await ChangeHistoryEnricher.EnrichAsync(ScanResult.Empty(At), [], provider, TestContext.Current.CancellationToken);
        Assert.Empty(provider.Windows);
    }

    [Fact]
    public async Task CancellationIsNotConvertedToUnavailable()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ChangeHistoryEnricher.EnrichAsync(Scan(At), [], new FakeProvider(), cancellation.Token));
    }

    private static ScanResult Scan(params DateTimeOffset[] times) => new(times.Select(time =>
    {
        var record = new NormalizedEvent(Guid.NewGuid(), SourceType.EventLog, "Windows", time, "System", "Display", 4101,
            0, IncidentSeverity.Medium, null, null, null, null, new Dictionary<string, string>(), "synthetic");
        return new Incident(Guid.NewGuid(), time, time, IncidentCategory.Graphics, IncidentSeverity.Medium, record, [],
            [new("synthetic", "1", FindingDisposition.Significant, EvidenceStrength.Strong, "observed", "interpretation", "not-established", [], [], [])],
            [record], [], "signature");
    }).ToArray(), [], At, At);

    private sealed class FakeProvider : IChangeHistoryProvider
    {
        public bool Fail { get; init; }
        public List<(DateTimeOffset From, DateTimeOffset To)> Windows { get; } = [];
        public Task<ChangeHistoryBatch> GetChangesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Windows.Add((fromUtc, toUtc));
            if (Fail) throw new IOException("synthetic failure");
            return Task.FromResult(new ChangeHistoryBatch([], [new(SourceType.ChangeHistory, CoverageState.Partial,
                fromUtc, toUtc, "ChangeCoveragePartial", "ChangeSourceSetupApi")]));
        }
    }
}
