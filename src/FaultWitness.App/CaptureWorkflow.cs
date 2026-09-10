using FaultWitness.Core;

namespace FaultWitness.App;

/// <summary>Explicit user-driven capture operations. A durable intent always precedes elevation.</summary>
public sealed class CaptureWorkflow(ICrashCaptureService service, ICaptureJournal journal)
{
    private string targetExecutable = string.Empty;
    private string? previewTarget;
    private int busy;
    public string TargetExecutable
    {
        get => targetExecutable;
        set { if (targetExecutable == value) return; targetExecutable = value; Preview = null; previewTarget = null; LastResult = null; }
    }
    public CaptureReadResult? Preview { get; private set; }
    public IReadOnlyList<CaptureJournalEntry> Entries { get; private set; } = [];
    public CaptureResult? LastResult { get; private set; }
    public bool IsBusy => busy != 0;
    public bool CanConfigure => !IsBusy && string.Equals(previewTarget, TargetExecutable, StringComparison.OrdinalIgnoreCase) &&
        CrashCapturePolicy.IsValidExecutable(TargetExecutable) &&
        Preview is { Code: CaptureResultCode.Success, State: { } state } && CrashCapturePolicy.IsSupportedState(state);
    public static bool CanRestore(CaptureJournalEntry entry) =>
        entry.Operation == CaptureOperation.ConfigureApplicationCrashDump && entry.RollbackStatus != "Complete" &&
        (entry.RollbackAvailable || entry.Result == CaptureResultCode.Pending);
    public CaptureActiveState ActiveState => Preview is null ? CaptureActiveState.CouldNotVerify : CrashCapturePolicy.Active(Preview);
    public event Action? Changed;

    public Task PreviewAsync() => RunAsync(async () =>
    {
        var target = TargetExecutable.ToLowerInvariant();
        Preview = await service.ReadAsync(target, CancellationToken.None);
        previewTarget = target;
        Entries = await journal.LoadAsync(CancellationToken.None);
    });
    public Task RefreshAsync() => RunAsync(async () =>
    {
        Entries = await journal.LoadAsync(CancellationToken.None);
        if (CrashCapturePolicy.IsValidExecutable(TargetExecutable))
        {
            previewTarget = TargetExecutable.ToLowerInvariant();
            Preview = await service.ReadAsync(previewTarget, CancellationToken.None);
        }
    });
    public Task ConfigureAsync() => RunAsync(async () =>
    {
        var target = TargetExecutable.ToLowerInvariant();
        if (previewTarget != target || Preview is not { Code: CaptureResultCode.Success, State: { } before } || !CrashCapturePolicy.IsValidExecutable(target))
        { LastResult = new(CaptureResultCode.InvalidRequest); return; }
        if (!CrashCapturePolicy.IsSupportedState(before)) { LastResult = new(CaptureResultCode.UnsupportedConfiguration); return; }
        var desired = CrashCapturePolicy.Desired(before);
        if (before == desired) { LastResult = new(CaptureResultCode.Success, before); return; }
        await ApplyAsync(new(Guid.NewGuid(), CaptureOperation.ConfigureApplicationCrashDump, target,
            DateTimeOffset.UtcNow, true, before, desired));
    });
    public Task RestoreAsync(Guid actionId) => RunAsync(async () =>
    {
        Entries = await journal.LoadAsync(CancellationToken.None);
        var original = Entries.FirstOrDefault(entry => entry.ActionId == actionId);
        if (original is null || original.Operation != CaptureOperation.ConfigureApplicationCrashDump)
        { LastResult = new(CaptureResultCode.InvalidRequest); return; }
        if (original.RollbackStatus == "Complete") { LastResult = new(CaptureResultCode.AlreadyRestored); return; }
        if (!original.RollbackAvailable && original.Result != CaptureResultCode.Pending)
        { LastResult = new(CaptureResultCode.InvalidRequest); return; }
        var current = await service.ReadAsync(original.TargetExecutable, CancellationToken.None);
        if (current.Code != CaptureResultCode.Success) { LastResult = new(current.Code, current.State, current.NativeError); return; }
        if (current.State != original.RequestedState)
        { LastResult = new(CaptureResultCode.UnexpectedCurrentState, current.State); return; }
        // A newer unreverted action for this target invalidates an old restore intent, even if values happen to match.
        if (Entries.Any(entry => entry.TargetExecutable == original.TargetExecutable && entry.TimestampUtc > original.TimestampUtc &&
            entry.Operation == CaptureOperation.ConfigureApplicationCrashDump && entry.Result is CaptureResultCode.Success or CaptureResultCode.Pending && entry.RollbackStatus != "Complete"))
        { LastResult = new(CaptureResultCode.UnexpectedCurrentState, current.State); return; }
        var restore = new CaptureJournalEntry(Guid.NewGuid(), CaptureOperation.RestoreApplicationCrashDumpConfiguration,
            original.TargetExecutable, DateTimeOffset.UtcNow, true, original.RequestedState, original.PreviousState);
        await ApplyAsync(restore);
        if (LastResult?.Code == CaptureResultCode.Success)
        {
            await journal.SaveAsync(original with { RollbackAvailable = false, RollbackStatus = "Complete" }, CancellationToken.None);
            Entries = await journal.LoadAsync(CancellationToken.None);
        }
    });
    private async Task ApplyAsync(CaptureJournalEntry entry)
    {
        await journal.SaveAsync(entry, CancellationToken.None);
        // Do not cancel or terminate an elevated write after dispatch: its verified result must be journaled.
        var request = new CaptureRequest(1, entry.ActionId, entry.Operation, entry.TargetExecutable, entry.TimestampUtc, entry.PreviousState, entry.RequestedState);
        CaptureResult result;
        CaptureReadResult? observed = null;
        try { result = await service.ExecuteAsync(request, CancellationToken.None); }
        catch (OperationCanceledException) { result = new(CaptureResultCode.CancelledByUser); }
        catch (UnauthorizedAccessException exception) { result = new(CaptureResultCode.AccessDenied, NativeError: exception.HResult); }
        catch (IOException exception) { result = new(CaptureResultCode.ApplyFailed, NativeError: exception.HResult); }
        catch (InvalidOperationException exception) { result = new(CaptureResultCode.ApplyFailed, NativeError: exception.HResult); }
        try { observed = await service.ReadAsync(entry.TargetExecutable, CancellationToken.None); }
        catch (OperationCanceledException) when (result.Code == CaptureResultCode.Success) { result = result with { Code = CaptureResultCode.VerificationFailed }; }
        catch (UnauthorizedAccessException exception) when (result.Code == CaptureResultCode.Success) { result = result with { Code = CaptureResultCode.VerificationFailed, NativeError = exception.HResult }; }
        catch (IOException exception) when (result.Code == CaptureResultCode.Success) { result = result with { Code = CaptureResultCode.VerificationFailed, NativeError = exception.HResult }; }
        catch (InvalidOperationException exception) when (result.Code == CaptureResultCode.Success) { result = result with { Code = CaptureResultCode.VerificationFailed, NativeError = exception.HResult }; }
        if (result.Code == CaptureResultCode.Success && (observed is not { Code: CaptureResultCode.Success, State: { } state } || state != entry.RequestedState))
            result = result with { Code = CaptureResultCode.VerificationFailed };
        result = result with { ObservedState = observed?.State ?? result.ObservedState, NativeError = result.NativeError ?? observed?.NativeError };
        // Keep the observation portable: unsupported external private paths are never journaled.
        var safeObserved = CrashCapturePolicy.IsSupportedState(result.ObservedState) ? result.ObservedState : null;
        var final = entry with { Result = result.Code, ObservedState = safeObserved, NativeError = result.NativeError,
            RollbackAvailable = entry.Operation == CaptureOperation.ConfigureApplicationCrashDump && result.Code == CaptureResultCode.Success,
            RollbackStatus = result.RollbackAttempted ? result.RollbackSucceeded ? "AutomaticComplete" : "Failed" : "NotRequested" };
        await journal.SaveAsync(final, CancellationToken.None);
        LastResult = result;
        targetExecutable = entry.TargetExecutable;
        previewTarget = entry.TargetExecutable;
        Preview = observed;
        Entries = await journal.LoadAsync(CancellationToken.None);
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        Changed?.Invoke();
        try { await action(); }
        catch (OperationCanceledException) { LastResult = new(CaptureResultCode.CancelledByUser); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Data.Common.DbException or InvalidOperationException)
        { LastResult = new(CaptureResultCode.ApplyFailed); }
        finally { Interlocked.Exchange(ref busy, 0); Changed?.Invoke(); }
    }
}
