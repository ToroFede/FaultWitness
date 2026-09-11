using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Core;
using FaultWitness.Storage;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "BetaUxVisualArtifacts")]
public sealed class BetaUxVisualTests
{
    private static readonly (string Name, AppPage Page)[] Screens =
    [
        ("home", AppPage.Home), ("analyze", AppPage.Analyze), ("incidents", AppPage.Incidents),
        ("incident-detail", AppPage.Detail), ("history", AppPage.History), ("system", AppPage.System),
        ("readiness", AppPage.Readiness), ("capture", AppPage.System), ("settings", AppPage.Settings)
    ];

    [AvaloniaFact]
    public async Task BetaScreensRenderWithinBoundedVisualMatrix()
    {
        var result = SyntheticResults.Create(8);
        result = result with { Incidents = result.Incidents.Select(incident => incident with
        {
            ChangeContext = new ChangeHistoryContext(incident.StartTimeUtc, FirstObservationBasis.CurrentScan, false,
                [new SourceCoverage(SourceType.ChangeHistory, CoverageState.Partial, incident.StartTimeUtc.AddDays(-1), incident.StartTimeUtc, "ChangeCoveragePartial", "ChangeSourceSetupApi")])
        }).ToArray() };
        var metadata = new ScanHistoryMetadata("recent", result.StartedUtc, result.FinishedUtc, 1430, 1, 1, 98, "SourceSystem=Complete; SourceWer=Partial");
        var stored = new StoredScan("synthetic", result.StartedUtc, result.FinishedUtc, "0.9.1", metadata,
            result.Incidents.Take(4).Select(item => new StoredIncident(item.Id.ToString("N"), item.StartTimeUtc, item.Category.ToString(), item.Severity.ToString(), item.Signature, "{\"Findings\":[]}")).ToArray());
        var capture = new CaptureWorkflow(new FakeCaptureService(), new MemoryCaptureJournal()) { TargetExecutable = "synthetic.exe" };
        var services = new TestServices
        {
            Result = result, Capture = capture,
            History = [stored, stored with { Id = "synthetic-older", FinishedUtc = stored.FinishedUtc.AddDays(-1) }],
            Inventory = new Dictionary<string, string>
            {
                ["InventoryOperatingSystem"] = "Synthetic Windows 11", ["InventoryOsVersion"] = "Synthetic 1.0",
                ["InventoryProcessorLogical"] = "8", ["InventoryArchitecture"] = "x64",
                ["InventoryGraphicsName"] = "Synthetic GPU", ["InventoryBiosVendor"] = "Synthetic Firmware",
                ["InventoryStorageModel"] = "Synthetic NVMe", ["InventoryDriverName"] = "Synthetic Driver"
            },
            StructuredReadiness = [
                new("SourceSystem", CoverageState.Complete, "ReadinessSourceReady"),
                new("SourceWer", CoverageState.Partial, "ReadinessSourceLimited"),
                new("SourceArtifacts", CoverageState.Unavailable, "ReadinessSourceUnavailable")]
        };
        services.StructuredInventory = new SystemInventorySnapshot([
            new InventoryGroup("InventoryGroupOperatingSystem", [new("os", [new("InventoryOperatingSystem", "Synthetic Windows 11"), new("InventoryOsVersion", "Synthetic 1.0")])]),
            new InventoryGroup("InventoryGroupProcessorMemory", [new("cpu", [new("InventoryProcessorLogical", "8"), new("InventoryArchitecture", "x64")])]),
            new InventoryGroup("InventoryGroupGraphics", [new("gpu", [new("InventoryGraphicsName", "Synthetic GPU")])]),
            new InventoryGroup("InventoryGroupFirmware", [new("firmware", [new("InventoryBiosVendor", "Synthetic Firmware")])]),
            new InventoryGroup("InventoryGroupStorage", [new("disk", [new("InventoryStorageModel", "Synthetic NVMe")])]),
            new InventoryGroup("InventoryGroupDrivers", [new("driver", [new("InventoryDriverName", "Synthetic Driver")])])]);
        using var viewModel = new MainViewModel(services);
        var window = new MainWindow(viewModel);
        window.Show();
        viewModel.SetResult(result);

        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_BETA_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);

        try
        {
            // Seed a verified fake capture state and journal entry. This never touches the machine.
            await capture.PreviewAsync();
            await capture.ConfigureAsync();
            await RenderMatrix(window, viewModel, output, "normal", "en", 1280, 900, [AppTheme.Light, AppTheme.Dark]);

            var germanThemes = new[] { AppTheme.Light, AppTheme.Dark };
            await RenderMatrix(window, viewModel, output, "compact-de", "de", 600, 900, germanThemes, alternate: true);
            await RenderMatrix(window, viewModel, output, "large-de", "de", 1920, 1080, germanThemes, alternate: true);

            await Prepare(window, viewModel, AppPage.Analyze, AppTheme.Light, "en", 1280, 900);
            viewModel.OpenAnalyze(AnalysisMode.Around); window.UpdateLayout();
            AssertRendered(window, output, "analyze-around-en-light-1280x900");
            await Prepare(window, viewModel, AppPage.Analyze, AppTheme.Dark, "en", 1280, 900);
            viewModel.OpenAnalyze(AnalysisMode.Files); window.UpdateLayout();
            AssertRendered(window, output, "analyze-files-en-dark-1280x900");

            // The capture journal is a separate bounded view at normal size.
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                await Prepare(window, viewModel, AppPage.System, theme, "en", 1280, 900);
                var restore = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name?.StartsWith("CaptureRestoreButton", StringComparison.Ordinal) == true);
                restore.BringIntoView();
                window.UpdateLayout();
                AssertRendered(window, output, $"capture-journal-{theme.ToString().ToLowerInvariant()}-1280x900");
                Assert.Contains("synthetic.exe", VisibleText(window), StringComparison.OrdinalIgnoreCase);
                await Prepare(window, viewModel, AppPage.Detail, theme, "en", 1280, 900);
                var limits = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == viewModel.Text.Get("CannotConclude"));
                limits.BringIntoView(); window.UpdateLayout();
                AssertRendered(window, output, $"detail-limits-context-{theme.ToString().ToLowerInvariant()}-1280x900");
            }
        }
        finally { window.Close(); }
    }

    private static async Task RenderMatrix(MainWindow window, MainViewModel viewModel, string? output, string matrix, string language, int width, int height, AppTheme[] themes, bool alternate = false)
    {
        for (var index = 0; index < Screens.Length; index++)
        {
            var screen = Screens[index];
            foreach (var theme in alternate ? new[] { themes[index % themes.Length] } : themes)
            {
                await Prepare(window, viewModel, screen.Page, theme, language, width, height);
                if (screen.Name == "capture")
                {
                    var executable = window.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "CaptureExecutable");
                    executable.BringIntoView();
                    window.UpdateLayout();
                }
                AssertRendered(window, output, $"{matrix}-{screen.Name}-{language}-{theme.ToString().ToLowerInvariant()}-{width}x{height}");
            }
        }
    }

    private static async Task Prepare(MainWindow window, MainViewModel viewModel, AppPage page, AppTheme theme, string language, int width, int height)
    {
        viewModel.ChangeSettings(viewModel.Settings with { Language = language, Theme = theme });
        if (page == AppPage.Analyze) viewModel.OpenAnalyze(AnalysisMode.Recent);
        else viewModel.Navigate(page);
        if (page == AppPage.History) await viewModel.RefreshHistoryAsync();
        if (page == AppPage.System) await viewModel.RefreshInventoryAsync();
        if (page == AppPage.Readiness) await viewModel.RefreshReadinessAsync();
        if (page == AppPage.Detail) viewModel.Select(viewModel.RecentSignificant[0]);
        window.Width = width; window.Height = height; window.UpdateLayout();
        Assert.NotEmpty(VisibleText(window));
        Assert.All(window.GetVisualDescendants().OfType<ScrollViewer>(), viewer =>
            Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 2 || viewer.Viewport.Width == 0, $"{language}/{page}/{width} overflow"));
    }

    private static void AssertRendered(MainWindow window, string? output, string name)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The real Avalonia view did not render.");
        Assert.True(frame.PixelSize.Width > 0 && frame.PixelSize.Height > 0, $"{name} produced an empty frame.");
        if (!string.IsNullOrWhiteSpace(output)) frame.Save(Path.Combine(output, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static string VisibleText(Control root) => string.Join(" ", root.GetVisualDescendants().OfType<TextBlock>().Where(item => item.IsVisible).Select(item => item.Text));

    private sealed class MemoryCaptureJournal : ICaptureJournal
    {
        private readonly List<CaptureJournalEntry> entries = [];
        public Task SaveAsync(CaptureJournalEntry entry, CancellationToken token) { entries.RemoveAll(item => item.ActionId == entry.ActionId); entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<CaptureJournalEntry>> LoadAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CaptureJournalEntry>>(entries.ToArray());
    }

    private sealed class FakeCaptureService : ICrashCaptureService
    {
        public LocalDumpState State { get; private set; } = new(false);
        public Task<CaptureReadResult> ReadAsync(string executable, CancellationToken token) => Task.FromResult(new CaptureReadResult(CaptureResultCode.Success, State));
        public Task<CaptureResult> ExecuteAsync(CaptureRequest request, CancellationToken token)
        {
            if (State != request.ExpectedState) return Task.FromResult(new CaptureResult(CaptureResultCode.UnexpectedCurrentState, State));
            State = request.DesiredState;
            return Task.FromResult(new CaptureResult(CaptureResultCode.Success, State));
        }
    }
}
