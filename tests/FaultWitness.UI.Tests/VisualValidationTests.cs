using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using FaultWitness.App;
using FaultWitness.Storage;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "ManualVisualArtifacts")]
public sealed class VisualValidationTests
{
    [AvaloniaFact]
    public async Task RealApplicationScreens_RenderForManualReview()
    {
        var result = SyntheticResults.Create(100);
        var metadata = new ScanHistoryMetadata("recent", result.StartedUtc, result.FinishedUtc, 1430, 1, 1, 98, "SourceSystem=Complete; SourceWer=Partial");
        var stored = new StoredScan("visual", result.StartedUtc, result.FinishedUtc, "0.9.1", metadata,
            result.Incidents.Take(8).Select(item => new StoredIncident(item.Id.ToString("N"), item.StartTimeUtc, item.Category.ToString(), item.Severity.ToString(), item.Signature, "{\"Findings\":[]}")).ToArray());
        var services = new TestServices { Result = result, History = [stored, stored with { Id = "visual-older", FinishedUtc = stored.FinishedUtc.AddDays(-1) }, stored with { Id = "visual-around", Metadata = metadata with { AnalysisType = "around" } }] };
        using var viewModel = new MainViewModel(services);
        var window = new MainWindow(viewModel); window.Show(); window.ViewModel.SetResult(result); window.UpdateLayout();
        try
        {
            var destination = Environment.GetEnvironmentVariable("FAULTWITNESS_VISUAL_OUTPUT");
            if (string.IsNullOrWhiteSpace(destination)) destination = Path.Combine(FindRoot(), "artifacts", "ux-correction", "rendered");
            Directory.CreateDirectory(destination);
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Theme = theme });
                await Capture(window, destination, "home", theme, AppPage.Home, 1280, 800);
                window.ViewModel.OpenAnalyze(AnalysisMode.Recent); await CaptureCurrent(window, destination, "analyze", theme, 1000, 800);
                await Capture(window, destination, "incidents", theme, AppPage.Incidents, 1280, 800);
                window.ViewModel.Select(window.ViewModel.RecentSignificant[0]); await CaptureCurrent(window, destination, "incident-detail", theme, 1280, 800);
                await window.ViewModel.RefreshHistoryAsync(); await Capture(window, destination, "history", theme, AppPage.History, 1280, 800);
                await window.ViewModel.RefreshInventoryAsync(); await Capture(window, destination, "system", theme, AppPage.System, 1280, 800);
                await window.ViewModel.RefreshReadinessAsync(); await Capture(window, destination, "readiness", theme, AppPage.Readiness, 1000, 800);
                await Capture(window, destination, "settings", theme, AppPage.Settings, 1000, 800);
            }
            await Capture(window, destination, "home-small", AppTheme.Light, AppPage.Home, 640, 800);
            await Capture(window, destination, "home-medium", AppTheme.Dark, AppPage.Home, 900, 800);
            await Capture(window, destination, "home-large", AppTheme.Light, AppPage.Home, 1920, 1080);
            window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Language = "it" });
            await Capture(window, destination, "home-it", AppTheme.Light, AppPage.Home, 1280, 800);
            await Capture(window, destination, "settings-it", AppTheme.Light, AppPage.Settings, 1000, 800);
            window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Language = "de" });
            foreach (var width in new[] { 639, 640, 641 })
                await Capture(window, destination, "home-de-boundary", AppTheme.Light, AppPage.Home, width, 800);
        }
        finally { window.Close(); }
    }

    private static async Task Capture(MainWindow window, string path, string name, AppTheme theme, AppPage page, int width, int height)
    { window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Theme = theme }); window.ViewModel.Navigate(page); await CaptureCurrent(window, path, name, theme, width, height); }
    private static Task CaptureCurrent(MainWindow window, string path, string name, AppTheme theme, int width, int height)
    {
        window.Width = width; window.Height = height; window.UpdateLayout();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The real Avalonia view did not render.");
        frame.Save(Path.Combine(path, $"{name}-{theme.ToString().ToLowerInvariant()}-{width}x{height}.png"), new PngBitmapEncoderOptions()); return Task.CompletedTask;
    }
    private static string FindRoot() { var path = AppContext.BaseDirectory; while (!File.Exists(Path.Combine(path, "FaultWitness.slnx"))) path = Directory.GetParent(path)?.FullName ?? throw new DirectoryNotFoundException(); return path; }
}
