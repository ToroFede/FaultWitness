using FaultWitness.Core;

namespace FaultWitness.Platform;

public interface IPlatformDiagnosticsProvider
{
    Task<EventBatch> ReadAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}

public interface IEventSource
{
    SourceType SourceType { get; }
    Task<EventBatch> ReadAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}

public interface ICrashArtifactSource
{
    Task<EventBatch> DiscoverAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}

public interface ISystemInformationProvider
{
    Task<IReadOnlyDictionary<string, string>> GetInventoryAsync(CancellationToken cancellationToken);
}

public interface IChangeHistoryProvider
{
    Task<IReadOnlyList<NormalizedEvent>> GetChangesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}

public interface IDiagnosticCapabilityProvider
{
    Task<IReadOnlyList<DiagnosticReadinessItem>> GetReadinessAsync(CancellationToken cancellationToken);
}

public interface IPrivilegedActionProvider
{
    Task<bool> EnableApplicationDumpsAsync(string executableName, bool fullDump, int maximumDumps, CancellationToken cancellationToken);
    Task<bool> RestoreApplicationDumpsAsync(string executableName, CancellationToken cancellationToken);
}
