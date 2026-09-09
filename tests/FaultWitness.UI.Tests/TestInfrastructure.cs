using Avalonia;
using Avalonia.Headless;
using FaultWitness.App;
using FaultWitness.Core;

[assembly: AvaloniaTestApplication(typeof(FaultWitness.UI.Tests.TestApplication))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FaultWitness.UI.Tests;

public static class TestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<FaultWitness.App.Application>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal sealed class TestServices : IAppServices
{
    public CaptureWorkflow? Capture { get; set; }
    public UserSettings Settings { get; set; } = new(Language: "en");
    public ScanResult Result { get; set; } = SyntheticResults.Create(3);
    public bool WaitForCancellation { get; set; }
    public bool RejectImport { get; set; }
    public bool FailHistory { get; set; }
    public bool SourceUnavailable { get; set; }
    public bool FailAnalysis { get; set; }
    public IReadOnlyList<DiagnosticReadinessItem>? StructuredReadiness { get; set; }
    public int Saved { get; private set; }
    public int Cleared { get; private set; }
    public DateTimeOffset From { get; private set; }
    public DateTimeOffset To { get; private set; }
    public async Task<ScanResult> AnalyzeAsync(DateTimeOffset from, DateTimeOffset endUtc, IProgress<string> progress, CancellationToken token)
    {
        From = from; To = endUtc; progress.Report("ReadingSources");
        if (FailAnalysis) throw new UnauthorizedAccessException("synthetic-private detail");
        if (WaitForCancellation) await Task.Delay(Timeout.Infinite, token);
        token.ThrowIfCancellationRequested();
        return Result;
    }
    public Task<ImportResult> ImportAsync(string path, CancellationToken token) => Task.FromResult(new ImportResult(
        new EventBatch(RejectImport ? [] : Result.Incidents.Select(item => item.AnchorEvent), [new(SourceType.Imported, CoverageState.Partial, null, null, "synthetic")]), RejectImport ? ["unsafe"] : []));
    public Task<ScanResult> AnalyzeImportedAsync(EventBatch batch, CancellationToken token) => Task.FromResult(Result with { Coverage = batch.Coverage });
    public Task<IReadOnlyList<DiagnosticReadinessItem>> ReadinessAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<DiagnosticReadinessItem>>(StructuredReadiness ??
        [new("SourceSystem", SourceUnavailable ? CoverageState.Unavailable : CoverageState.Complete, "synthetic"), new("SourceWer", CoverageState.Partial, "synthetic")]);
    public IReadOnlyDictionary<string, string> Inventory { get; set; } = new Dictionary<string, string> { ["OperatingSystem"] = "Synthetic Windows" };
    public Task<IReadOnlyDictionary<string, string>> InventoryAsync(CancellationToken token) => Task.FromResult(Inventory);
    public SystemInventorySnapshot? StructuredInventory { get; set; }
    public Task<SystemInventorySnapshot> SystemInventoryAsync(CancellationToken token) => Task.FromResult(StructuredInventory ?? new SystemInventorySnapshot([
        new InventoryGroup("InventoryGroupOperatingSystem", [new InventoryDevice("legacy", Inventory.Select(pair => new InventoryField(pair.Key, pair.Value)).ToArray())]) ]));
    public IReadOnlyList<FaultWitness.Storage.StoredScan> History { get; set; } = [];
    public Task SaveAsync(ScanResult result, int retentionDays, FaultWitness.Storage.ScanHistoryMetadata metadata, CancellationToken token)
    { if (FailHistory) throw new IOException("synthetic"); Saved++; return Task.CompletedTask; }
    public Task<IReadOnlyList<FaultWitness.Storage.StoredScan>> LoadHistoryAsync(CancellationToken token) => Task.FromResult(History);
    public Task ClearAsync(CancellationToken token) { Cleared++; return Task.CompletedTask; }
    public UserSettings LoadSettings() => Settings;
    public void SaveSettings(UserSettings settings) => Settings = settings;
    public CaptureWorkflow? CreateCaptureWorkflow() => Capture;
}

internal static class SyntheticResults
{
    public static ScanResult Create(int count)
    {
        var time = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var incidents = Enumerable.Range(0, count).Select(index => Incident(index, time)).ToArray();
        var coverage = new[] { new SourceCoverage(SourceType.EventLog, CoverageState.Complete, time.AddDays(-7), time.AddDays(1), "synthetic", "System"),
            new SourceCoverage(SourceType.Wer, CoverageState.Partial, time.AddDays(-7), time.AddDays(1), "synthetic"),
            new SourceCoverage(SourceType.CrashArtifact, CoverageState.AccessDenied, null, null, "synthetic") };
        return new ScanResult(incidents, coverage, time.AddDays(-7), time.AddDays(1))
        {
            Patterns = count > 2 ? [new("synthetic-recurring", IncidentCategory.Graphics, [incidents[0].Id, incidents[1].Id])] : []
        };
    }
    public static Incident Incident(int index, DateTimeOffset time)
    {
        var source = new NormalizedEvent(Guid.NewGuid(), SourceType.EventLog, "Windows", time.AddSeconds(index), "Application", "SyntheticProvider", 1000, 0,
            IncidentSeverity.High, index == 1 ? "SearchTarget.exe" : "Example.exe", 12, "Example.dll", "synthetic-device", new Dictionary<string, string> { ["UserName"] = "synthetic-private", ["OriginalRecordId"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            @"C:\Users\synthetic-private\record.xml", "<Event>synthetic raw</Event>");
        var finding = new Finding("graphics.engine_timeout", "0.9.1", index < 2 ? FindingDisposition.Significant : FindingDisposition.Context,
            index < 2 ? EvidenceStrength.Moderate : EvidenceStrength.Limited, "rule.graphics.engine_timeout.observed", "rule.graphics.engine_timeout.interpretation", "rule.graphics.engine_timeout.not_established", [], ["action.Graphics.investigate"], ["physical_gpu_failure"])
        { Severity = index == 0 ? IncidentSeverity.High : IncidentSeverity.Medium };
        return new Incident(Guid.NewGuid(), source.TimestampUtc, source.TimestampUtc, IncidentCategory.Graphics, finding.Severity, source,
            [new(EvidenceKind.Positive, "EventLog", "evidence.recorded", "synthetic", source, "observation"), new(EvidenceKind.Negative, "EventLog", "evidence.whea.not_observed", "synthetic"), new(EvidenceKind.Unknown, "CrashArtifact", "evidence.dump.unknown", "synthetic")],
            [finding], [source], [], "synthetic-" + index);
    }
}
