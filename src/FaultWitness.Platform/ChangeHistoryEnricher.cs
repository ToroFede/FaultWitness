using FaultWitness.Core;


namespace FaultWitness.Platform;

/// <summary>Runs bounded source queries after diagnosis; imports never enter this local-machine pipeline.</summary>
public static class ChangeHistoryEnricher
{
    public static async Task<ScanResult> EnrichAsync(ScanResult scan, IReadOnlyList<RetainedOccurrence> retained,
        IChangeHistoryProvider provider, CancellationToken token, bool retainedHistoryAvailable = true)
    {
        var onsets = ChangeCorrelator.FirstObservations(scan, retained).Values.Select(item => item.FirstObservedUtc).Distinct().Order().ToArray();
        if (onsets.Length == 0) return scan;
        var windows = new List<(DateTimeOffset From, DateTimeOffset To)>();
        foreach (var onset in onsets)
        {
            var from = onset - ChangeCorrelator.MaximumLookback;
            var to = onset + ChangeCorrelator.MaximumLookahead;
            if (windows.Count > 0 && from <= windows[^1].To)
                windows[^1] = (windows[^1].From, to);
            else windows.Add((from, to));
        }
        var changes = new List<SystemChange>();
        var coverage = new List<SourceCoverage>();
        foreach (var (from, to) in windows)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var batch = await provider.GetChangesAsync(from, to, token).ConfigureAwait(false);
                changes.AddRange(batch.Changes);
                coverage.AddRange(batch.Coverage);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Optional contextual collection must not discard a completed diagnosis.
                coverage.Add(new(SourceType.ChangeHistory,
                    exception is UnauthorizedAccessException ? CoverageState.AccessDenied : CoverageState.Unavailable,
                    from, to, "ChangeCoverageUnavailable", "ChangeSourceLocalHistory"));
            }
        }
        if (!retainedHistoryAvailable)
            coverage.Add(new(SourceType.ChangeHistory, CoverageState.Unavailable, null, null,
                "ChangeRetainedHistoryUnavailable", "ChangeSourceFaultWitnessHistory"));
        return ChangeCorrelator.Attach(scan, retained, new(changes, coverage), token);
    }
}
