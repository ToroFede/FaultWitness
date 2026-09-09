using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using FaultWitness.App;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "CaptureVisualArtifacts")]
public sealed class CaptureVisualValidationTests
{
    [AvaloniaFact]
    public async Task CaptureStatesRenderInLightAndDarkThemes()
    {
        var service = new FakeCaptureService();
        var journal = new MemoryCaptureJournal();
        var capture = new CaptureWorkflow(service, journal) { TargetExecutable = "demo.exe" };
        using var viewModel = new MainViewModel(new TestServices { Capture = capture });
        var window = new MainWindow(viewModel);
        window.Show();
        window.Width = 1100; window.Height = 1400;
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_CAPTURE_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);

        await capture.PreviewAsync();
        viewModel.Navigate(AppPage.System); await RenderAndSave(window, output, "preview");
        await capture.ConfigureAsync(); await RenderAndSave(window, output, "configured-journal-restore");
        service.State = service.State with { DumpCount = 7 };
        await capture.RefreshAsync(); await RenderAndSave(window, output, "configuration-changed");
        await capture.RestoreAsync(capture.Entries.Single(entry => entry.RollbackAvailable).ActionId);
        await RenderAndSave(window, output, "restore-blocked");
        window.Close();
    }

    private static async Task RenderAndSave(MainWindow window, string? output, string state)
    {
        window.UpdateLayout();
        if (string.IsNullOrWhiteSpace(output)) return;
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Theme = theme });
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The real Avalonia view did not render.");
            frame.Save(Path.Combine(output, $"capture-{state}-{theme.ToString().ToLowerInvariant()}.png"), new PngBitmapEncoderOptions());
        }
        await Task.CompletedTask;
    }

    private sealed class MemoryCaptureJournal : ICaptureJournal
    {
        private readonly List<CaptureJournalEntry> entries = [];
        public Task SaveAsync(CaptureJournalEntry entry, CancellationToken token) { entries.RemoveAll(item => item.ActionId == entry.ActionId); entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<CaptureJournalEntry>> LoadAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CaptureJournalEntry>>(entries.ToArray());
    }

    private sealed class FakeCaptureService : ICrashCaptureService
    {
        public LocalDumpState State { get; set; } = new(false);
        public Task<CaptureReadResult> ReadAsync(string executable, CancellationToken token) => Task.FromResult(new CaptureReadResult(CaptureResultCode.Success, State));
        public Task<CaptureResult> ExecuteAsync(CaptureRequest request, CancellationToken token)
        {
            if (State != request.ExpectedState) return Task.FromResult(new CaptureResult(CaptureResultCode.UnexpectedCurrentState, State));
            State = request.DesiredState;
            return Task.FromResult(new CaptureResult(CaptureResultCode.Success, State));
        }
    }
}
