using System.Globalization;
using FaultWitness.Core;
using FaultWitness.Localization;
using FaultWitness.Rules;
using FaultWitness.Storage;

namespace FaultWitness.App;

public enum AttentionLevel { Attention, Knowing, Background }
public enum AnalysisPeriod { Day, Week, Month, Custom }
public enum AppPage { Home, Analyze, Incidents, Detail, History, Readiness, System, Settings, Export }
public enum AnalysisMode { Recent, Around, Files }
public enum AppTheme { System, Light, Dark }
public enum ExportFormat { Summary, Html, Json, Bundle }
public sealed record ImportRow(string Path, string StatusKey);
public sealed class HistoryRow
{
    private readonly LocalizationService text;
    public HistoryRow(StoredScan scan, LocalizationService text) { Scan = scan; this.text = text; }
    public StoredScan Scan { get; }
    private StoredScan scan => Scan;
    public string Title => text.Get(scan.Metadata?.AnalysisType switch { "around" => "AnalyzeCrashFreeze", "imported" => "AnalyzeFiles", _ => "AnalyzeRecent" });
    public string Timestamp => scan.FinishedUtc.ToLocalTime().ToString("G", text.Culture);
    public string Period => scan.Metadata?.RequestedFromUtc is null ? text.Get("HistoryPeriodUnavailable") : text.Format("HistoryPeriodValue", scan.Metadata.RequestedFromUtc.Value.ToLocalTime().ToString("g", text.Culture), scan.Metadata.RequestedToUtc?.ToLocalTime().ToString("g", text.Culture) ?? text.Get("NotAvailable"));
    public string Counts => scan.Metadata?.NeedsAttention is null ? text.Format("HistoryIncidentCount", scan.Incidents.Count) : text.Format("HistoryCounts", scan.Metadata.NeedsAttention ?? 0, scan.Metadata.WorthKnowing ?? 0, scan.Metadata.Background ?? 0);
}

/// <summary>Presentation only. Never changes diagnostic findings, evidence or occurrence identity.</summary>
public static class PresentationPolicy
{
    private static readonly HashSet<string> DevelopmentExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "FaultWitness.UI.Tests.exe", "FaultWitness.Core.Tests.exe", "FaultWitness.Rules.Tests.exe",
        "FaultWitness.Platform.Windows.Tests.exe", "FaultWitness.Export.Tests.exe", "FaultWitness.Design.Tests.exe"
    };
    // A filename hint only: no publisher assertion and no effect on diagnostic priority.
    public static bool HasDevelopmentExecutableName(string? process) => !string.IsNullOrWhiteSpace(process) &&
        DevelopmentExecutables.Contains(Path.GetFileName(process.Replace('\\', '/')));
    public static string? ReportReferenceKey(Incident incident)
    {
        var report = DiagnosticFacts.ReportId(incident.AnchorEvent).Trim();
        if (report.Length == 0 || report.Contains("redacted", StringComparison.OrdinalIgnoreCase) ||
            (Guid.TryParse(report, out var id) && id == Guid.Empty)) return null;
        return incident.AnchorEvent.Platform + "|" + incident.Category + "|" + report.ToUpperInvariant();
    }
    public static AttentionLevel Classify(Incident incident, int exactOccurrenceCount = 1)
    {
        var significant = incident.Findings.Where(item => item.Disposition == FindingDisposition.Significant).ToArray();
        if (significant.Length == 0) return AttentionLevel.Background;
        if (significant.Any(item => item.Severity == IncidentSeverity.High && item.Strength <= EvidenceStrength.Moderate))
            return AttentionLevel.Attention;
        // An observed unclean restart is a user-visible disruption, even when its cause is unknown.
        if (significant.Any(item => item.RuleId == "power.unclean_shutdown" && item.Strength != EvidenceStrength.Insufficient))
            return AttentionLevel.Knowing;
        var supported = significant.Where(item => item.Severity >= IncidentSeverity.Medium && item.Strength <= EvidenceStrength.Moderate).ToArray();
        // Core's exact recurrence is meaningful; same category, similar wording and duplicate provenance are not.
        if (supported.Length > 0 && exactOccurrenceCount > 1) return AttentionLevel.Knowing;
        return supported.Any(item => item.RuleId is "graphics.tdr_with_driver_event" or "graphics.repeated_timeout_pattern" or
            "hardware.whea.recurrent_corrected" or "application.repeated_signature" or "audio.repeated_apo_failure" or
            "storage.repeated_timeout_pattern" or "service.repeated_failure" or "resources.exhaustion_with_hang_or_crash")
            ? AttentionLevel.Knowing : AttentionLevel.Background;
    }
    public static string SourceKey(SourceCoverage source) => source.SourceType switch
    {
        SourceType.EventLog => source.Channel == "System" ? "SourceSystem" : source.Channel == "Application" ? "SourceApplication" : "SourceEventLog",
        SourceType.Wer => "SourceWer", SourceType.Reliability => "SourceReliability", SourceType.CrashArtifact => "SourceArtifacts",
        SourceType.Imported => "ImportedData", _ => "SourceUnavailable"
    };
    public static DateTimeOffset LocalTime(DateTimeOffset utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(utc, zone);
}

public sealed class IncidentRow
{
    public IncidentRow(Incident incident, LocalizationService text, int recurrenceCount, int sharedReportCount = 1)
    {
        Incident = incident;
        Priority = PresentationPolicy.Classify(incident, recurrenceCount);
        PriorityText = text.Get("Priority" + Priority);
        Title = text.Get("Category" + incident.Category);
        Timestamp = incident.StartTimeUtc.ToLocalTime().ToString("G", text.Culture);
        Strength = text.Format("EvidenceValue", text.Get("Strength" + incident.EvidenceStrength));
        var primary = incident.Findings.Where(item => item.Disposition != FindingDisposition.Suppressed)
            .OrderBy(item => item.Disposition).ThenBy(item => item.Strength).FirstOrDefault();
        Assessment = primary is null ? text.Get("BackgroundDescription") : text.Get(primary.ObservedKey);
        Context = string.Join(" · ", new[] { incident.AnchorEvent.Process, incident.AnchorEvent.Module, incident.AnchorEvent.Device }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => Path.GetFileName(value!.Replace('\\', '/'))));
        if (Context.Length == 0) Context = text.Get("Priority" + Priority);
        DevelopmentContext = PresentationPolicy.HasDevelopmentExecutableName(incident.AnchorEvent.Process) ? text.Get("DevelopmentProcessContext") : string.Empty;
        RecurrenceCount = recurrenceCount;
        Recurrence = recurrenceCount > 1 ? text.Format("RecurringCount", recurrenceCount) : string.Empty;
        SharedReportCount = sharedReportCount;
        SharedReport = sharedReportCount > 1 ? text.Format("SharedReportCount", sharedReportCount) : string.Empty;
        SearchText = string.Join(" ", incident.SourceEvents.SelectMany(item => new[] { item.Process, item.Module, item.Provider, item.Device })
            .Concat(incident.Findings.SelectMany(item => new[] { item.RuleId, text.Get(item.ObservedKey), text.Get(item.InterpretationKey) }))
            .Append(Title));
    }
    public Incident Incident { get; }
    public AttentionLevel Priority { get; }
    public string PriorityText { get; }
    public string Title { get; }
    public string Timestamp { get; }
    public string Strength { get; }
    public string Assessment { get; }
    public string Context { get; }
    public string DevelopmentContext { get; }
    public string Recurrence { get; }
    public int RecurrenceCount { get; }
    public int SharedReportCount { get; }
    public string SharedReport { get; }
    public string SearchText { get; }
    public override string ToString() => Title + " · " + Timestamp + " · " + Strength;
}

public sealed record IncidentFilter(AttentionLevel? Priority = null, IncidentCategory? Category = null,
    EvidenceStrength? Strength = null, string Search = "", DateTimeOffset? From = null, DateTimeOffset? To = null,
    IReadOnlySet<Guid>? Occurrences = null)
{
    public bool Matches(IncidentRow row) => (Priority is null || Priority == row.Priority) &&
        (Category is null || Category == row.Incident.Category) && (Strength is null || Strength == row.Incident.EvidenceStrength) &&
        (From is null || row.Incident.StartTimeUtc >= From) && (To is null || row.Incident.StartTimeUtc <= To) &&
        (Occurrences is null || Occurrences.Contains(row.Incident.Id)) &&
        (string.IsNullOrWhiteSpace(Search) || row.SearchText.Contains(Search.Trim(), StringComparison.CurrentCultureIgnoreCase));
}
