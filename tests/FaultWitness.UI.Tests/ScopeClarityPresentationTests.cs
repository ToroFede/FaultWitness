using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;
using FaultWitness.Localization;

namespace FaultWitness.UI.Tests;

public sealed class ScopeClarityPresentationTests
{
    private static readonly string[] Locales = ["en", "it", "es", "fr", "de", "pt", "ru", "pl"];
    private static readonly string[] ChangedKeys =
    [
        "HomePurpose", "AnalyzeLastDay", "AnalyzeLastWeek", "AnalyzeLastMonth", "AnalyzeSelectedPeriod",
        "AnalyzeRecent", "AnalyzeCrashFreeze", "AnalysisHelp", "RecentAnalysisGuide", "ProductScope", "ImportScope",
        "NoSupportedIncidents", "NoSupportedIncidentsImported", "NoPriorityIncidents", "NoPriorityIncidentsImported",
        "QuietResultCaution", "BackgroundEntriesCount", "CoverageLimitSummary"
    ];

    [AvaloniaFact]
    public async Task ZeroIncidentResult_StatesSupportedScopeAndExactRequestedPeriod()
    {
        var result = SyntheticResults.Create(0) with
        {
            Coverage = [new(SourceType.EventLog, CoverageState.Complete, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, "synthetic", "System")]
        };
        var services = new TestServices { Result = result, Settings = new UserSettings(Language: "en", Period: AnalysisPeriod.Day) };
        using var viewModel = new MainViewModel(services);
        var window = Open(viewModel);
        try
        {
            await viewModel.AnalyzeAsync();
            var text = VisibleText(window);
            Assert.Contains("No supported stability incidents were found", text, StringComparison.Ordinal);
            Assert.Contains(services.From.ToLocalTime().ToString("g", viewModel.Text.Culture), text, StringComparison.Ordinal);
            Assert.Contains(services.To.ToLocalTime().ToString("g", viewModel.Text.Culture), text, StringComparison.Ordinal);
            Assert.Contains(viewModel.Text.Get("ProductScope"), text, StringComparison.Ordinal);
            Assert.Contains(viewModel.Text.Get("QuietResultCaution"), text, StringComparison.Ordinal);
            Assert.DoesNotContain("No problems found", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PC is healthy", text, StringComparison.OrdinalIgnoreCase);
            Assert.False(FindButton(window, "ResetFilters")?.IsVisible ?? false);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void BackgroundOnlyResult_UsesContextVocabularyAndOffersAllIncidents()
    {
        var incident = BackgroundIncident();
        var result = SyntheticResults.Create(0) with { Incidents = [incident] };
        using var viewModel = new MainViewModel(new TestServices { Result = result, Settings = new UserSettings(Language: "en") });
        var window = Open(viewModel);
        try
        {
            viewModel.SetResult(result);
            var text = VisibleText(window);
            Assert.Contains("No incidents needing attention or worth noting were found", text, StringComparison.Ordinal);
            Assert.Contains("Background context: 1", text, StringComparison.Ordinal);
            Assert.NotNull(FindButton(window, "ViewAllIncidents"));
            Assert.DoesNotContain("Background fault", text, StringComparison.OrdinalIgnoreCase);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(CoverageState.Complete)]
    [InlineData(CoverageState.Partial)]
    [InlineData(CoverageState.Unavailable)]
    [InlineData(CoverageState.AccessDenied)]
    [InlineData(CoverageState.NotSupported)]
    public void QuietResult_PreservesEachCoverageState(CoverageState state)
    {
        var result = SyntheticResults.Create(0) with
        {
            Coverage = [new(SourceType.Wer, state, null, null, "synthetic")]
        };
        using var viewModel = new MainViewModel(new TestServices { Result = result, Settings = new UserSettings(Language: "en") });
        var window = Open(viewModel);
        try
        {
            viewModel.SetResult(result);
            var text = VisibleText(window);
            var summaryKey = viewModel.Text.Get("CoverageLimitSummary");
            if (state == CoverageState.Complete)
            {
                Assert.DoesNotContain(summaryKey, text, StringComparison.Ordinal);
                Assert.DoesNotContain(viewModel.Text.Get("CoverageComplete"), text, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains(summaryKey, text, StringComparison.Ordinal);
                Assert.Contains(viewModel.Text.Get("SourceWer") + ": " + viewModel.Text.Get("Coverage" + state), text, StringComparison.Ordinal);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ResetFilters_AppearsOnlyWhenFiltersHideExistingIncidents()
    {
        var empty = SyntheticResults.Create(0);
        using var viewModel = new MainViewModel(new TestServices { Result = empty, Settings = new UserSettings(Language: "en") });
        var window = Open(viewModel);
        try
        {
            viewModel.SetResult(empty);
            viewModel.Navigate(AppPage.Incidents);
            var incidentsPage = Assert.IsType<IncidentsView>(window.FindControl<ContentControl>("PageHost")!.Content);
            var filterEmpty = Assert.IsType<Border>(incidentsPage.FindControl<Border>("FilterEmpty"));
            Assert.True(filterEmpty.IsVisible);
            Assert.False(FindButton(window, "ResetFilters")?.IsVisible ?? false);

            var result = SyntheticResults.Create(1);
            viewModel.SetResult(result);
            viewModel.SetFilter(new(Search: "no matching synthetic incident"));
            window.UpdateLayout();
            Assert.True(filterEmpty.IsVisible);
            Assert.Contains(viewModel.Text.Get("NoFilterMatches"), VisibleText(window), StringComparison.Ordinal);
            var reset = FindButton(window, "ResetFilters");
            Assert.NotNull(reset);
            Assert.True(reset.IsVisible);

            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert.False(FindButton(window, "ResetFilters")?.IsVisible ?? false);
            Assert.NotEmpty(viewModel.FilteredRows);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HomeAction_UsesCurrentPeriodState()
    {
        using var viewModel = new MainViewModel(new TestServices { Settings = new UserSettings(Language: "en", Period: AnalysisPeriod.Day) });
        var window = Open(viewModel);
        try
        {
            Assert.Contains("last 24 hours", VisibleText(window), StringComparison.Ordinal);
            viewModel.ChangeSettings(viewModel.Settings with { Period = AnalysisPeriod.Month });
            Assert.Contains("last 30 days", VisibleText(window), StringComparison.Ordinal);
            viewModel.ChangeSettings(viewModel.Settings with { Period = AnalysisPeriod.Custom });
            var text = VisibleText(window);
            Assert.Contains("selected period", text, StringComparison.Ordinal);
            Assert.DoesNotContain("last 7 days", text, StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ImportWorkflow_ExplainsDiagnosticScopeWithoutAssumingAPeriod()
    {
        using var viewModel = new MainViewModel(new TestServices { Settings = new UserSettings(Language: "en") });
        var window = Open(viewModel);
        try
        {
            viewModel.OpenAnalyze(AnalysisMode.Files);
            Assert.Contains(viewModel.Text.Get("ImportScope"), VisibleText(window), StringComparison.Ordinal);
            Assert.DoesNotContain(viewModel.Text.Get("ProductScope"), VisibleText(window), StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void LocalizationResourcesHaveExactKeyAndPlaceholderParity()
    {
        var localizationDirectory = Path.Combine(FindRepositoryRoot(), "src", "FaultWitness.Localization");
        var files = Directory.GetFiles(localizationDirectory, "Strings*.resx");
        Assert.Equal(8, files.Length);
        var valuesByLocale = files.ToDictionary(
            path => Path.GetFileNameWithoutExtension(path) == "Strings" ? "en" : Path.GetFileNameWithoutExtension(path)["Strings.".Length..],
            path => XDocument.Load(path).Root!.Elements("data").ToDictionary(
                element => (string)element.Attribute("name")!, element => element.Element("value")!.Value));
        Assert.Equal(Locales.OrderBy(locale => locale, StringComparer.Ordinal), valuesByLocale.Keys.OrderBy(locale => locale, StringComparer.Ordinal));

        var english = valuesByLocale["en"];
        foreach (var locale in Locales)
        {
            var localized = valuesByLocale[locale];
            Assert.True(english.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(localized.Keys), $"Key parity failed for {locale}.");
            foreach (var key in ChangedKeys)
            {
                Assert.True(localized.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value), $"{locale} is missing {key}.");
                Assert.Equal(Placeholders(english[key]), Placeholders(value));
            }

            var service = new LocalizationService();
            service.SetCulture(locale);
            foreach (var key in ChangedKeys)
            {
                Assert.Equal(localized[key], service.Get(key));
                if (locale != "en" && key != "BackgroundEntriesCount")
                    Assert.NotEqual(english[key], service.Get(key));
            }
        }
    }

    [AvaloniaFact]
    public async Task ScopeClarityScreens_RenderSyntheticMatrix()
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_BETA_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);
        var sizes = new[] { (Width: 600, Height: 900, Name: "compact"), (Width: 1280, Height: 900, Name: "desktop") };
        foreach (var locale in new[] { "en", "de" })
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        foreach (var size in sizes)
        foreach (var scenario in new[] { "home", "analyze", "quiet", "background-only", "filter-empty" })
        {
            var result = scenario switch
            {
                "home" or "analyze" => SyntheticResults.Create(3),
                "background-only" => SyntheticResults.Create(0) with { Incidents = [BackgroundIncident()] },
                "filter-empty" => SyntheticResults.Create(1),
                _ => SyntheticResults.Create(0) with
                {
                    Coverage = [
                        new(SourceType.EventLog, CoverageState.Complete, null, null, "synthetic", "System"),
                        new(SourceType.Wer, CoverageState.Partial, null, null, "synthetic"),
                        new(SourceType.CrashArtifact, CoverageState.AccessDenied, null, null, "synthetic")]
                }
            };
            var services = new TestServices { Result = result, Settings = new UserSettings(Language: locale, Theme: theme) };
            using var viewModel = new MainViewModel(services);
            var window = Open(viewModel, size.Width, size.Height);
            try
            {
                await viewModel.AnalyzeAsync();
                if (scenario == "analyze") viewModel.OpenAnalyze(AnalysisMode.Recent);
                else if (scenario == "filter-empty")
                {
                    viewModel.Navigate(AppPage.Incidents);
                    window.UpdateLayout();
                    viewModel.SetFilter(new(Search: "no-match"));
                    viewModel.Navigate(AppPage.Home);
                    viewModel.Navigate(AppPage.Incidents);
                }
                else viewModel.Navigate(AppPage.Home);
                window.UpdateLayout();
                if (scenario == "quiet" || scenario == "background-only") Find<Border>(window, "QuietResult").BringIntoView();
                if (scenario == "filter-empty")
                {
                    Assert.Empty(viewModel.FilteredRows);
                    Assert.True(Find<Border>(window, "FilterEmpty").IsVisible);
                    Assert.NotNull(FindButton(window, "ResetFilters"));
                    Assert.Contains(viewModel.Text.Get("NoFilterMatches"), VisibleText(window), StringComparison.Ordinal);
                    Find<Border>(window, "FilterEmpty").BringIntoView();
                }
                window.UpdateLayout();
                Render(window, output, $"{scenario}-{locale}-{theme.ToString().ToLowerInvariant()}-{size.Name}-{size.Width}x{size.Height}");
            }
            finally { window.Close(); }
        }
    }

    private static Incident BackgroundIncident()
    {
        var incident = SyntheticResults.Incident(0, new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
        return incident with { Findings = incident.Findings.Select(finding => finding with { Disposition = FindingDisposition.Context }).ToArray() };
    }

    private static MainWindow Open(MainViewModel viewModel, int width = 1280, int height = 900)
    {
        var window = new MainWindow(viewModel) { Width = width, Height = height };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static T Find<T>(MainWindow window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(item => item.Name == name);
    private static Button? FindButton(MainWindow window, string name) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(item => item.Name == name);
    private static string VisibleText(MainWindow window)
    {
        window.UpdateLayout();
        return string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Where(item => item.IsVisible).Select(item => item.Text));
    }
    private static string[] Placeholders(string value) => Regex.Matches(value, @"\{(\d+)(?:[^}]*)\}").Cast<Match>().Select(match => match.Groups[1].Value).OrderBy(index => index, StringComparer.Ordinal).ToArray();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "FaultWitness.Localization"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the FaultWitness repository root.");
    }

    private static void Render(MainWindow window, string? output, string name)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{name} did not render.");
        Assert.True(frame.PixelSize.Width > 0 && frame.PixelSize.Height > 0, $"{name} produced an empty frame.");
        if (!string.IsNullOrWhiteSpace(output)) frame.Save(Path.Combine(output, name + ".png"), new PngBitmapEncoderOptions());
    }
}
