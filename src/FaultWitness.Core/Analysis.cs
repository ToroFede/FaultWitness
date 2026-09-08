namespace FaultWitness.Core;

public interface IDiagnosticRule
{
    string RuleId { get; }
    string Version { get; }
    bool AppliesTo(NormalizedEvent diagnosticEvent);
    Finding Evaluate(NormalizedEvent anchor, IReadOnlyList<NormalizedEvent> nearbyEvents);
    string BuildSignature(NormalizedEvent diagnosticEvent);
    TimeSpan CorrelationWindow => TimeSpan.FromMinutes(5);
    IncidentCategory Category => IncidentCategory.ApplicationCrash;
    bool RequirementsMet(NormalizedEvent anchor, RuleContext context) => true;
    bool IsRelated(NormalizedEvent anchor, NormalizedEvent other) => anchor.Id == other.Id;
    bool SameOccurrence(NormalizedEvent left, NormalizedEvent right) => left.Id == right.Id;
    IEnumerable<Evidence> CoverageEvidence(NormalizedEvent anchor, RuleContext context) => [];
}

public sealed record RuleContext(IReadOnlyList<NormalizedEvent> Nearby,
    IReadOnlyList<NormalizedEvent> History, IReadOnlyList<SourceCoverage> Coverage);

/// <summary>Occurrence identity is separate from recurrence. Windows are closed and anchored, never chained.</summary>
public sealed class IncidentAnalyzer(IEnumerable<IDiagnosticRule> rules)
{
    private readonly IReadOnlyList<IDiagnosticRule> rules = rules.ToList();

    public ScanResult Analyze(EventBatch batch, DateTimeOffset startedUtc, DateTimeOffset finishedUtc,
        CancellationToken cancellationToken = default)
    {
        var drafts = new List<Draft>();
        var events = batch.Events.OrderBy(static item => item.TimestampUtc).ToArray();
        foreach (var anchor in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var rule in rules)
            {
                if (!rule.AppliesTo(anchor)) continue;
                var nearby = Window(events, anchor.TimestampUtc, rule.CorrelationWindow);
                var context = new RuleContext(nearby, events, batch.Coverage);
                if (!rule.RequirementsMet(anchor, context)) continue;
                var related = nearby.Where(item => rule.IsRelated(anchor, item)).Append(anchor)
                    .DistinctBy(static item => item.Id).ToList();
                var finding = rule.Evaluate(anchor, related);
                var draft = drafts.Find(item => item.Rule.Category == rule.Category &&
                    rule.SameOccurrence(item.Anchor, anchor));
                if (draft is null)
                {
                    draft = new Draft(rule, anchor);
                    drafts.Add(draft);
                }
                draft.Findings.Add(finding);
                draft.Events.AddRange(related);
                draft.ExtraEvidence.AddRange(rule.CoverageEvidence(anchor, context));
            }
        }
        var incidents = drafts.Select(BuildIncident).OrderByDescending(static incident => incident.StartTimeUtc).ToList();
        var patterns = incidents.Where(static incident => incident.Findings.Any(finding => finding.Disposition is not FindingDisposition.Expected and not FindingDisposition.Suppressed))
            .GroupBy(static incident => (incident.Category, incident.Signature))
            .Where(static group => group.Count() > 1)
            .Select(static group => new RecurringPattern(group.Key.Signature, group.Key.Category,
                group.Select(static incident => incident.Id).ToArray())).ToArray();
        return new ScanResult(incidents, batch.Coverage, startedUtc, finishedUtc) { Patterns = patterns };
    }

    private static List<NormalizedEvent> Window(NormalizedEvent[] events, DateTimeOffset anchor, TimeSpan size)
    {
        var from = anchor - size;
        var to = anchor + size;
        var low = 0;
        var high = events.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (events[middle].TimestampUtc < from) low = middle + 1;
            else high = middle;
        }
        var result = new List<NormalizedEvent>();
        for (var index = low; index < events.Length && events[index].TimestampUtc <= to; index++) result.Add(events[index]);
        return result;
    }

    private static Incident BuildIncident(Draft draft)
    {
        var events = draft.Events.DistinctBy(static item => item.Id).OrderBy(static item => item.TimestampUtc).ToList();
        var relations = new List<EventRelation>();
        var evidence = events.Select(source =>
        {
            var same = draft.Rule.SameOccurrence(draft.Anchor, source);
            if (source.Id != draft.Anchor.Id)
            {
                if (same) relations.Add(new EventRelation(source.Id, draft.Anchor.Id,
                    source.SourceType == SourceType.Reliability ? RelationKind.DerivedFrom :
                    source.Field("ReportId").Length > 0 && string.Equals(source.Field("ReportId"), draft.Anchor.Field("ReportId"), StringComparison.OrdinalIgnoreCase)
                        ? RelationKind.SameReport : RelationKind.SameSignature));
                else if (source.TimestampUtc < draft.Anchor.TimestampUtc)
                    relations.Add(new EventRelation(source.Id, draft.Anchor.Id, RelationKind.Precedes));
            }
            return new Evidence(EvidenceKind.Positive, $"{source.SourceType}.{source.Channel}", "evidence.recorded",
                source.SourceReference, source, same ? draft.Anchor.Id.ToString() : source.Id.ToString());
        }).Concat(draft.ExtraEvidence).DistinctBy(static item => (item.Kind, item.Provenance, item.LocalizationKey)).ToList();
        var findings = draft.Findings.GroupBy(static item => item.RuleId)
            .Select(static group => group.MinBy(static item => item.Strength)!).ToList();
        return new Incident(Guid.NewGuid(), draft.Anchor.TimestampUtc, draft.Anchor.TimestampUtc,
            draft.Rule.Category, findings.Max(static item => item.Severity), draft.Anchor, evidence, findings,
            events, [], draft.Rule.BuildSignature(draft.Anchor)) { Relations = relations };
    }

    private sealed class Draft(IDiagnosticRule rule, NormalizedEvent anchor)
    {
        public IDiagnosticRule Rule { get; } = rule;
        public NormalizedEvent Anchor { get; } = anchor;
        public List<NormalizedEvent> Events { get; } = [];
        public List<Finding> Findings { get; } = [];
        public List<Evidence> ExtraEvidence { get; } = [];
    }
}
