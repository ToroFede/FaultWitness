using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;
using FaultWitness.Storage;

#pragma warning disable CA2000 // MainWindow owns and disposes its view model.

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Pass2D2VisualValidation")]
public sealed class Pass2D2VisualValidationTests
{
    private static readonly VisualCase[] Cases = BuildCases();

    [AvaloniaFact]
    public async Task RenderBoundedFilterFormLocalizationAndWidthMatrix()
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2D2_VISUAL_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);

        foreach (var scenario in Cases)
            await Render(scenario, output);
    }

    private static async Task Render(VisualCase scenario, string output)
    {
        var services = new TestServices { Settings = new UserSettings(Language: scenario.Locale, Theme: scenario.Theme) };
        if (scenario.Page == "history") services.History = [StoredHistory(scenario.Locale)];
        var viewModel = new MainViewModel(services);
        var window = new MainWindow(viewModel);
        VisualRenderGeometry.ShowAtRequestedGeometry(window, scenario.Width, 900);
        try
        {
            switch (scenario.Page)
            {
                case "incidents":
                    viewModel.SetResult(SyntheticResults.Create(40));
                    viewModel.Navigate(AppPage.Incidents);
                    var incidents = Current<IncidentsView>(window);
                    if (scenario.State == "filter-empty")
                    {
                        incidents.FindControl<TextBox>("IncidentSearch")!.Text = "no matching synthetic incident";
                        Dispatcher.UIThread.RunJobs();
                    }
                    else if (scenario.State == "long-evidence")
                    {
                        var strength = incidents.FindControl<ComboBox>("StrengthFilter")!;
                        var presentation = Assert.IsType<FaultWitness.App.Presentation.IncidentListPresentation>(strength.DataContext);
                        strength.SelectedIndex = Enumerable.Range(1, presentation.Strengths.Count - 1)
                            .MaxBy(index => presentation.Strengths[index].Length);
                    }
                    break;
                case "settings":
                    viewModel.Navigate(AppPage.Settings);
                    break;
                case "analyze":
                    SetAnalyzeState(viewModel, window, scenario.State);
                    break;
                case "history":
                    await viewModel.RefreshHistoryAsync();
                    viewModel.Navigate(AppPage.History);
                    viewModel.SelectHistory(viewModel.History[0]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown visual page.");
            }

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AssertNoHorizontalOverflow(window, scenario.Name);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{scenario.Name} produced no frame.");
            VisualRenderGeometry.AssertFrameMatches(window, frame, scenario.Width, 900, scenario.Name);
            frame.Save(Path.Combine(output, scenario.Name + ".png"), new PngBitmapEncoderOptions());
        }
        finally { window.Close(); }
    }

    private static void SetAnalyzeState(MainViewModel viewModel, MainWindow window, string state)
    {
        var mode = state switch
        {
            "around" => AnalysisMode.Around,
            "import" => AnalysisMode.Files,
            _ => AnalysisMode.Recent
        };
        viewModel.OpenAnalyze(mode);
        var view = Current<AnalyzeView>(window);
        switch (state)
        {
            case "custom":
                view.FindControl<ComboBox>("PeriodSelector")!.SelectedIndex = (int)AnalysisPeriod.Custom;
                view.FindControl<DatePicker>("FromDate")!.SelectedDate = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
                view.FindControl<DatePicker>("ToDate")!.SelectedDate = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
                break;
            case "around":
                view.FindControl<DatePicker>("AroundDate")!.SelectedDate = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
                view.FindControl<TimePicker>("AroundTime")!.SelectedTime = new TimeSpan(10, 23, 0);
                view.FindControl<ComboBox>("AroundWindow")!.SelectedIndex = 2;
                break;
            case "import":
                viewModel.AddImports([Path.Combine(Path.GetTempPath(), "synthetic.evtx")]);
                break;
        }
    }

    private static StoredScan StoredHistory(string locale)
    {
        var started = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);
        var incident = new StoredIncident("visual-incident", started, "Graphics", "High", "synthetic-signature", "{}");
        var metadata = new ScanHistoryMetadata("recent", started.AddDays(-7), started, 15, 1, 0, 1, "SourceSystem=Complete");
        return new StoredScan("history-" + locale, started, started.AddMinutes(1), "0.9.1", metadata, [incident]);
    }

    private static T Current<T>(MainWindow window) where T : Control =>
        Assert.IsType<T>(window.FindControl<ContentControl>("PageHost")!.Content);

    private static void AssertNoHorizontalOverflow(Control root, string scenario)
    {
        foreach (var viewer in root.GetVisualDescendants().OfType<ScrollViewer>().Where(item => item.Name != "PART_ScrollViewer"))
            Assert.True(viewer.Viewport.Width == 0 || viewer.Extent.Width <= viewer.Viewport.Width + 2,
                $"{scenario}: {viewer.Name} extent={viewer.Extent.Width} viewport={viewer.Viewport.Width}.");
    }

    private static VisualCase[] BuildCases()
    {
        var cases = new List<VisualCase>
        {
            new("incidents-560-en-light-dense", "incidents", "en", AppTheme.Light, 560, "dense"),
            new("incidents-600-de-dark-dense", "incidents", "de", AppTheme.Dark, 600, "dense"),
            new("incidents-640-ru-dark-dense", "incidents", "ru", AppTheme.Dark, 640, "dense"),
            new("incidents-641-pl-light-dense", "incidents", "pl", AppTheme.Light, 641, "dense"),
            new("incidents-1008-en-light-dense", "incidents", "en", AppTheme.Light, 1008, "dense"),
            new("incidents-1280-en-light-dense", "incidents", "en", AppTheme.Light, 1280, "dense"),
            new("incidents-1920-it-dark-dense", "incidents", "it", AppTheme.Dark, 1920, "dense"),
            new("incidents-boundary-640-en-light", "incidents", "en", AppTheme.Light, 640, "dense"),
            new("incidents-boundary-641-en-light", "incidents", "en", AppTheme.Light, 641, "dense"),
            new("incidents-1280-de-dark-filter-empty", "incidents", "de", AppTheme.Dark, 1280, "filter-empty"),
            new("incidents-1280-pl-light-long-evidence", "incidents", "pl", AppTheme.Light, 1280, "long-evidence"),
            new("settings-600-de-dark", "settings", "de", AppTheme.Dark, 600, "default"),
            new("settings-1280-en-light", "settings", "en", AppTheme.Light, 1280, "default"),
            new("settings-1280-en-dark", "settings", "en", AppTheme.Dark, 1280, "default"),
            new("settings-1920-pl-light", "settings", "pl", AppTheme.Light, 1920, "default"),
            new("analyze-600-de-light-recent", "analyze", "de", AppTheme.Light, 600, "recent"),
            new("analyze-1280-en-light-recent", "analyze", "en", AppTheme.Light, 1280, "recent"),
            new("analyze-1280-en-light-custom", "analyze", "en", AppTheme.Light, 1280, "custom"),
            new("analyze-1920-it-light-custom", "analyze", "it", AppTheme.Light, 1920, "custom"),
            new("analyze-600-de-dark-custom", "analyze", "de", AppTheme.Dark, 600, "custom"),
            new("analyze-1280-en-light-around", "analyze", "en", AppTheme.Light, 1280, "around"),
            new("analyze-1920-pl-dark-around", "analyze", "pl", AppTheme.Dark, 1920, "around"),
            new("analyze-1280-en-light-import", "analyze", "en", AppTheme.Light, 1280, "import"),
            new("analyze-600-de-dark-import", "analyze", "de", AppTheme.Dark, 600, "import"),
            new("analyze-1280-ru-dark-around", "analyze", "ru", AppTheme.Dark, 1280, "around")
        };

        foreach (var locale in new[] { "en", "it", "es", "fr", "de", "pt", "ru", "pl" })
            cases.Add(new("history-" + locale + "-1280-light-severity", "history", locale, AppTheme.Light, 1280, "selected"));
        foreach (var locale in new[] { "en", "it", "de", "ru", "pl" })
            cases.Add(new("culture-analyze-custom-" + locale + "-1280-light", "analyze", locale, AppTheme.Light, 1280, "custom"));

        return cases.ToArray();
    }

    private sealed record VisualCase(string Name, string Page, string Locale, AppTheme Theme, int Width, string State);
}

#pragma warning restore CA2000
