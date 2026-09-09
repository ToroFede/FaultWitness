namespace FaultWitness.Core;

/// <summary>Attaches temporal context only. Diagnostic findings, evidence and relations are never changed.</summary>
public static class ChangeCorrelator
{
    public static readonly TimeSpan MaximumLookback = TimeSpan.FromDays(7);
    public static readonly TimeSpan MaximumLookahead = TimeSpan.FromDays(1);

    public static IReadOnlyDictionary<Guid, ChangeHistoryContext> FirstObservations(ScanResult scan,
        IEnumerable<RetainedOccurrence> retained)
    {
        var history = retained.GroupBy(item => (item.Category, item.Signature))
            .ToDictionary(group => group.Key, group => group.Select(item => item.TimestampUtc).Distinct().ToArray());
        var result = new Dictionary<Guid, ChangeHistoryContext>();
        foreach (var group in scan.Incidents.Where(Eligible).GroupBy(item => (item.Category, item.Signature)))
        {
            var currentFirst = group.Min(item => item.StartTimeUtc);
            var earlier = history.GetValueOrDefault(group.Key) ?? [];
            var times = group.Select(item => item.StartTimeUtc).Concat(earlier).Distinct().ToArray();
            var first = times.Min();
            // Use the engine's exact pattern identity, with retained exact matches extending available observations.
            var recurring = scan.Patterns.Any(pattern => pattern.Category == group.Key.Category && pattern.Signature == group.Key.Signature)
                || times.Length > 1;
            foreach (var incident in group)
                result[incident.Id] = new(first, first < currentFirst ? FirstObservationBasis.RetainedHistory : FirstObservationBasis.CurrentScan, recurring, []);
        }
        return result;
    }

    public static ScanResult Attach(ScanResult scan, IEnumerable<RetainedOccurrence> retained,
        ChangeHistoryBatch batch, CancellationToken token = default)
    {
        var onsets = FirstObservations(scan, retained);
        var changes = Deduplicate(batch.Changes);
        return scan with { Incidents = scan.Incidents.Select(incident =>
        {
            token.ThrowIfCancellationRequested();
            if (!onsets.TryGetValue(incident.Id, out var context)) return incident;
            var from = context.FirstObservedUtc - MaximumLookback;
            var to = context.FirstObservedUtc + MaximumLookahead;
            var coverage = batch.Coverage.Where(item =>
                (item.ExaminedFromUtc is null || item.ExaminedFromUtc <= to) &&
                (item.ExaminedToUtc is null || item.ExaminedToUtc >= from)).ToArray();
            var related = changes.Where(change => WithinWindow(change, context.FirstObservedUtc))
                .Select(change => Relate(incident, change, context.FirstObservedUtc))
                .OrderBy(item => item.Relevance).ThenBy(item => item.OffsetFromFirstObservation.Duration())
                .ThenBy(item => item.Change.Id, StringComparer.Ordinal).ToArray();
            return incident with { RelatedChanges = related, ChangeContext = context with { Coverage = coverage, TotalChangeCount = related.Length } };
        }).ToArray() };
    }

    private static bool Eligible(Incident incident) => incident.Findings.Any(finding =>
        finding.Disposition is not FindingDisposition.Expected and not FindingDisposition.Suppressed);

    private static bool WithinWindow(SystemChange change, DateTimeOffset first)
    {
        var (before, after) = change.Category switch
        {
            ChangeCategory.DriverInstalled => (MaximumLookback, MaximumLookahead),
            ChangeCategory.WindowsUpdate => (TimeSpan.FromDays(3), MaximumLookahead),
            _ => (TimeSpan.Zero, TimeSpan.Zero)
        };
        return change.TimestampUtc >= first - before && change.TimestampUtc <= first + after;
    }

    private static RelatedSystemChange Relate(Incident incident, SystemChange change, DateTimeOffset first)
    {
        var offset = change.TimestampUtc - first;
        var timing = offset < TimeSpan.Zero ? ChangeTiming.Before : offset > TimeSpan.Zero ? ChangeTiming.After : ChangeTiming.SameTime;
        var subsystem = incident.Category switch
        {
            IncidentCategory.Graphics => ChangeSubsystem.Display,
            IncidentCategory.Audio => ChangeSubsystem.Audio,
            IncidentCategory.Storage => ChangeSubsystem.Storage,
            _ => ChangeSubsystem.Unknown
        };
        var sameSubsystem = subsystem != ChangeSubsystem.Unknown && change.Subsystem == subsystem;
        // Match explicit IDs, not prose, vendor substrings, or generic device descriptions.
        var deviceId = incident.AnchorEvent.Field("DeviceInstanceId");
        var sameDevice = deviceId.Length > 0 && !string.IsNullOrEmpty(change.ComponentIdentity) &&
            string.Equals(deviceId, change.ComponentIdentity, StringComparison.OrdinalIgnoreCase);
        var conflictingDevice = deviceId.Length > 0 && !string.IsNullOrEmpty(change.ComponentIdentity) && !sameDevice;
        var relevance = ContextualRelevance.Low;
        var reason = "ChangeReasonOtherSubsystem";
        if (!change.IsDefinitionUpdate && !conflictingDevice && (sameSubsystem || sameDevice))
        {
            relevance = offset.Duration() <= TimeSpan.FromDays(2) && !conflictingDevice
                ? ContextualRelevance.High : ContextualRelevance.Moderate;
            reason = sameDevice ? "ChangeReasonSameDevice" : "ChangeReasonSameSubsystem";
        }
        else if (change.Category == ChangeCategory.WindowsUpdate && !change.IsDefinitionUpdate && !conflictingDevice)
        {
            relevance = ContextualRelevance.Moderate;
            reason = "ChangeReasonSystemUpdate";
        }
        if (timing == ChangeTiming.After)
        {
            relevance = ContextualRelevance.Low;
            reason = "ChangeReasonAfter";
        }
        return new(change, relevance, timing, offset, reason);
    }

    public static IReadOnlyList<SystemChange> Deduplicate(IEnumerable<SystemChange> changes)
    {
        var result = new List<SystemChange>();
        foreach (var change in changes.OrderBy(item => item.TimestampUtc).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            if (result.Any(existing => existing.Id == change.Id || SameOperation(existing, change))) continue;
            result.Add(change);
        }
        return result;
    }

    private static bool SameOperation(SystemChange left, SystemChange right)
    {
        if (left.Platform != right.Platform || left.Category != right.Category ||
            (left.TimestampUtc - right.TimestampUtc).Duration() > TimeSpan.FromMinutes(2)) return false;
        // Identical records can reappear under another ID; distinct same-source operations keep their identity.
        if (left.Source == right.Source) return left.SourceReference == right.SourceReference;
        if (left.Category == ChangeCategory.WindowsUpdate)
            return !string.IsNullOrEmpty(left.UpdateIdentity) && left.UpdateIdentity == right.UpdateIdentity;
        return !string.IsNullOrEmpty(left.ComponentIdentity) && !string.IsNullOrEmpty(left.NewValue) &&
            string.Equals(left.ComponentIdentity, right.ComponentIdentity, StringComparison.OrdinalIgnoreCase) &&
            left.NewValue == right.NewValue && left.ClassId == right.ClassId;
    }
}
