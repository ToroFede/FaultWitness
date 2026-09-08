using System.Collections.ObjectModel;

namespace FaultWitness.Core;

public enum CoverageState { Complete, Partial, Unavailable, AccessDenied, NotSupported }
public enum SourceType { EventLog, Wer, Reliability, CrashArtifact, Inventory, ChangeHistory, Imported }
public enum EvidenceKind { Positive, Negative, Unknown }
public enum EvidenceStrength { Strong, Moderate, Limited, Insufficient }
public enum IncidentCategory { Power, Graphics, Hardware, ApplicationCrash, ApplicationHang, Audio, Storage, Resources, PlugAndPlay, Service, BugCheck }
public enum IncidentSeverity { Informational, Low, Medium, High }
public enum FindingDisposition { Significant, Supporting, Context, Expected, Suppressed }

/// <summary>Platform-neutral representation of a diagnostic record.</summary>
public sealed record NormalizedEvent(
    Guid Id,
    SourceType SourceType,
    string Platform,
    DateTimeOffset TimestampUtc,
    string? Channel,
    string Provider,
    int EventId,
    int? EventVersion,
    IncidentSeverity Severity,
    string? Process,
    int? ProcessId,
    string? Module,
    string? Device,
    IReadOnlyDictionary<string, string> Fields,
    string SourceReference,
    string? RawData = null)
{
    public string Field(string key) => Fields.TryGetValue(key, out var value) ? value :
        Fields.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
}

public sealed record SourceCoverage(
    SourceType SourceType,
    CoverageState State,
    DateTimeOffset? ExaminedFromUtc,
    DateTimeOffset? ExaminedToUtc,
    string DetailKey,
    string? Channel = null);

public sealed record Evidence(
    EvidenceKind Kind,
    string Family,
    string LocalizationKey,
    string Provenance,
    NormalizedEvent? SourceEvent = null,
    string? ObservationId = null);

public sealed record Finding(
    string RuleId,
    string RuleVersion,
    FindingDisposition Disposition,
    EvidenceStrength Strength,
    string ObservedKey,
    string InterpretationKey,
    string NotEstablishedKey,
    IReadOnlyList<string> HypothesisKeys,
    IReadOnlyList<string> RecommendedActionKeys,
    IReadOnlyList<string> FalsePositiveContract)
{
    public IncidentSeverity Severity { get; init; } = IncidentSeverity.Medium;
}

public sealed record Incident(
    Guid Id,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    IncidentCategory Category,
    IncidentSeverity Severity,
    NormalizedEvent AnchorEvent,
    IReadOnlyList<Evidence> Evidence,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<NormalizedEvent> SourceEvents,
    IReadOnlyList<string> RelatedChanges,
    string Signature)
{
    public EvidenceStrength EvidenceStrength => Findings.Count == 0 ? EvidenceStrength.Insufficient : Findings.Min(static finding => finding.Strength);
    public bool IsHeadline => Findings.Any(static finding => finding.Disposition == FindingDisposition.Significant);
    public IReadOnlyList<EventRelation> Relations { get; init; } = [];
}

public sealed record ScanResult(
    IReadOnlyList<Incident> Incidents,
    IReadOnlyList<SourceCoverage> Coverage,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc)
{
    public static ScanResult Empty(DateTimeOffset now) => new([], [], now, now);
    public IReadOnlyList<RecurringPattern> Patterns { get; init; } = [];
}

public enum RelationKind { Precedes, SameReport, DerivedFrom, SameProcess, SameDevice, SameSignature }
public enum SignatureMatch { Exact, Related, SameCategory, Unrelated }
public sealed record EventRelation(Guid From, Guid To, RelationKind Kind);
public sealed record RecurringPattern(string Signature, IncidentCategory Category, IReadOnlyList<Guid> IncidentIds);

public sealed record ExportPrivacyOptions(bool RedactPersonalData = true, bool IncludeRawXml = false, bool IncludeDumps = false);
public sealed record RecommendedAction(string LocalizationKey, bool Reversible, bool RequiresAdmin, int ExpectedInformationGain, int EstimatedEffort);
public sealed record DiagnosticReadinessItem(string NameKey, CoverageState State, string DetailKey);
public sealed record ImportResult(EventBatch Batch, IReadOnlyList<string> Errors);

public sealed class EventBatch
{
    public EventBatch(IEnumerable<NormalizedEvent> events, IEnumerable<SourceCoverage> coverage)
    {
        Events = new ReadOnlyCollection<NormalizedEvent>(events.OrderBy(static item => item.TimestampUtc).ToList());
        Coverage = new ReadOnlyCollection<SourceCoverage>(coverage.ToList());
    }

    public IReadOnlyList<NormalizedEvent> Events { get; }
    public IReadOnlyList<SourceCoverage> Coverage { get; }
}
