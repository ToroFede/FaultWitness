using FaultWitness.App;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

public sealed class CaptureWorkflowTests
{
    [Fact]
    public async Task PreviewNeverWritesAndConfigureJournalsBeforeElevation()
    {
        var journal = new MemoryJournal(); var service = new FakeService();
        service.BeforeExecute = () => Assert.Equal(CaptureResultCode.Pending, Assert.Single(journal.Items).Result);
        var flow = new CaptureWorkflow(service, journal) { TargetExecutable = "demo.exe" };
        await flow.PreviewAsync(); Assert.Equal(0, service.Executions);
        await flow.ConfigureAsync();
        Assert.Equal(CaptureResultCode.Success, flow.LastResult!.Code);
        Assert.Equal(CaptureActiveState.Active, flow.ActiveState);
        Assert.True(Assert.Single(flow.Entries).RollbackAvailable);
        Assert.Equal(new LocalDumpState(false), flow.Entries[0].PreviousState);
    }
    [Fact]
    public async Task DurablePendingFailurePreventsAnyDispatch()
    {
        var service = new FakeService(); var flow = new CaptureWorkflow(service, new MemoryJournal { Fail = true }) { TargetExecutable = "demo.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync();
        Assert.Equal(0, service.Executions); Assert.Equal(CaptureResultCode.ApplyFailed, flow.LastResult!.Code);
    }
    [Theory]
    [InlineData(CaptureResultCode.CancelledByUser)]
    [InlineData(CaptureResultCode.AccessDenied)]
    [InlineData(CaptureResultCode.ApplyFailed)]
    [InlineData(CaptureResultCode.VerificationFailed)]
    public async Task FailuresNeverClaimSuccess(CaptureResultCode code)
    {
        var journal = new MemoryJournal(); var service = new FakeService { Result = code }; var flow = new CaptureWorkflow(service, journal) { TargetExecutable = "demo.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync();
        Assert.Equal(code, flow.LastResult!.Code); Assert.False(Assert.Single(flow.Entries).RollbackAvailable);
        Assert.Equal(code, flow.Entries[0].Result);
    }
    [Fact]
    public async Task HelperSuccessRequiresIndependentReadback()
    {
        var service = new FakeService { SkipWrite = true }; var flow = new CaptureWorkflow(service, new MemoryJournal()) { TargetExecutable = "demo.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync();
        Assert.Equal(CaptureResultCode.VerificationFailed, flow.LastResult!.Code);
    }
    [Fact]
    public async Task ExecutionExceptionTransitionsPendingToFailure()
    {
        var journal = new MemoryJournal(); var service = new FakeService { ExecuteFailure = new UnauthorizedAccessException() }; var flow = new CaptureWorkflow(service, journal) { TargetExecutable = "demo.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync();
        Assert.Equal(CaptureResultCode.AccessDenied, flow.LastResult!.Code); Assert.Equal(CaptureResultCode.AccessDenied, Assert.Single(flow.Entries).Result);
    }
    [Fact]
    public async Task ReadbackExceptionTransitionsPendingToVerificationFailure()
    {
        var journal = new MemoryJournal(); var service = new FakeService { FailReadback = true }; var flow = new CaptureWorkflow(service, journal) { TargetExecutable = "demo.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync();
        Assert.Equal(CaptureResultCode.VerificationFailed, flow.LastResult!.Code); Assert.Equal(CaptureResultCode.VerificationFailed, Assert.Single(flow.Entries).Result);
    }
    [Fact]
    public async Task TargetEditInvalidatesPreview()
    {
        var service = new FakeService(); var flow = new CaptureWorkflow(service, new MemoryJournal()) { TargetExecutable = "first.exe" };
        await flow.PreviewAsync(); flow.TargetExecutable = "second.exe"; await flow.ConfigureAsync();
        Assert.Equal(0, service.Executions); Assert.Equal(CaptureResultCode.InvalidRequest, flow.LastResult!.Code);
    }
    [Fact]
    public async Task ExternalChangeBetweenPreviewAndApplyIsRefused()
    {
        var service = new FakeService(); var flow = new CaptureWorkflow(service, new MemoryJournal()) { TargetExecutable = "demo.exe" };
        await flow.PreviewAsync(); service.State = new(true, 2, 5); await flow.ConfigureAsync();
        Assert.Equal(CaptureResultCode.UnexpectedCurrentState, flow.LastResult!.Code);
        Assert.Equal(new LocalDumpState(true,2,5), service.State);
    }
    [Fact]
    public async Task RestoreIsExactAndSecondRestoreDoesNothing()
    {
        var before = new LocalDumpState(true, 2, null, @"%LOCALAPPDATA%\CrashDumps");
        var service = new FakeService { State = before }; var flow = new CaptureWorkflow(service, new MemoryJournal()) { TargetExecutable = "demo.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync(); var id = flow.Entries[0].ActionId;
        await flow.RestoreAsync(id); Assert.Equal(before, service.State); Assert.Equal(2, service.Executions);
        await flow.RestoreAsync(id); Assert.Equal(CaptureResultCode.AlreadyRestored, flow.LastResult!.Code); Assert.Equal(2, service.Executions);
    }
    [Fact]
    public async Task RestoreDriftNeverDispatches()
    {
        var service = new FakeService(); var flow = new CaptureWorkflow(service, new MemoryJournal()) { TargetExecutable = "demo.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync(); var id = flow.Entries[0].ActionId;
        service.State = service.State with { DumpCount = 7 }; await flow.RestoreAsync(id);
        Assert.Equal(CaptureResultCode.UnexpectedCurrentState, flow.LastResult!.Code); Assert.Equal(1, service.Executions);
        await flow.RefreshAsync(); Assert.Equal(CaptureActiveState.ConfigurationChanged, flow.ActiveState);
    }
    [Fact]
    public async Task PendingAfterAppRestartCanBeReviewedAndRestored()
    {
        var before = new LocalDumpState(false); var desired = CrashCapturePolicy.Desired(before);
        var pending = new CaptureJournalEntry(Guid.NewGuid(), CaptureOperation.ConfigureApplicationCrashDump, "demo.exe", DateTimeOffset.UtcNow, true, before, desired);
        var journal = new MemoryJournal(); journal.Items.Add(pending); var service = new FakeService { State = desired };
        var restarted = new CaptureWorkflow(service, journal); await restarted.RefreshAsync();
        Assert.Equal(CaptureResultCode.Pending, Assert.Single(restarted.Entries).Result);
        await restarted.RestoreAsync(pending.ActionId); Assert.Equal(before, service.State);
        Assert.Equal("Complete", restarted.Entries.Single(e => e.ActionId == pending.ActionId).RollbackStatus);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReopenedPendingWithDriftOrNewerActionNeverDispatches(bool newerAction)
    {
        var before = new LocalDumpState(false); var desired = CrashCapturePolicy.Desired(before);
        var pending = new CaptureJournalEntry(Guid.NewGuid(), CaptureOperation.ConfigureApplicationCrashDump, "demo.exe", DateTimeOffset.UtcNow.AddMinutes(-2), true, before, desired);
        var journal = new MemoryJournal(); journal.Items.Add(pending);
        if (newerAction) journal.Items.Add(pending with { ActionId = Guid.NewGuid(), TimestampUtc = DateTimeOffset.UtcNow });
        var service = new FakeService { State = newerAction ? desired : desired with { DumpCount = 7 } };
        var reopened = new CaptureWorkflow(service, journal); await reopened.RefreshAsync();
        await reopened.RestoreAsync(pending.ActionId);
        Assert.Equal(CaptureResultCode.UnexpectedCurrentState, reopened.LastResult!.Code);
        Assert.Equal(0, service.Executions);
        Assert.Equal(CaptureResultCode.Pending, reopened.Entries.Single(x => x.ActionId == pending.ActionId).Result);
    }

    [Fact]
    public async Task LaterUnrevertedActionBlocksOldJournalRestore()
    {
        var before = new LocalDumpState(false); var desired = CrashCapturePolicy.Desired(before);
        var old = new CaptureJournalEntry(Guid.NewGuid(), CaptureOperation.ConfigureApplicationCrashDump, "demo.exe", DateTimeOffset.UtcNow.AddMinutes(-2), true, before, desired, CaptureResultCode.Success, desired, true);
        var journal = new MemoryJournal(); journal.Items.Add(old); journal.Items.Add(old with { ActionId = Guid.NewGuid(), TimestampUtc = DateTimeOffset.UtcNow });
        var service = new FakeService { State = desired }; var flow = new CaptureWorkflow(service, journal);
        await flow.RestoreAsync(old.ActionId); Assert.Equal(0, service.Executions); Assert.Equal(CaptureResultCode.UnexpectedCurrentState, flow.LastResult!.Code);
    }
    [Fact]
    public async Task DuplicateClickCannotDispatchConcurrentAction()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeService { Gate = gate.Task }; var flow = new CaptureWorkflow(service, new MemoryJournal()) { TargetExecutable = "demo.exe" };
        await flow.PreviewAsync(); var first = flow.ConfigureAsync(); await flow.ConfigureAsync();
        Assert.Equal(1, service.Executions); gate.SetResult(); await first;
    }
    private sealed class MemoryJournal : ICaptureJournal
    {
        public List<CaptureJournalEntry> Items { get; } = [];
        public bool Fail { get; init; }
        public Task SaveAsync(CaptureJournalEntry entry, CancellationToken token)
        { if (Fail) throw new IOException(); Items.RemoveAll(e => e.ActionId == entry.ActionId); Items.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<CaptureJournalEntry>> LoadAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CaptureJournalEntry>>(Items.ToArray());
    }
    private sealed class FakeService : ICrashCaptureService
    {
        public LocalDumpState State { get; set; } = new(false);
        public int Executions { get; private set; }
        public CaptureResultCode Result { get; init; } = CaptureResultCode.Success;
        public bool SkipWrite { get; init; }
        public Exception? ExecuteFailure { get; init; }
        public bool FailReadback { get; init; }
        private int reads;
        public Action? BeforeExecute { get; set; }
        public Task? Gate { get; init; }
        public Task<CaptureReadResult> ReadAsync(string executable, CancellationToken token)
        { if (FailReadback && Interlocked.Increment(ref reads) > 1) throw new IOException(); return Task.FromResult(new CaptureReadResult(CaptureResultCode.Success, State)); }
        public async Task<CaptureResult> ExecuteAsync(CaptureRequest request, CancellationToken token)
        {
            BeforeExecute?.Invoke(); Executions++; if (ExecuteFailure is not null) throw ExecuteFailure; if (Gate is not null) await Gate;
            if (State != request.ExpectedState) return new(CaptureResultCode.UnexpectedCurrentState, State);
            if (Result == CaptureResultCode.Success && !SkipWrite) State = request.DesiredState;
            return new(Result, State);
        }
    }
}
