using Avalonia.Controls;
using Avalonia.VisualTree;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Pages;

namespace FaultWitness.App;

public sealed partial class MainWindow
{
    private bool captureArchitectureConfirmed;
    private int captureJournalVisibleCount = 20;
    private CaptureView? captureView;
    private CapturePresentation? capturePresentation;

    private CaptureView CapturePage()
    {
        if (captureView is null)
        {
            capturePresentation = new();
            captureView = new();
            captureView.TargetRequested += target => { if (ViewModel.Capture is { } flow) flow.TargetExecutable = target; RefreshCapture(); };
            captureView.ArchitectureRequested += value => { captureArchitectureConfirmed = value; RefreshCapture(); };
            captureView.PreviewRequested += async () => { if (ViewModel.Capture is { } flow) await RunGuardedAsync(flow.PreviewAsync).ConfigureAwait(true); };
            captureView.RefreshRequested += async () => { if (ViewModel.Capture is { } flow) await RunGuardedAsync(flow.RefreshAsync).ConfigureAwait(true); };
            captureView.ConfigureRequested += async () => await RunGuardedAsync(ConfigureCaptureAsync).ConfigureAwait(true);
            captureView.RestoreRequested += async id => await RunGuardedAsync(() => ConfirmRestoreAsync(id)).ConfigureAwait(true);
            captureView.MoreRequested += () => { captureJournalVisibleCount += 20; RefreshCapture(); };
        }
        RefreshCapture();
        return captureView;
    }

    private void RefreshCapture() => captureView?.Refresh(capturePresentation!, ViewModel, captureArchitectureConfirmed, captureJournalVisibleCount);

    private async Task ConfigureCaptureAsync()
    {
        if (ViewModel.Capture is not { } capture || !captureArchitectureConfirmed || !capture.CanConfigure || ViewModel.IsBusy) return;
        var dialog = new CaptureConfirmationWindow();
        dialog.Prepare(ViewModel.Text, this, restore: false);
        if (await dialog.ShowDialog<bool>(this).ConfigureAwait(true)) await capture.ConfigureAsync().ConfigureAwait(true);
        RefreshCapture();
        if (ViewModel.Page == AppPage.System) captureView?.FindControl<Button>("CaptureConfigureButton")?.Focus();
    }

    private async Task ConfirmRestoreAsync(Guid actionId)
    {
        var dialog = new CaptureConfirmationWindow();
        dialog.Prepare(ViewModel.Text, this, restore: true);
        if (await dialog.ShowDialog<bool>(this).ConfigureAwait(true) && ViewModel.Capture is { } capture)
            await capture.RestoreAsync(actionId).ConfigureAwait(true);
        RefreshCapture();
        if (ViewModel.Page == AppPage.System && captureView is not null)
        {
            captureView.UpdateLayout();
            var restore = captureView.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "CaptureRestoreButton" + actionId.ToString("N"));
            (restore ?? captureView.FindControl<Button>("CaptureRefreshButton"))?.Focus();
        }
    }
}
