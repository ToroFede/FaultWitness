using System.Diagnostics;
using System.Globalization;
using FaultWitness.App;
using FaultWitness.Core;
using FaultWitness.Storage;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "PerformanceValidation")]
public sealed class PerformanceValidationTests
{
    [Fact]
    public void SyntheticAnalysisAndProjection_1000Events_IsBounded()
    {
        var now = DateTimeOffset.UtcNow; var events = Enumerable.Range(0, 1000)
            .Select(i => new NormalizedEvent(Guid.NewGuid(), SourceType.EventLog, "Windows", now.AddSeconds(i), "Application", "Synthetic", 1000, null,
                IncidentSeverity.Medium, "Example.exe", null, null, null, new Dictionary<string, string>(), "synthetic"));
        var watch = Stopwatch.StartNew();
        var result = new IncidentAnalyzer([new PerfRule()]).Analyze(new EventBatch(events, []), now, now.AddDays(7), TestContext.Current.CancellationToken);
        var analysisMs = watch.ElapsedMilliseconds;
        var services = new TestServices { Result = SyntheticResults.Create(1000) };
        using var vm = new MainViewModel(services);
        watch.Restart(); vm.SetResult(services.Result); vm.SetFilter(new IncidentFilter(Search: "SearchTarget.exe"));
        var projectionMs = watch.ElapsedMilliseconds;
        TestContext.Current.TestOutputHelper?.WriteLine($"analysis_1000_events_ms={analysisMs}; projection_and_search_1000_incidents_ms={projectionMs}");
        Assert.Equal(1000, result.Incidents.Count); Assert.Single(vm.FilteredRows);
        Assert.InRange(analysisMs, 0, 10_000); Assert.InRange(projectionMs, 0, 5_000);
    }

    [Fact]
    public async Task SevenDayReadinessInventoryAndLargeHistory_AreBounded()
    {
        var services = new TestServices { Result = SyntheticResults.Create(1000), History = Enumerable.Range(0, 1000)
            .Select(i => new StoredScan(i.ToString(CultureInfo.InvariantCulture), DateTimeOffset.UtcNow.AddDays(-i), DateTimeOffset.UtcNow.AddDays(-i), "1", null, [])).ToArray() };
        using var vm = new MainViewModel(services); vm.Period = AnalysisPeriod.Week;
        var watch = Stopwatch.StartNew(); await vm.AnalyzeAsync(); var sevenDayMs = watch.ElapsedMilliseconds;
        watch.Restart(); await vm.RefreshReadinessAsync(); await vm.RefreshInventoryAsync(); var readinessInventoryMs = watch.ElapsedMilliseconds;
        watch.Restart(); await vm.RefreshHistoryAsync(); var historyMs = watch.ElapsedMilliseconds;
        TestContext.Current.TestOutputHelper?.WriteLine($"seven_day_analysis_ms={sevenDayMs}; readiness_and_inventory_ms={readinessInventoryMs}; history_1000_entries_ms={historyMs}");
        Assert.Equal(1000, vm.History.Count); Assert.Equal(1000, vm.Result.Incidents.Count);
        Assert.InRange(sevenDayMs, 0, 5_000); Assert.InRange(readinessInventoryMs, 0, 5_000); Assert.InRange(historyMs, 0, 5_000);
    }

    [Fact]
    public async Task ActionJournal_1000Entries_LoadsWithinBound()
    {
        var entries = Enumerable.Range(0, 1000).Select(i => new CaptureJournalEntry(Guid.NewGuid(), CaptureOperation.ConfigureApplicationCrashDump,
            "Example.exe", DateTimeOffset.UtcNow.AddSeconds(-i), true, new LocalDumpState(false), CrashCapturePolicy.Desired(new LocalDumpState(false)),
            CaptureResultCode.Success, CrashCapturePolicy.Desired(new LocalDumpState(false)), true, "NotRequested")).ToArray();
        var workflow = new CaptureWorkflow(new PerfCaptureService(), new PerfJournal(entries));
        var watch = Stopwatch.StartNew(); await workflow.RefreshAsync();
        TestContext.Current.TestOutputHelper?.WriteLine($"action_journal_1000_entries_ms={watch.ElapsedMilliseconds}");
        Assert.Equal(1000, workflow.Entries.Count); Assert.InRange(watch.ElapsedMilliseconds, 0, 5_000);
    }

    private sealed class PerfRule : IDiagnosticRule
    {
        public string RuleId => "perf"; public string Version => "1"; public IncidentCategory Category => IncidentCategory.ApplicationCrash;
        public bool AppliesTo(NormalizedEvent _) => true; public Finding Evaluate(NormalizedEvent a, IReadOnlyList<NormalizedEvent> _) =>
            new(RuleId, Version, FindingDisposition.Significant, EvidenceStrength.Limited, "o", "i", "n", [], [], []);
        public string BuildSignature(NormalizedEvent e) => e.Id.ToString();
    }
    private sealed class PerfJournal(IReadOnlyList<CaptureJournalEntry> entries) : ICaptureJournal
    { public Task SaveAsync(CaptureJournalEntry e, CancellationToken t) => Task.CompletedTask; public Task<IReadOnlyList<CaptureJournalEntry>> LoadAsync(CancellationToken t) => Task.FromResult(entries); }
    private sealed class PerfCaptureService : ICrashCaptureService
    { public Task<CaptureReadResult> ReadAsync(string e, CancellationToken t) => Task.FromResult(new CaptureReadResult(CaptureResultCode.Success, new LocalDumpState(false))); public Task<CaptureResult> ExecuteAsync(CaptureRequest r, CancellationToken t) => Task.FromResult(new CaptureResult(CaptureResultCode.Success)); }
}
