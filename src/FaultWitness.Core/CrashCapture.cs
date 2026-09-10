namespace FaultWitness.Core;

public enum CaptureOperation { ConfigureApplicationCrashDump = 1, RestoreApplicationCrashDumpConfiguration = 2 }
public enum CaptureResultCode { Success, CancelledByUser, AccessDenied, InvalidRequest, UnexpectedCurrentState, UnsupportedConfiguration, ApplyFailed, VerificationFailed, RollbackFailed, RegistryUnavailable, UnsupportedPlatform, Pending, AlreadyRestored, HelperUnavailable, HelperIncompatible }
public enum CaptureActiveState { Active, ConfigurationChanged, NotActive, UnsupportedConfiguration, CouldNotVerify }
public sealed record CaptureTarget(string ExecutableName, string? DisplayName = null);
public sealed record LocalDumpState(bool KeyExists, int? DumpType = null, int? DumpCount = null, string? DumpFolder = null, string OtherValuesFingerprint = "", bool Supported = true);
public sealed record CaptureReadResult(CaptureResultCode Code, LocalDumpState? State, int? NativeError = null);
public sealed record CaptureRequest(int SchemaVersion, Guid ActionId, CaptureOperation Operation, string TargetExecutable, DateTimeOffset CreatedUtc, LocalDumpState ExpectedState, LocalDumpState DesiredState);
public sealed record CaptureResult(CaptureResultCode Code, LocalDumpState? ObservedState = null, int? NativeError = null, bool RollbackAttempted = false, bool RollbackSucceeded = false);
public sealed record CaptureJournalEntry(Guid ActionId, CaptureOperation Operation, string TargetExecutable, DateTimeOffset TimestampUtc, bool ElevationRequired, LocalDumpState PreviousState, LocalDumpState RequestedState, CaptureResultCode Result = CaptureResultCode.Pending, LocalDumpState? ObservedState = null, bool RollbackAvailable = false, string RollbackStatus = "NotRequested", int? NativeError = null, int SchemaVersion = 1, string AppVersion = ReleaseIdentity.Assembly);
public interface ICaptureJournal
{
    Task SaveAsync(CaptureJournalEntry entry, CancellationToken token);
    Task<IReadOnlyList<CaptureJournalEntry>> LoadAsync(CancellationToken token);
}
public interface ICrashCaptureService
{
    Task<CaptureReadResult> ReadAsync(string executable, CancellationToken token);
    Task<CaptureResult> ExecuteAsync(CaptureRequest request, CancellationToken token);
}
public static class CrashCapturePolicy
{
    public const string Folder = @"%LOCALAPPDATA%\FaultWitness\Captures";
    public const int DumpType = 1;
    public const int DumpCount = 3;
    public static LocalDumpState Desired(LocalDumpState before) => new(true, DumpType, DumpCount, Folder, before.OtherValuesFingerprint);
    private static readonly string[] ReservedExecutableNames = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
    public static bool IsValidExecutable(string? name)
    {
        if (name is not { Length: > 4 and <= 128 } || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name[0] == '.' || name.Contains("..", StringComparison.Ordinal) ||
            !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')) return false;
        return !ReservedExecutableNames.Contains(name[..^4], StringComparer.OrdinalIgnoreCase);
    }
    public static bool IsSupportedState(LocalDumpState? state) => state is not null && state.Supported && state.OtherValuesFingerprint is not null && state.OtherValuesFingerprint.Length <= 64 && (state.OtherValuesFingerprint.Length == 0 || state.OtherValuesFingerprint.Length == 64 && state.OtherValuesFingerprint.All(Uri.IsHexDigit)) && (state.DumpType is null or 1 or 2) && (state.DumpCount is null or >= 1 and <= 10) && (state.DumpFolder is null or Folder or @"%LOCALAPPDATA%\CrashDumps") && (state.KeyExists || state.DumpType is null && state.DumpCount is null && state.DumpFolder is null && state.OtherValuesFingerprint.Length == 0);
    public static CaptureActiveState Active(CaptureReadResult read) => read.Code != CaptureResultCode.Success || read.State is null ? CaptureActiveState.CouldNotVerify : !read.State.Supported ? CaptureActiveState.UnsupportedConfiguration : read.State == Desired(read.State) ? CaptureActiveState.Active : read.State.KeyExists ? CaptureActiveState.ConfigurationChanged : CaptureActiveState.NotActive;
}

/// <summary>Bounded process-exit response: no writable result path or privileged IPC endpoint.</summary>
public static class CaptureExitProtocol
{
    public static int Encode(CaptureResult result) => (int)result.Code | (result.RollbackAttempted ? 1 << 8 : 0) |
        (result.RollbackSucceeded ? 1 << 9 : 0) | ((result.NativeError.GetValueOrDefault() & 0xffff) << 16);
    public static CaptureResult Decode(int value)
    {
        var code = (CaptureResultCode)(value & 0xff);
        if (!Enum.IsDefined(code) || code is CaptureResultCode.Pending or CaptureResultCode.AlreadyRestored || (value & 0xfc00) != 0)
            return new(CaptureResultCode.ApplyFailed);
        var native = (int)((uint)value >> 16);
        return new(code, NativeError: native == 0 ? null : native, RollbackAttempted: (value & (1 << 8)) != 0,
            RollbackSucceeded: (value & (1 << 9)) != 0);
    }
}
