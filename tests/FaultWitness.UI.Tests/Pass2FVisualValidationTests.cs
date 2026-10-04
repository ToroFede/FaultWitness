using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

public sealed class Pass2FVisualValidationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private sealed record Scenario(string Name, AppPage Page, int Width = 1280, string Locale = "en",
        AppTheme Theme = AppTheme.Light, CoverageState Coverage = CoverageState.Complete,
        IncidentCategory? Detail = null, bool Unknown = false);

    [AvaloniaFact]
    public async Task BoundedExplainabilityMatrixUsesRealRulesAndActualQueryPeriod()
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2F_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);
        var records = new List<object>();
        var selected = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2F_SCENARIO");
        foreach (var scenario in Cases().Where(item => string.IsNullOrWhiteSpace(selected) || selected.Split(',').Contains(item.Name + ":" + item.Locale + ":" + item.Width, StringComparer.Ordinal)))
        {
            var result = Pass2FFixtures.Result(scenario.Coverage);
            if (scenario.Name.StartsWith("zero", StringComparison.Ordinal)) result = result with { Incidents = [] };
            if (scenario.Name == "background-only") result = result with { Incidents = [Pass2FFixtures.Background()] };
            var services = new TestServices { Result = result, Settings = new UserSettings(Language: scenario.Locale, Theme: scenario.Theme) };
            using var vm = new MainViewModel(services) { Period = AnalysisPeriod.Custom,
                CustomFrom = Pass2FFixtures.End.AddDays(-90), CustomTo = Pass2FFixtures.End };
            var window = new MainWindow(vm);
            VisualRenderGeometry.ShowAtRequestedGeometry(window, scenario.Width, 1100);
            try
            {
                await vm.AnalyzeAsync();
                vm.Navigate(scenario.Page);
                CaptureAxamlTests.Settle(window);
                if (scenario.Page == AppPage.Incidents) vm.ShowPriority(null);
                if (scenario.Detail is { } category)
                    vm.Select(vm.AllRows.Single(item => item.Incident.Category == category &&
                        (category != IncidentCategory.ApplicationCrash || (item.Incident.AnchorEvent.Process is null) == scenario.Unknown)));
                if (scenario.Name == "analyze-invalid")
                {
                    window.GetVisualDescendants().OfType<DatePicker>().Single(item => item.Name == "FromDate").SelectedDate = vm.CustomFrom.AddDays(-1);
                    await vm.AnalyzeAsync();
                    Assert.Equal("InvalidTimeRange", vm.StatusKey);
                    Assert.Equal(1, services.AnalysisCalls);
                }
                CaptureAxamlTests.Settle(window);
                if (scenario.Page == AppPage.Home)
                {
                    var quiet = window.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "QuietResult");
                    quiet.BringIntoView(); CaptureAxamlTests.Settle(window);
                }
                if (scenario.Page == AppPage.Detail && scenario.Width <= 641)
                {
                    var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(item => item.Name == "DetailScroll");
                    scroll.Offset = new Vector(0, 0); CaptureAxamlTests.Settle(window);
                }
                if (!string.IsNullOrWhiteSpace(output))
                    await File.WriteAllTextAsync(Path.Combine(output, $"geometry-{scenario.Name}-{scenario.Locale}-{scenario.Width}.json"),
                        JsonSerializer.Serialize(Geometry(window, scenario), JsonOptions));
                try { AssertPageGeometry(window, scenario.Name); }
                catch
                {
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                    using var failedFrame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Failed frame missing.");
                    if (!string.IsNullOrWhiteSpace(output)) failedFrame.Save(Path.Combine(output,
                        $"FAILED-{scenario.Name}-{scenario.Locale}-{scenario.Theme}-{scenario.Width}.png"), new PngBitmapEncoderOptions());
                    throw;
                }
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Explainability frame missing.");
                VisualRenderGeometry.AssertFrameMatches(window, frame, scenario.Width, 1100, scenario.Name);
                var filename = $"{scenario.Name}-{scenario.Locale}-{scenario.Theme}-{scenario.Width}.png";
                if (!string.IsNullOrWhiteSpace(output)) frame.Save(Path.Combine(output, filename), new PngBitmapEncoderOptions());
                records.Add(new { filename, scenario.Name, page = scenario.Page.ToString(), scenario.Locale,
                    theme = scenario.Theme.ToString(), actualWidth = frame.PixelSize.Width, actualHeight = frame.PixelSize.Height,
                    coverage = scenario.Coverage.ToString(), requestedFrom = services.From, requestedTo = services.To,
                    incidentCount = vm.AllRows.Count, data = "Illustrative synthetic records evaluated by unchanged production rules" });
            }
            finally { window.Close(); }
        }
        if (!string.IsNullOrWhiteSpace(output))
            await File.WriteAllTextAsync(Path.Combine(output, "explainability-matrix.json"), JsonSerializer.Serialize(records, JsonOptions));
    }

    private static void AssertPageGeometry(MainWindow window, string scenario)
    {
        // Single-line TextBox editing/placeholder content is intentionally internally scrollable.
        // Its outer control must fit, while every page and list ScrollViewer must have no horizontal overflow.
        var page = window.FindControl<ContentControl>("PageHost")!;
        foreach (var viewer in page.GetVisualDescendants().OfType<ScrollViewer>()
            .Where(item => !item.GetVisualAncestors().OfType<TextBox>().Any()))
            Assert.True(viewer.Viewport.Width == 0 || viewer.Extent.Width <= viewer.Viewport.Width + 1,
                $"{scenario}: {viewer.Name} page/list extent {viewer.Extent.Width} exceeds viewport {viewer.Viewport.Width}");
        foreach (var control in page.GetVisualDescendants().OfType<Control>()
            .Where(item => item.IsEffectivelyVisible && (item.Name is "IncidentPageContent" or "IncidentFilters" or
                "IncidentResultRegion" or "BackgroundProcessGuidance" or "AnalyzedPeriod" or "IncidentSubject" or
                "IncidentSource" or "MaximumAnalysisRange" or "QuietResult" || item is TextBox)))
        {
            var position = control.TranslatePoint(default, page)!.Value;
            Assert.True(position.X >= -1 && position.X + control.Bounds.Width <= page.Bounds.Width + 1,
                $"{scenario}: {control.Name} x={position.X}, width={control.Bounds.Width}, page width={page.Bounds.Width}");
        }
        var incidentScroll = page.GetVisualDescendants().OfType<ScrollViewer>().SingleOrDefault(item => item.Name == "IncidentScroll");
        if (incidentScroll is not null)
        {
            var content = incidentScroll.Content as Control;
            Assert.NotNull(content);
            Assert.True(content!.Bounds.Width <= incidentScroll.Viewport.Width + 1);
            var bar = incidentScroll.GetVisualDescendants().OfType<ScrollBar>().SingleOrDefault(item =>
                item.Name == "PART_VerticalScrollBar" && item.IsEffectivelyVisible &&
                item.GetVisualAncestors().OfType<ScrollViewer>().First() == incidentScroll);
            if (bar is not null)
                Assert.True(content.TranslatePoint(default, page)!.Value.X + content.Bounds.Width <=
                    bar.TranslatePoint(default, page)!.Value.X + 1, $"{scenario}: page content lies under its vertical scrollbar");
        }
    }

    private static object Geometry(MainWindow window, Scenario scenario) => new
    {
        scenario.Name, scenario.Locale, requestedWidth = scenario.Width, actualWidth = window.Bounds.Width,
        scrolls = window.GetVisualDescendants().OfType<ScrollViewer>().Select(item => new { item.Name,
            item.Bounds, item.Extent, item.Viewport, item.Offset, parent = item.Parent?.GetType().Name }).ToArray(),
        controls = window.GetVisualDescendants().OfType<Control>().Where(item => item.IsVisible)
            .Select(item => new { type = item.GetType().Name, item.Name, item.Bounds, item.DesiredSize,
                location = item.TranslatePoint(default, window), item.MinWidth, item.MaxWidth,
                parent = (item.Parent as Control)?.Name, text = (item as TextBlock)?.Text,
                wrapping = (item as TextBlock)?.TextWrapping.ToString() }).ToArray()
    };

    private static IEnumerable<Scenario> Cases()
    {
        yield return new("incidents-mixed", AppPage.Incidents);
        yield return new("incidents-mixed", AppPage.Incidents, Theme: AppTheme.Dark);
        yield return new("incidents-compact", AppPage.Incidents, 600, "de", AppTheme.Dark);
        yield return new("incidents-long", AppPage.Incidents, 641, "ru", AppTheme.Dark);
        yield return new("incidents-long", AppPage.Incidents, 1920, "pl");
        foreach (var width in new[] { 560, 600, 640, 641 })
        foreach (var locale in new[] { "en", "it", "de", "ru", "pl" })
            yield return new("compact-bounds", AppPage.Incidents, width, locale, AppTheme.Dark);
        yield return new("detail-application", AppPage.Detail, Detail: IncidentCategory.ApplicationCrash);
        yield return new("detail-unknown", AppPage.Detail, 600, "de", AppTheme.Dark, Detail: IncidentCategory.ApplicationCrash, Unknown: true);
        yield return new("detail-restart", AppPage.Detail, Theme: AppTheme.Dark, Detail: IncidentCategory.Power);
        yield return new("detail-graphics-limited", AppPage.Detail, Coverage: CoverageState.Partial, Detail: IncidentCategory.Graphics);
        yield return new("detail-service", AppPage.Detail, 641, "pl", AppTheme.Dark, Detail: IncidentCategory.Service);
        yield return new("zero-complete-90days", AppPage.Home);
        yield return new("zero-partial", AppPage.Home, Theme: AppTheme.Dark, Coverage: CoverageState.Partial);
        yield return new("zero-denied", AppPage.Incidents, 600, "de", AppTheme.Dark, Coverage: CoverageState.AccessDenied);
        yield return new("zero-unavailable", AppPage.Home, 641, "ru", AppTheme.Dark, Coverage: CoverageState.Unavailable);
        yield return new("zero-not-supported", AppPage.Home, 1280, "it", Coverage: CoverageState.NotSupported);
        yield return new("background-only", AppPage.Home, Theme: AppTheme.Dark);
        yield return new("analyze-90days", AppPage.Analyze);
        yield return new("analyze-90days", AppPage.Analyze, 600, "de", AppTheme.Dark);
        yield return new("analyze-90days", AppPage.Analyze, 641, "ru", AppTheme.Dark);
        yield return new("analyze-invalid", AppPage.Analyze, Theme: AppTheme.Dark);
    }
}
