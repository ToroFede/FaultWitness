using System.ComponentModel;
using FaultWitness.Core;
using FaultWitness.Export;
using FaultWitness.Localization;

namespace FaultWitness.App.Presentation;

/// <summary>Lossless presentation of an existing incident. Never evaluates rules or ranks evidence/actions.</summary>
public sealed class IncidentDetailPresentation : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public Incident Incident { get; private set; } = null!;
    public LocalizedLabels Text { get; private set; } = new(new LocalizationService());
    public string Title { get; private set; } = string.Empty;
    public string ObservedSummary { get; private set; } = string.Empty;
    public string Metadata { get; private set; } = string.Empty;
    public string Recurrence { get; private set; } = string.Empty;
    public string BestNextStep { get; private set; } = string.Empty;
    public IReadOnlyList<string> ActionKeys { get; private set; } = [];
    public IReadOnlyList<string> Interpretations { get; private set; } = [];
    public IReadOnlyList<string> Limitations { get; private set; } = [];
    public IReadOnlyList<string> Hypotheses { get; private set; } = [];
    public IReadOnlyList<string> OtherActions { get; private set; } = [];
    public IReadOnlyList<EvidenceGroupPresentation> EvidenceGroups { get; private set; } = [];
    public IReadOnlyList<EventRecordPresentation> TimelineRows { get; private set; } = [];
    public IReadOnlyList<EventRecordPresentation> TechnicalRows { get; private set; } = [];
    public IReadOnlyList<CoverageRowPresentation> CoverageRows { get; private set; } = [];
    public IReadOnlyList<string> CoverageLimitations { get; private set; } = [];
    public string TimelineTitle { get; private set; } = string.Empty;
    public string ChangeSummary { get; private set; } = string.Empty;
    public IReadOnlyList<string> ChangeNotes { get; private set; } = [];
    public IReadOnlyList<ChangeRowPresentation> RelevantChanges { get; private set; } = [];
    public IReadOnlyList<ChangeRowPresentation> LowChanges { get; private set; } = [];
    public IReadOnlyList<string> ChangeCoverage { get; private set; } = [];
    public string FirstLatest { get; private set; } = string.Empty;
    public string ReportContext { get; private set; } = string.Empty;
    public string TechnicalContext { get; private set; } = string.Empty;
    public bool HasPattern { get; private set; }
    public bool HasHypotheses => Hypotheses.Count > 0;
    public bool HasOtherActions => OtherActions.Count > 0;
    public bool HasLowChanges => LowChanges.Count > 0;
    public bool HasNoCoverage => CoverageRows.Count == 0;
    public bool HasCoverageLimitations => CoverageLimitations.Count > 0;

    public void Refresh(IncidentRow row, ScanResult result, LocalizationService text, string origin)
    {
        Incident = row.Incident;
        Text = new(text);
        Title = row.Title;
        ObservedSummary = row.Assessment;
        Metadata = row.Timestamp + " · " + row.Strength + " · " + origin;
        var findings = Incident.Findings.Where(item => item.Disposition != FindingDisposition.Suppressed).ToArray();
        // Preserve the previous distinct key order. The first action is not rescored or replaced.
        ActionKeys = findings.SelectMany(item => item.RecommendedActionKeys).Distinct().ToArray();
        BestNextStep = ActionKeys.Count == 0 ? text.Get("NoActionAvailable") : text.Get(ActionKeys[0]);
        OtherActions = ActionKeys.Skip(1).Select(text.Get).ToArray();
        Interpretations = findings.Select(item => text.Get(item.InterpretationKey)).Distinct().ToArray();
        Limitations = findings.Select(item => text.Get(item.NotEstablishedKey)).Distinct().ToArray();
        Hypotheses = findings.SelectMany(item => item.HypothesisKeys).Distinct().Select(text.Get).ToArray();
        // Show every item, including all negative/unknown items. No top-N heuristic or evidence deduplication.
        EvidenceGroups = Enum.GetValues<EvidenceKind>().Select(kind => new EvidenceGroupPresentation(kind,
            (kind == EvidenceKind.Positive ? "+" : kind == EvidenceKind.Negative ? "−" : "?") + "  " + text.Get("Evidence" + kind),
            Incident.Evidence.Where(item => item.Kind == kind).Select(item => new EvidenceItemPresentation(item,
                EvidencePresentation.Describe(item, result.Coverage, text))).ToArray(), text.Get("NoEvidenceInGroup"))).ToArray();
        TimelineRows = Incident.SourceEvents.OrderBy(item => item.TimestampUtc).Select(item => Record(item, text, true)).ToArray();
        TechnicalRows = Incident.SourceEvents.Select(item => Record(item, text, false)).ToArray();
        TimelineTitle = text.Format("DetailTimelineCount", TimelineRows.Count);
        CoverageRows = result.Coverage.Select(source => new CoverageRowPresentation(source,
            text.Get(PresentationPolicy.SourceKey(source)), text.Get("Coverage" + source.State),
            text.Get("CoverageHelp" + source.State), source.ExaminedFromUtc is null ? text.Get("IntervalUnknown") :
                text.Format("IntervalValue", source.ExaminedFromUtc.Value.ToLocalTime().ToString("g", text.Culture),
                    source.ExaminedToUtc?.ToLocalTime().ToString("g", text.Culture) ?? text.Get("NotAvailable")))).ToArray();
        CoverageLimitations = CoverageRows.Where(item => item.Source.State != CoverageState.Complete)
            .Select(item => item.Name + " — " + item.State).Distinct().ToArray();
        if (CoverageRows.Count == 0) CoverageLimitations = [text.Get("CoverageNotChecked")];
        var pattern = result.Patterns.FirstOrDefault(item => item.IncidentIds.Contains(Incident.Id));
        HasPattern = pattern is not null;
        var occurrences = pattern is null ? [] : result.Incidents.Where(item => pattern.IncidentIds.Contains(item.Id)).ToArray();
        Recurrence = occurrences.Length > 1 ? text.Format("RecurringCount", occurrences.Length) : string.Empty;
        FirstLatest = occurrences.Length == 0 ? string.Empty : text.Format("FirstLatest",
            occurrences.Min(item => item.StartTimeUtc).ToLocalTime().ToString("g", text.Culture),
            occurrences.Max(item => item.StartTimeUtc).ToLocalTime().ToString("g", text.Culture));
        ReportContext = text.Get(row.SharedReportCount > 1 ? "SharedReportHelp" : "SeparateOccurrence") +
            (row.SharedReportCount > 1 ? " " + row.SharedReport : string.Empty);
        TechnicalContext = ReportContext + (row.DevelopmentContext.Length > 0 ? " " + row.DevelopmentContext : string.Empty);
        ProjectChanges(text);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private EventRecordPresentation Record(NormalizedEvent item, LocalizationService text, bool timeline)
    {
        var relation = item.Id == Incident.AnchorEvent.Id ? text.Get("Anchor") :
            Incident.Relations.FirstOrDefault(link => link.From == item.Id || link.To == item.Id) is { } link
                ? text.Get("Relation" + link.Kind) : text.Get("Context");
        var technical = string.Join(Environment.NewLine, new[]
        {
            text.Get("DetailProvider") + ": " + item.Provider, text.Get("DetailChannel") + ": " + item.Channel,
            text.Get("DetailEventId") + ": " + item.EventId, text.Get("DetailRecordId") + ": " + item.Field("OriginalRecordId"),
            "UTC: " + item.TimestampUtc.ToString("O"), text.Get("LocalTime") + ": " + item.TimestampUtc.ToLocalTime().ToString("O")
        }.Concat(item.Fields.Select(pair => pair.Key + ": " + pair.Value)));
        return new(item, timeline ? item.TimestampUtc.ToLocalTime().ToString("T", text.Culture) + " · " +
            EvidencePresentation.FriendlyEvent(item, text) : item.Provider + " / " + item.EventId,
            relation + " · " + text.Get("SourceType" + item.SourceType), technical, item.RawData ?? text.Get("RawUnavailable"), text.Get("RawXml"));
    }

    private void ProjectChanges(LocalizationService text)
    {
        var changes = Incident.RelatedChanges.OrderBy(item => item.Relevance).ThenBy(item => item.OffsetFromFirstObservation.Duration()).ToArray();
        ChangeRowPresentation Project(RelatedSystemChange item) => new(item, ChangePresentation.RowText(item, text),
            ChangePresentation.Details(item, text), text.Get("TechnicalDetails"));
        RelevantChanges = changes.Where(item => item.Relevance != ContextualRelevance.Low).Select(Project).ToArray();
        LowChanges = changes.Where(item => item.Relevance == ContextualRelevance.Low).Select(Project).ToArray();
        var notes = new List<string>();
        if (Incident.ChangeContext is not { } context)
        {
            ChangeSummary = text.Get("ChangesNotExamined");
            ChangeNotes = [ChangeSummary];
            ChangeCoverage = [];
            return;
        }
        var limited = context.Coverage.Count == 0 || context.Coverage.Any(item => item.State != CoverageState.Complete);
        var unavailable = context.Coverage.Count > 0 && context.Coverage.All(item => item.State is CoverageState.Unavailable or CoverageState.AccessDenied or CoverageState.NotSupported);
        ChangeSummary = RelevantChanges.Count > 0 ? text.Format("DetailRelevantChanges", RelevantChanges.Count) : text.Get("NoRelevantChanges");
        if (unavailable) ChangeSummary = (RelevantChanges.Count > 0 ? ChangeSummary + " · " : string.Empty) + text.Get("ChangesUnavailable");
        else if (limited) ChangeSummary += " · " + text.Get("ChangesCoverageIncomplete");
        notes.Add(text.Get("ChangesDisclaimer"));
        notes.Add(text.Format("FirstObservedValue", context.FirstObservedUtc.ToLocalTime().ToString("g", text.Culture), text.Get("FirstObservedBasis" + context.Basis)));
        notes.Add(text.Get(context.IsRecurring ? "ChangesRecurring" : "ChangesNotRecurring"));
        if (RelevantChanges.Count == 0) notes.Add(text.Get("NoRelevantChanges"));
        if (limited) notes.Add(text.Get("ChangesCoverageIncomplete"));
        if (context.TotalChangeCount > changes.Length) notes.Add(text.Format("ChangesSummaryLimited", changes.Length, context.TotalChangeCount));
        notes.Add(text.Get("ChangesCoverageHelp"));
        ChangeNotes = notes;
        ChangeCoverage = context.Coverage.Select(source => ChangePresentation.CoverageText(source, text)).ToArray();
    }
}

public sealed class LocalizedLabels(LocalizationService service)
{
    public string this[string key] => service.Get(key);
}
public sealed record EvidenceItemPresentation(Evidence Source, string Description);
public sealed record EvidenceGroupPresentation(EvidenceKind Kind, string Heading, IReadOnlyList<EvidenceItemPresentation> Items, string EmptyText)
{
    public bool HasNoItems => Items.Count == 0;
}
public sealed record EventRecordPresentation(NormalizedEvent Source, string Heading, string Relation, string Technical, string Raw, string RawTitle);
public sealed record CoverageRowPresentation(SourceCoverage Source, string Name, string State, string Help, string Interval);
public sealed record ChangeRowPresentation(RelatedSystemChange Source, string Description, string Details, string DetailsTitle);
