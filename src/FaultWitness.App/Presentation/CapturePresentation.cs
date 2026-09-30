using System.Collections.ObjectModel;
using System.Globalization;
using FaultWitness.Core;
using FaultWitness.Localization;

namespace FaultWitness.App.Presentation;

/// <summary>Display-only projection. Workflow/policy remain the sole authority for mutation and recovery guards.</summary>
public sealed class CapturePresentation : PagePresentation
{
    public LocalizedLabels Text { get; private set; } = new(new LocalizationService());
    public bool HasService { get; private set; }
    public bool IsBusy { get; private set; }
    public bool CanRead { get; private set; }
    public string ConfigureGuard { get; private set; } = string.Empty;
    public bool CanConfigure { get; private set; }
    public string Target { get; private set; } = string.Empty;
    public bool ArchitectureConfirmed { get; private set; }
    public string CurrentState { get; private set; } = string.Empty;
    public string PreviewNotice { get; private set; } = string.Empty;
    public bool HasVerifiedResult { get; private set; }
    public string PreviewState { get; private set; } = string.Empty;
    public string ProposedState { get; private set; } = string.Empty;
    public string LastResult { get; private set; } = string.Empty;
    public string RestoreGuardResult { get; private set; } = string.Empty;
    public bool HasRestoreGuardResult => RestoreGuardResult.Length > 0;
    public bool HasMore { get; private set; }
    public IReadOnlyList<CapturePresentation> MoreChoices => HasMore ? [this] : [];
    public bool HasEntries => Entries.Count > 0;
    public ObservableCollection<CaptureJournalPresentation> Entries { get; } = [];

    public void Refresh(MainViewModel source, bool architectureConfirmed, int visibleCount)
    {
        var text = source.Text;
        var flow = source.Capture;
        Text = new(text);
        HasService = flow is not null;
        Target = flow?.TargetExecutable ?? string.Empty;
        ArchitectureConfirmed = architectureConfirmed;
        IsBusy = flow?.IsBusy == true;
        CanRead = flow is not null && !IsBusy && !source.IsBusy;
        CanConfigure = architectureConfirmed && flow?.CanConfigure == true && !source.IsBusy;
        ConfigureGuard = CanRead && flow is { CanConfigure: false, Preview: { Code: CaptureResultCode.Success, State: not null } } ? text.Get("CaptureResultUnsupportedConfiguration") : text.Get("CaptureConfigureGuard");
        PreviewNotice = flow?.CanConfigure == true ? text.Get("CapturePreviewReady") : flow?.Preview is { Code: not CaptureResultCode.Success } failed ? text.Get("CaptureResult" + failed.Code) : text.Get("CaptureNotRead");
        HasVerifiedResult = !IsBusy && flow?.LastResult is { Code: CaptureResultCode.Success, ObservedState: not null };
        CurrentState = text.Format("CaptureActiveState", text.Get("CaptureState" + (flow?.ActiveState ?? CaptureActiveState.CouldNotVerify)));
        PreviewState = flow?.Preview is not { } read ? text.Get("CaptureNotRead") : read.State is not { } state
            ? text.Get("CaptureReadUnavailable") + " " + text.Get("CaptureResult" + read.Code)
            : text.Format("CapturePreviewValue", Number(state.DumpType, text), Number(state.DumpCount, text), state.DumpFolder ?? text.Get("NotAvailable"));
        // Display the existing fixed policy, never construct a request or add dump-mode choices.
        ProposedState = text.Format("CaptureRequestedValue", CrashCapturePolicy.DumpType, CrashCapturePolicy.DumpCount, text.Get("CaptureDefaultFolder"));
        LastResult = flow?.LastResult is { } result ? text.Format("CaptureLastResultValue", text.Get("CaptureResult" + result.Code)) : text.Get("CaptureNoResult");
        // Do not independently infer drift/newer actions or pre-authorize Restore. Show only the workflow's refusal.
        RestoreGuardResult = flow?.LastResult is { Code: CaptureResultCode.UnexpectedCurrentState or CaptureResultCode.InvalidRequest or CaptureResultCode.AlreadyRestored or CaptureResultCode.VerificationFailed or CaptureResultCode.RegistryUnavailable or CaptureResultCode.UnsupportedConfiguration } guard
            ? text.Get("CaptureResult" + guard.Code) : string.Empty;
        var entries = flow?.Entries.OrderByDescending(CaptureWorkflow.CanRestore).ToArray() ?? [];
        HasMore = entries.Length > visibleCount;
        var visible = entries.Take(visibleCount).ToArray();
        for (var i = 0; i < visible.Length; i++)
        {
            var entry = visible[i];
            var row = Entries.FirstOrDefault(item => item.Source.ActionId == entry.ActionId);
            if (row is null) { row = new(); row.Refresh(entry, text, CanRead); Entries.Insert(i, row); }
            else
            {
                if (Entries.IndexOf(row) != i) Entries.Move(Entries.IndexOf(row), i);
                row.Refresh(entry, text, CanRead);
            }
        }
        while (Entries.Count > visible.Length) Entries.RemoveAt(Entries.Count - 1);
        Changed();
    }

    internal static string Number(int? value, LocalizationService text) => value?.ToString(CultureInfo.InvariantCulture) ?? text.Get("NotAvailable");
    internal static string StateText(LocalDumpState state, LocalizationService text) => text.Format("CaptureStateValue",
        text.Get(state.KeyExists ? "CapturePresent" : "CaptureAbsent"), Number(state.DumpType, text), Number(state.DumpCount, text));
}

public sealed class CaptureJournalPresentation : PagePresentation
{
    public CaptureJournalEntry Source { get; private set; } = null!;
    public LocalizedLabels Text { get; private set; } = new(new LocalizationService());
    public string Title { get; private set; } = string.Empty;
    public string Timestamp { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string Restoration { get; private set; } = string.Empty;
    public string AccessibleName { get; private set; } = string.Empty;
    public string Previous { get; private set; } = string.Empty;
    public string Requested { get; private set; } = string.Empty;
    public string Observed { get; private set; } = string.Empty;
    public string RestoreButtonName => "CaptureRestoreButton" + Source.ActionId.ToString("N");
    public bool CanRestore { get; private set; }
    public bool CanInvoke { get; private set; }
    public IReadOnlyList<CaptureJournalPresentation> RestoreChoices => CanRestore ? [this] : [];

    public void Refresh(CaptureJournalEntry entry, LocalizationService text, bool canInvoke)
    {
        Source = entry;
        Text = new(text);
        CanRestore = CaptureWorkflow.CanRestore(entry);
        CanInvoke = canInvoke;
        Title = text.Get(entry.Operation == CaptureOperation.ConfigureApplicationCrashDump ? "CaptureActionConfigure" : "CaptureActionRestore") + ": " + entry.TargetExecutable;
        Timestamp = entry.TimestampUtc.ToLocalTime().ToString("g", text.Culture);
        Status = text.Get("CaptureResult" + entry.Result);
        Restoration = text.Get(entry.RollbackStatus == "Complete" ? "CaptureResultAlreadyRestored" : CanRestore ? "CaptureRestoreAvailable" : "CaptureRestoreUnavailable");
        AccessibleName = text.Format("CaptureJournalRow", Title, Timestamp, Status, Restoration);
        Previous = CapturePresentation.StateText(entry.PreviousState, text);
        Requested = text.Format("CaptureRequestedValue", entry.RequestedState.DumpType ?? 1, entry.RequestedState.DumpCount ?? 3, entry.RequestedState.DumpFolder ?? text.Get("CaptureDefaultFolder"));
        Observed = entry.ObservedState is { } state ? text.Format("CaptureObservedValue", CapturePresentation.Number(state.DumpType, text),
            CapturePresentation.Number(state.DumpCount, text), state.DumpFolder ?? text.Get("CaptureDefaultFolder")) : text.Get("CaptureObservedUnavailable");
        Changed();
    }
}

public sealed record CaptureConfirmationPresentation(LocalizedLabels Text, string Warning, string ConfirmLabel);
