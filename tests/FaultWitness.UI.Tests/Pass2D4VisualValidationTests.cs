using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Pass2D4VisualValidation")]
public sealed class Pass2D4VisualValidationTests
{
    [AvaloniaFact]
    public async Task RenderSharedSurfaceSpacingAndCompactHomeMatrix()
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2D4_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);

        var scenarios = new List<VisualCase>();
        foreach (var (state, locale, theme) in new[]
        {
            ("relevant", "en", AppTheme.Light), ("background", "de", AppTheme.Dark),
            ("quiet", "en", AppTheme.Light), ("limited", "ru", AppTheme.Dark)
        })
        foreach (var width in new[] { 600, 640, 641 })
            scenarios.Add(new($"home-{state}-{locale}-{theme}-{width}", "home", state, locale, theme, width));
        scenarios.Add(new("home-relevant-en-light-1008", "home", "relevant", "en", AppTheme.Light, 1008));
        scenarios.Add(new("home-quiet-it-dark-1920", "home", "quiet", "it", AppTheme.Dark, 1920, 1080));

        scenarios.AddRange([
            new("history-empty-de-dark-560", "history", "empty", "de", AppTheme.Dark, 560),
            new("history-selected-de-dark-600", "history", "selected", "de", AppTheme.Dark, 600),
            new("history-dense-pl-light-641", "history", "dense", "pl", AppTheme.Light, 641),
            new("history-selected-en-light-1280", "history", "selected", "en", AppTheme.Light, 1280),
            new("history-dense-it-dark-1920", "history", "dense", "it", AppTheme.Dark, 1920, 1080),
            new("fallback-pl-light-600", "fallback", "missing", "pl", AppTheme.Light, 600),
            new("fallback-pl-dark-600", "fallback", "missing", "pl", AppTheme.Dark, 600),
            new("fallback-en-light-1280", "fallback", "missing", "en", AppTheme.Light, 1280),
            new("fallback-en-dark-1280", "fallback", "missing", "en", AppTheme.Dark, 1280)
        ]);

        scenarios.AddRange([
            new("surfaces-home-light-1008", "home", "relevant", "en", AppTheme.Light, 1008),
            new("surfaces-home-dark-1280", "home", "relevant", "en", AppTheme.Dark, 1280),
            new("surfaces-incidents-light-1280", "incidents", "selected", "en", AppTheme.Light, 1280),
            new("surfaces-incidents-dark-1920", "incidents", "selected", "de", AppTheme.Dark, 1920, 1080),
            new("surfaces-history-light-1280", "history", "selected", "en", AppTheme.Light, 1280),
            new("surfaces-history-dark-1920", "history", "selected", "ru", AppTheme.Dark, 1920, 1080),
            new("surfaces-detail-light-1280", "detail", "expanded", "en", AppTheme.Light, 1280),
            new("surfaces-detail-dark-1280", "detail", "expanded", "de", AppTheme.Dark, 1280),
            new("surfaces-readiness-light-1280", "readiness", "expanded", "en", AppTheme.Light, 1280),
            new("surfaces-readiness-dark-1920", "readiness", "expanded", "ru", AppTheme.Dark, 1920, 1080),
            new("surfaces-capture-light-1280", "capture", "expanded", "en", AppTheme.Light, 1280, 1400),
            new("surfaces-capture-dark-1280", "capture", "expanded", "de", AppTheme.Dark, 1280, 1400)
        ]);

        Assert.InRange(scenarios.Count, 35, 50);
        var scenarioFilter = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2D4_VISUAL_SCENARIO");
        var selectedScenarios = string.IsNullOrWhiteSpace(scenarioFilter)
            ? scenarios
            : scenarios.Where(scenario => string.Equals(scenario.Name, scenarioFilter, StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(selectedScenarios);
        foreach (var scenario in selectedScenarios)
        {
            try { await Render(scenario, output); }
            catch (Exception exception) { throw new InvalidOperationException($"Visual scenario {scenario.Name} failed.", exception); }
        }
    }

    private static async Task Render(VisualCase scenario, string? output)
    {
        var services = new TestServices { Settings = new UserSettings(Language: scenario.Locale, Theme: scenario.Theme) };
        CaptureWorkflow? captureFlow = null;
        if (scenario.Page == "capture")
        {
            captureFlow = new CaptureWorkflow(new SyntheticCaptureService(), new MemoryCaptureJournal()) { TargetExecutable = "synthetic.exe" };
            services.Capture = captureFlow;
        }
#pragma warning disable CA2000 // MainWindow owns its view model and disposes it on Closed.
        var viewModel = new MainViewModel(services);
        var window = new MainWindow(viewModel);
#pragma warning restore CA2000
        VisualRenderGeometry.ShowAtRequestedGeometry(window, scenario.Width, scenario.Height);
        try
        {
            switch (scenario.Page)
            {
                case "home":
                    viewModel.SetResult(HomeResult(scenario.State));
                    viewModel.Navigate(AppPage.Home);
                    break;
                case "incidents":
                    viewModel.SetResult(SyntheticResults.Create(40));
                    viewModel.Navigate(AppPage.Incidents);
                    break;
                case "history":
                    services.History = scenario.State == "empty" ? [] : [Stored("recent", 12), Stored("older", 25)];
                    await viewModel.RefreshHistoryAsync();
                    viewModel.Navigate(AppPage.History);
                    if (scenario.State != "empty") viewModel.SelectHistory(viewModel.History[0]);
                    break;
                case "fallback":
                    viewModel.Navigate(AppPage.Detail);
                    break;
                case "detail":
                    var detail = IncidentDetailAxamlTests.Scenario("groups");
                    viewModel.SetResult(detail);
                    viewModel.Select(viewModel.AllRows[0]);
                    viewModel.Navigate(AppPage.Detail);
                    break;
                case "readiness":
                    services.StructuredReadiness = [new("readiness-id", "SourceSystem", DiagnosticCapabilityStatus.Limited, "ReadinessHelp",
                        [new("SourceSystem", "synthetic retained interval")], true, "ReadinessHelp")];
                    viewModel.Navigate(AppPage.Readiness);
                    await viewModel.RefreshReadinessAsync();
                    break;
                case "capture":
                    await captureFlow!.PreviewAsync();
                    viewModel.Navigate(AppPage.Capture);
                    window.UpdateLayout();
                    var preview = window.GetVisualDescendants().OfType<Expander>().Single(item => item.Name == "CapturePreviewDetails");
                    preview.IsExpanded = true;
                    var systemScroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(item => item.Name == "AnalyzeCaptureScroll");
                    systemScroll.ScrollToEnd();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown visual page.");
            }

            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var page = window.FindControl<ContentControl>("PageHost")!.Content as Control
                ?? throw new InvalidOperationException($"{scenario.Name} has no page content.");
            AssertNoHorizontalOverflow(page, scenario.Name);
            if (scenario.Page is "home" or "incidents" or "history" or "detail" or "readiness" or "capture")
            {
                var semanticDisclosures = page.GetVisualDescendants().OfType<Expander>()
                    .Where(item => item.Classes.Contains("semantic-disclosure") && item.IsVisible).ToArray();
                foreach (var expander in semanticDisclosures.Take(3)) expander.IsExpanded = true;
            }

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{scenario.Name} produced no frame.");
            VisualRenderGeometry.AssertFrameMatches(window, frame, scenario.Width, scenario.Height, scenario.Name);
            if (!string.IsNullOrWhiteSpace(output)) frame.Save(Path.Combine(output, scenario.Name + ".png"), new PngBitmapEncoderOptions());
        }
        finally
        {
            window.Close();
        }
    }

    private static ScanResult HomeResult(string state) => state switch
    {
        "relevant" => SyntheticResults.Create(12),
        "background" => SyntheticResults.Create(0) with { Incidents = [BackgroundIncident()] },
        "quiet" => SyntheticResults.Create(0) with { Coverage = [Coverage(CoverageState.Complete)] },
        _ => SyntheticResults.Create(0) with
        {
            Coverage = [Coverage(CoverageState.Partial), Coverage(CoverageState.Unavailable), Coverage(CoverageState.AccessDenied), Coverage(CoverageState.NotSupported)]
        }
    };

    private static Incident BackgroundIncident()
    {
        var incident = SyntheticResults.Incident(0, DateTimeOffset.UtcNow);
        return incident with { Findings = incident.Findings.Select(item => item with { Disposition = FindingDisposition.Context }).ToArray() };
    }

    private static SourceCoverage Coverage(CoverageState state) =>
        new(SourceType.EventLog, state, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, "synthetic", "System");

    private static FaultWitness.Storage.StoredScan Stored(string id, int incidentCount)
    {
        var time = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var incidents = Enumerable.Range(0, incidentCount).Select(index => new FaultWitness.Storage.StoredIncident(
            $"{id}-{index}", time.AddMinutes(index), "Graphics", "High", $"synthetic-{id}-{index}", "{}")).ToArray();
        return new FaultWitness.Storage.StoredScan(id, time, time.AddMinutes(1), "0.9.1",
            new FaultWitness.Storage.ScanHistoryMetadata("recent", time.AddDays(-7), time, 15, 1, 0, incidentCount, "SourceSystem=Complete"), incidents);
    }

    private static void AssertNoHorizontalOverflow(Control root, string scenario)
    {
        foreach (var viewer in root.GetVisualDescendants().OfType<ScrollViewer>().Where(item => item.Name != "PART_ScrollViewer"))
            Assert.True(viewer.Viewport.Width == 0 || viewer.Extent.Width <= viewer.Viewport.Width + 2,
                $"{scenario}: {viewer.Name} overflow {viewer.Extent.Width:0.##}/{viewer.Viewport.Width:0.##}.");
    }

    private sealed record VisualCase(string Name, string Page, string State, string Locale, AppTheme Theme, int Width, int Height = 900);

    private sealed class MemoryCaptureJournal : ICaptureJournal
    {
        private readonly List<CaptureJournalEntry> entries = [];
        public Task SaveAsync(CaptureJournalEntry entry, CancellationToken token) { entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<CaptureJournalEntry>> LoadAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CaptureJournalEntry>>(entries.ToArray());
    }

    private sealed class SyntheticCaptureService : ICrashCaptureService
    {
        private LocalDumpState state = new(false);
        public Task<CaptureReadResult> ReadAsync(string executable, CancellationToken token) => Task.FromResult(new CaptureReadResult(CaptureResultCode.Success, state));
        public Task<CaptureResult> ExecuteAsync(CaptureRequest request, CancellationToken token)
        {
            if (state != request.ExpectedState) return Task.FromResult(new CaptureResult(CaptureResultCode.UnexpectedCurrentState, state));
            state = request.DesiredState;
            return Task.FromResult(new CaptureResult(CaptureResultCode.Success, state));
        }
    }
}
