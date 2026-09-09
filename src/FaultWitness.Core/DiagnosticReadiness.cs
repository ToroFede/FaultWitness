namespace FaultWitness.Core;

public enum DiagnosticCapabilityStatus { Ready, Limited, Unavailable, AccessDenied, Disabled, NotSupported }

/// <summary>Only allowlisted configuration facts belong here; never raw registry paths or identifiers.</summary>
public sealed record DiagnosticObservation(string NameKey, string Value, bool ValueIsLocalizationKey = false);

/// <summary>A point-in-time diagnostic capability, not a health or stability assessment.</summary>
public sealed record DiagnosticReadinessItem(
    string Id,
    string NameKey,
    DiagnosticCapabilityStatus Status,
    string DetailKey,
    IReadOnlyList<DiagnosticObservation>? Observations = null,
    bool ElevationMayHelp = false,
    string? NextActionKey = null)
{
    public DiagnosticReadinessItem(string nameKey, CoverageState state, string detailKey)
        : this(nameKey, nameKey, state switch
        {
            CoverageState.Complete => DiagnosticCapabilityStatus.Ready,
            CoverageState.Partial => DiagnosticCapabilityStatus.Limited,
            CoverageState.AccessDenied => DiagnosticCapabilityStatus.AccessDenied,
            CoverageState.NotSupported => DiagnosticCapabilityStatus.NotSupported,
            _ => DiagnosticCapabilityStatus.Unavailable
        }, detailKey) { }
}
