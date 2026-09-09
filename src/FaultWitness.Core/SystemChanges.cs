using System.Text.Json.Serialization;

namespace FaultWitness.Core;

public enum ChangeCategory { DriverInstalled, WindowsUpdate }
public enum ChangeSubsystem { Unknown, Display, Audio, Storage, Network, System, Printer }
public enum ChangeQuality { Recorded, LocalTimeConverted }
public enum ContextualRelevance { High, Moderate, Low }
public enum ChangeTiming { Before, SameTime, After }
public enum FirstObservationBasis { CurrentScan, RetainedHistory }

/// <summary>Source-recorded facts, never a causal claim. Missing values are unknown, not inferred.</summary>
public sealed record SystemChange(string Id, string Platform, DateTimeOffset TimestampUtc,
    ChangeCategory Category, string Source, string Subject, ChangeSubsystem Subsystem,
    string? PreviousValue, string? NewValue, string? Vendor, string SourceReference,
    ChangeQuality Quality = ChangeQuality.Recorded)
{
    // Explicit device identity is used only in memory, never serialized into saved summaries or exports.
    [JsonIgnore] public string? ComponentIdentity { get; init; }
    public string? ClassId { get; init; }
    public string? UpdateIdentity { get; init; }
    public bool IsDefinitionUpdate { get; init; }
}

public sealed record RelatedSystemChange(SystemChange Change, ContextualRelevance Relevance,
    ChangeTiming Timing, TimeSpan OffsetFromFirstObservation, string ReasonKey);
public sealed record ChangeHistoryBatch(IReadOnlyList<SystemChange> Changes, IReadOnlyList<SourceCoverage> Coverage);
public sealed record ChangeHistoryContext(DateTimeOffset FirstObservedUtc, FirstObservationBasis Basis,
    bool IsRecurring, IReadOnlyList<SourceCoverage> Coverage)
{
    public int TotalChangeCount { get; init; }
}
public sealed record RetainedOccurrence(string Signature, IncidentCategory Category, DateTimeOffset TimestampUtc);
