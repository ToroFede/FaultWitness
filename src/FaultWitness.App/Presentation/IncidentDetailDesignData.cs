using FaultWitness.Core;
using FaultWitness.Localization;
using FaultWitness.Storage;

namespace FaultWitness.App.Presentation;

/// <summary>In-memory preview data. No filesystem, Windows source, settings store or privileged service is accessed.</summary>
public static class IncidentDetailDesignData
{
    public static ScanResult Result
    {
        get
        {
            var time = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
            var record = new NormalizedEvent(Guid.Parse("11111111-1111-1111-1111-111111111111"), SourceType.EventLog,
                "Windows", time, "System", "Microsoft-Windows-Kernel-Power", 41, 0, IncidentSeverity.High,
                null, null, null, null, new Dictionary<string, string> { ["BugcheckCode"] = "0" }, "synthetic:41");
            var finding = new Finding("power.unclean_shutdown", "0.9.1", FindingDisposition.Significant, EvidenceStrength.Moderate,
                "rule.power.unclean_shutdown.observed", "rule.power.unclean_shutdown.interpretation", "rule.power.unclean_shutdown.not_established",
                [], ["action.Power.investigate"], []);
            var incident = new Incident(Guid.Parse("22222222-2222-2222-2222-222222222222"), time, time, IncidentCategory.Power,
                IncidentSeverity.Medium, record, [new(EvidenceKind.Positive, "EventLog.System", "evidence.recorded", "synthetic", record)],
                [finding], [record], [], "preview:power");
            return new([incident], [new(SourceType.EventLog, CoverageState.Partial, time.AddDays(-1), time, "synthetic", "System")], time.AddDays(-1), time);
        }
    }
    public static IncidentDetailPresentation Sample
    {
        get
        {
            var result = Result;
            var text = new LocalizationService();
            var projection = new IncidentDetailPresentation();
            projection.Refresh(new IncidentRow(result.Incidents[0], text, 1), result, text, text.Get("LocalSystem"));
            return projection;
        }
    }
}

internal sealed class IncidentDetailDesignServices : IAppServices
{
    public UserSettings LoadSettings() => new(Language: "en");
    public void SaveSettings(UserSettings settings) { }
    public Task<ScanResult> AnalyzeAsync(DateTimeOffset from, DateTimeOffset endUtc, IProgress<string> progress, CancellationToken token) => Task.FromResult(IncidentDetailDesignData.Result);
    public Task<ImportResult> ImportAsync(string path, CancellationToken token) => Task.FromResult(new ImportResult(new EventBatch([], []), []));
    public Task<ScanResult> AnalyzeImportedAsync(EventBatch batch, CancellationToken token) => Task.FromResult(IncidentDetailDesignData.Result);
    public Task<IReadOnlyList<DiagnosticReadinessItem>> ReadinessAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<DiagnosticReadinessItem>>([]);
    public Task<IReadOnlyDictionary<string, string>> InventoryAsync(CancellationToken token) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    public Task<SystemInventorySnapshot> SystemInventoryAsync(CancellationToken token) => Task.FromResult(new SystemInventorySnapshot([]));
    public Task SaveAsync(ScanResult result, int retentionDays, ScanHistoryMetadata metadata, CancellationToken token) => Task.CompletedTask;
    public Task<IReadOnlyList<StoredScan>> LoadHistoryAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<StoredScan>>([]);
    public Task ClearAsync(CancellationToken token) => Task.CompletedTask;
}
