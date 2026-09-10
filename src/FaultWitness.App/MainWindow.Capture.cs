using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using FaultWitness.Core;

namespace FaultWitness.App;

public sealed partial class MainWindow
{
    private bool captureArchitectureConfirmed;
    private int captureJournalVisibleCount = 20;
    private Action? updateCaptureControls;

    private StackPanel BuildCaptureSection()
    {
        var capture = ViewModel.Capture;
        if (capture is null) return Section("CaptureTitle", Stack(Label(T("CaptureTitle"), TextRole.SectionTitle), Label(T("CaptureUnavailable"))));
        var executable = new TextBox { Name = "CaptureExecutable", Text = capture.TargetExecutable, MaxLength = 128, MinWidth = 260 };
        AutomationProperties.SetName(executable, T("CaptureExecutable"));
        var current = Label(CaptureCurrentText()); current.Name = "CaptureCurrentState"; AutomationProperties.SetName(current, current.Text);
        var details = Stack(Label(T("CapturePrivacy")), Label(T("CaptureRequiresAdministrator")), Label(T("CaptureLimitations")), Label(T("CaptureScope")), Label(T("CaptureFolder")));
        var preview = Label(CapturePreviewText()); preview.Name = "CapturePreviewState"; AutomationProperties.SetName(preview, preview.Text);
        var result = Label(CaptureResultText()); result.Name = "CaptureLastResult"; AutomationProperties.SetName(result, result.Text);
        var architecture = new CheckBox { Name = "CaptureArchitectureConfirmation", Content = T("CaptureArchitectureConfirmation"), IsChecked = captureArchitectureConfirmed };
        AutomationProperties.SetName(architecture, T("CaptureArchitectureConfirmation"));
        var read = PrimaryAsyncButton("CaptureRead", () => capture.PreviewAsync(), "CaptureReadButton");
        var configure = AsyncButton("CaptureConfigure", ConfigureCaptureAsync, "CaptureConfigureButton");
        // Capture has additional guards beyond the shared operation busy state.
        configure.Classes.Remove("operation");
        updateCaptureControls = () =>
        {
            configure.IsEnabled = captureArchitectureConfirmed && capture.CanConfigure && !ViewModel.IsBusy;
            current.Text = CaptureCurrentText(); preview.Text = CapturePreviewText(); result.Text = CaptureResultText();
            AutomationProperties.SetName(current, current.Text);
            AutomationProperties.SetName(preview, preview.Text);
            AutomationProperties.SetName(result, result.Text);
        };
        architecture.IsCheckedChanged += (_, _) => { captureArchitectureConfirmed = architecture.IsChecked == true; updateCaptureControls?.Invoke(); };
        executable.TextChanged += (_, _) => { capture.TargetExecutable = executable.Text ?? string.Empty; updateCaptureControls?.Invoke(); };
        updateCaptureControls();
        var refresh = AsyncButton("CaptureRefresh", async () => { await capture.RefreshAsync().ConfigureAwait(true); RenderPage(); }, "CaptureRefreshButton");
        var body = Stack(Label(T("CaptureTitle"), TextRole.SectionTitle), Label(T("CaptureHelp")), Field("CaptureExecutable", executable),
            Actions(read, configure, refresh), Surface(Stack(Label(T("CaptureCurrent"), TextRole.RowTitle), current, Label(T("CaptureProposed"), TextRole.RowTitle), preview, Label(T("CaptureLastResult"), TextRole.RowTitle), result, architecture, details), D("primitive.space.3")),
            Label(T("CaptureJournal"), TextRole.RowTitle));
        var journal = new StackPanel { Spacing = D("primitive.space.3") };
        var entries = capture.Entries.OrderByDescending(CaptureWorkflow.CanRestore).ToArray();
        void RenderJournal()
        {
            journal.Children.Clear();
            foreach (var entry in entries.Take(captureJournalVisibleCount)) journal.Children.Add(CaptureJournalRow(entry));
            if (entries.Length > captureJournalVisibleCount)
                journal.Children.Add(Button("CaptureShowMore", () => { captureJournalVisibleCount += 20; RenderJournal(); }, "CaptureShowMoreButton"));
        }
        RenderJournal();
        body.Children.Add(journal);
        if (capture.Entries.Count == 0) body.Children.Add(Muted(T("CaptureNoJournal")));
        return body;
    }

    private string CaptureCurrentText()
    {
        var state = ViewModel.Capture?.ActiveState ?? CaptureActiveState.CouldNotVerify;
        return ViewModel.Text.Format("CaptureActiveState", T("CaptureState" + state));
    }

    private string CapturePreviewText()
    {
        if (ViewModel.Capture?.Preview is not { } read) return T("CaptureNotRead");
        if (read.State is not { } state) return T("CaptureReadUnavailable") + " " + T("CaptureResult" + read.Code);
        return ViewModel.Text.Format("CapturePreviewValue", state.DumpType?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? T("NotAvailable"), state.DumpCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? T("NotAvailable"), state.DumpFolder ?? T("NotAvailable"));
    }

    private string CaptureResultText() => ViewModel.Capture?.LastResult is { } result ? ViewModel.Text.Format("CaptureLastResultValue", T("CaptureResult" + result.Code)) : T("CaptureNoResult");

    private Border CaptureJournalRow(CaptureJournalEntry entry)
    {
        var status = T("CaptureResult" + entry.Result);
        var restoration = CaptureWorkflow.CanRestore(entry) ? T("CaptureRestoreAvailable") : T("CaptureRestoreUnavailable");
        var action = entry.Operation == CaptureOperation.ConfigureApplicationCrashDump ? T("CaptureActionConfigure") : T("CaptureActionRestore");
        var summary = ViewModel.Text.Format("CaptureJournalRow", $"{action}: {entry.TargetExecutable}", entry.TimestampUtc.ToLocalTime().ToString("g", ViewModel.Text.Culture), status, restoration);
        var requested = ViewModel.Text.Format("CaptureRequestedValue", entry.RequestedState.DumpType ?? 1, entry.RequestedState.DumpCount ?? 3, entry.RequestedState.DumpFolder ?? T("CaptureDefaultFolder"));
        var observed = entry.ObservedState is { } state ? ViewModel.Text.Format("CaptureObservedValue", state.DumpType?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? T("NotAvailable"), state.DumpCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? T("NotAvailable"), state.DumpFolder ?? T("CaptureDefaultFolder")) : T("CaptureObservedUnavailable");
        var content = Stack(Label(summary, TextRole.RowTitle), Expand("CaptureJournalDetails", Stack(Label(T("CapturePrevious")), Muted(StateText(entry.PreviousState)), Label(T("CaptureRequested")), Muted(requested), Label(T("CaptureObserved")), Muted(observed)), false));
        if (CaptureWorkflow.CanRestore(entry)) content.Children.Add(AsyncButton("CaptureRestore", () => ConfirmRestoreAsync(entry), "CaptureRestoreButton" + entry.ActionId.ToString("N")));
        var border = Surface(content, D("primitive.space.3")); AutomationProperties.SetName(border, summary); return border;
    }

    private string StateText(LocalDumpState state) => ViewModel.Text.Format("CaptureStateValue", state.KeyExists ? T("CapturePresent") : T("CaptureAbsent"), state.DumpType?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? T("NotAvailable"), state.DumpCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? T("NotAvailable"));

    private async Task ConfigureCaptureAsync()
    {
        var dialog = new Window { Title = T("CaptureConfigure"), Width = 500, Height = 300, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var confirm = PrimaryButton("CaptureConfirmConfigure", () => dialog.Close(true));
        dialog.Content = new Border { Padding = new Thickness(D("primitive.space.6")), Child = Stack(Label(T("CaptureConfigureWarning")), Actions(confirm, Button("Cancel", () => dialog.Close(false)))) };
        if (await dialog.ShowDialog<bool>(this).ConfigureAwait(true) && ViewModel.Capture is { } capture) { await capture.ConfigureAsync().ConfigureAwait(true); RenderPage(); }
    }

    private async Task ConfirmRestoreAsync(CaptureJournalEntry entry)
    {
        var dialog = new Window { Title = T("CaptureRestore"), Width = 500, Height = 260, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var confirm = DangerButton("CaptureConfirmRestore", () => dialog.Close(true));
        dialog.Content = new Border { Padding = new Thickness(D("primitive.space.6")), Child = Stack(Label(T("CaptureRestoreWarning")), Actions(confirm, Button("Cancel", () => dialog.Close(false)))) };
        if (await dialog.ShowDialog<bool>(this).ConfigureAwait(true) && ViewModel.Capture is { } capture) { await capture.RestoreAsync(entry.ActionId).ConfigureAwait(true); RenderPage(); }
    }
}
