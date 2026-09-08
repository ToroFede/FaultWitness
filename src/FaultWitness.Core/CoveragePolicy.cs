namespace FaultWitness.Core;

/// <summary>Absence requires complete coverage of the entire closed interval and exact source/channel.</summary>
public static class CoveragePolicy
{
    public static bool IsComplete(IEnumerable<SourceCoverage> coverage, SourceType source, string? channel,
        DateTimeOffset from, DateTimeOffset to)
    {
        var matching = coverage.Where(item => item.SourceType == source &&
            string.Equals(item.Channel, channel, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matching.Any(item => item.State != CoverageState.Complete &&
            (item.ExaminedFromUtc is null || item.ExaminedFromUtc <= to) &&
            (item.ExaminedToUtc is null || item.ExaminedToUtc >= from))) return false;
        return matching.Any(item => item.State == CoverageState.Complete &&
            item.ExaminedFromUtc <= from && item.ExaminedToUtc >= to);
    }

    public static Evidence Missing(IEnumerable<SourceCoverage> coverage, SourceType source, string? channel,
        DateTimeOffset from, DateTimeOffset to, string subject)
    {
        var complete = IsComplete(coverage, source, channel, from, to);
        return new Evidence(complete ? EvidenceKind.Negative : EvidenceKind.Unknown,
            $"{source}.{channel}", $"evidence.{subject}.{(complete ? "not_observed" : "unknown")}",
            $"{source}/{channel} [{from:O}, {to:O}]");
    }
}
