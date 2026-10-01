using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;
using FaultWitness.Storage;

#pragma warning disable CA2000 // Each test transfers its view model to a window that disposes it when closed.

namespace FaultWitness.UI.Tests;

[Trait("Suite", "CorePagesAxaml")]
public sealed class CorePagesAxamlTests
{
    private static readonly string[] Locales = ["en", "it", "es", "fr", "de", "pt", "ru", "pl"];
    private static readonly double[] Widths = [560, 600, 640, 641, 1008, 1280, 1920];

    [AvaloniaFact]
    public void HomeAxaml_PreservesScopeRecentPriorityBackgroundQuietCoverageAndActions()
    {
        var viewModel = new MainViewModel(new TestServices { Settings = new UserSettings(Language: "en") });
        var window = Open(viewModel);
        try
        {
            var result = SyntheticResults.Create(4);
            viewModel.SetResult(result);
            var home = Current<HomeView>(window);
            Assert.Contains(viewModel.Text.Get("ProductScope"), VisibleText(home), StringComparison.Ordinal);
            Assert.NotEmpty(home.FindControl<ListBox>("RecentSignificantList")!.Items);
            Assert.True(home.FindControl<Button>("PrimaryAnalyze")!.IsVisible);

            home.FindControl<Button>("PrimaryAnalyze")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(AppPage.Analyze, viewModel.Page);
            Assert.Equal(AnalysisMode.Recent, viewModel.AnalysisMode);
            viewModel.Navigate(AppPage.Home);
            Assert.Same(home, Current<HomeView>(window));

            var background = SyntheticResults.Incident(0, DateTimeOffset.UtcNow);
            background = background with { Findings = background.Findings.Select(item => item with { Disposition = FindingDisposition.Context }).ToArray() };
            viewModel.SetResult(SyntheticResults.Create(0) with { Incidents = [background] });
            Assert.Same(home, Current<HomeView>(window));
            Assert.True(home.FindControl<Border>("QuietResult")!.IsVisible);
            var backgroundText = VisibleText(home);
            var quietTitle = ((HomePresentation)home.DataContext!).QuietTitle;
            Assert.StartsWith("No incidents needing attention or worth noting were found for ", quietTitle, StringComparison.Ordinal);
            Assert.DoesNotContain("{0}", quietTitle, StringComparison.Ordinal);
            Assert.Contains(viewModel.Text.Get("PriorityBackground"), backgroundText, StringComparison.Ordinal);
            Assert.Contains(viewModel.Text.Format("BackgroundEntriesCount", viewModel.Text.Get("PriorityBackground"), "1"), backgroundText, StringComparison.Ordinal);
            Assert.DoesNotContain("background fault", backgroundText, StringComparison.OrdinalIgnoreCase);
            Assert.True(home.FindControl<Button>("ViewAllIncidents")!.IsVisible);

            var complete = SyntheticResults.Create(0) with
            {
                Coverage = [new(SourceType.EventLog, CoverageState.Complete, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, "synthetic", "System")]
            };
            viewModel.SetResult(complete);
            var quiet = VisibleText(home);
            Assert.Contains("No supported stability incidents were found in the examined records for ", ((HomePresentation)home.DataContext!).QuietTitle, StringComparison.Ordinal);
            Assert.Contains(viewModel.Text.Get("QuietResultCaution"), quiet, StringComparison.Ordinal);
            Assert.False(((HomePresentation)home.DataContext!).HasLimitedCoverage);

            var limited = complete with { Coverage = [new(SourceType.Wer, CoverageState.Partial, null, null, "synthetic")] };
            viewModel.SetResult(limited);
            Assert.Contains(viewModel.Text.Get("CoveragePartial"), VisibleText(home), StringComparison.Ordinal);
            Assert.True(((HomePresentation)home.DataContext!).HasLimitedCoverage);
            home.FindControl<Button>("StartAroundFlow")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(AnalysisMode.Around, viewModel.AnalysisMode);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AnalyzeAxaml_KeepsCustomInputsAcrossRuntimeUpdatesAndRunsTheSelectedRange()
    {
        var services = new TestServices { Result = SyntheticResults.Create(1), Settings = new UserSettings(Language: "en") };
        var viewModel = new MainViewModel(services);
        var window = Open(viewModel);
        try
        {
            viewModel.OpenAnalyze(AnalysisMode.Recent);
            var view = Current<AnalyzeView>(window);
            Assert.Contains(viewModel.Text.Get("ProductScope"), VisibleText(view), StringComparison.Ordinal);
            var period = view.FindControl<ComboBox>("PeriodSelector")!;
            period.SelectedIndex = (int)AnalysisPeriod.Custom;
            var from = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
            var to = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
            view.FindControl<DatePicker>("FromDate")!.SelectedDate = from;
            view.FindControl<DatePicker>("ToDate")!.SelectedDate = to;
            Assert.True(view.FindControl<WrapPanel>("CustomPeriodFields")!.IsVisible);

            viewModel.ChangeSettings(viewModel.Settings with { Language = "de", Theme = AppTheme.Dark });
            Assert.Same(view, Current<AnalyzeView>(window));
            Assert.Equal((int)AnalysisPeriod.Custom, period.SelectedIndex);
            Assert.Equal(from, view.FindControl<DatePicker>("FromDate")!.SelectedDate);
            Assert.Equal(to, view.FindControl<DatePicker>("ToDate")!.SelectedDate);
            window.Width = 600; window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Same(view, Current<AnalyzeView>(window));
            Assert.Equal((int)AnalysisPeriod.Custom, period.SelectedIndex);

            view.FindControl<Button>("RunAnalysis")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => services.Saved == 1);
            Assert.Equal(DateTimeInput.Combine(from, TimeSpan.Zero).ToUniversalTime(), services.From);
            Assert.Equal(DateTimeInput.Combine(to, new TimeSpan(23, 59, 59)).ToUniversalTime(), services.To);
            Assert.Equal(AppPage.Home, viewModel.Page);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AnalyzeAxaml_PreservesKnownTimeImportValidationCompletionAndErrorStates()
    {
        var services = new TestServices { Result = SyntheticResults.Create(1), Settings = new UserSettings(Language: "en") };
        var viewModel = new MainViewModel(services);
        var window = Open(viewModel);
        try
        {
            viewModel.OpenAnalyze(AnalysisMode.Around);
            var around = Current<AnalyzeView>(window);
            var aroundDate = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
            var aroundClock = new TimeSpan(10, 23, 0);
            around.FindControl<DatePicker>("AroundDate")!.SelectedDate = aroundDate;
            around.FindControl<TimePicker>("AroundTime")!.SelectedTime = aroundClock;
            around.FindControl<ComboBox>("AroundWindow")!.SelectedIndex = 2;
            viewModel.ChangeSettings(viewModel.Settings with { Language = "de", Theme = AppTheme.Dark });
            Assert.Same(around, Current<AnalyzeView>(window));
            Assert.Equal(aroundDate, around.FindControl<DatePicker>("AroundDate")!.SelectedDate);
            Assert.Equal(aroundClock, around.FindControl<TimePicker>("AroundTime")!.SelectedTime);
            Assert.Equal(2, around.FindControl<ComboBox>("AroundWindow")!.SelectedIndex);
            around.FindControl<Button>("RunAroundAnalysis")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => services.Saved == 1);
            var center = DateTimeInput.Combine(aroundDate, aroundClock).ToUniversalTime();
            Assert.Equal(center.AddMinutes(-10), services.From);
            Assert.Equal(center.AddMinutes(10), services.To);

            viewModel.OpenAnalyze(AnalysisMode.Files);
            var files = Current<AnalyzeView>(window);
            var importText = VisibleText(files);
            Assert.Contains(viewModel.Text.Get("ImportScope"), importText, StringComparison.Ordinal);
            Assert.DoesNotContain(viewModel.Text.Get("ProductScope"), importText, StringComparison.Ordinal);
            viewModel.AddImports([Path.Combine(Path.GetTempPath(), "synthetic.evtx")]);
            Assert.Same(files, Current<AnalyzeView>(window));
            Assert.Contains("synthetic.evtx", ((AnalyzePresentation)files.DataContext!).Imports.Select(item => item.Name));
            Assert.True(files.FindControl<Button>("AnalyzeImports")!.IsVisible);
            files.FindControl<Button>("AnalyzeImports")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => viewModel.IsImported);
            Assert.Equal(AppPage.Home, viewModel.Page);
            Assert.Equal(2, services.Saved);

            viewModel.OpenAnalyze(AnalysisMode.Recent);
            var recent = Current<AnalyzeView>(window);
            recent.FindControl<ComboBox>("PeriodSelector")!.SelectedIndex = (int)AnalysisPeriod.Custom;
            recent.FindControl<DatePicker>("FromDate")!.SelectedDate = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
            recent.FindControl<DatePicker>("ToDate")!.SelectedDate = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
            var savedBeforeInvalidRange = services.Saved;
            recent.FindControl<Button>("RunAnalysis")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("InvalidTimeRange", viewModel.StatusKey);
            Assert.Equal(savedBeforeInvalidRange, services.Saved);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AnalyzeAxaml_ReportsRunningCancellationAndErrorsThroughTheExistingShell()
    {
        var waitingServices = new TestServices { WaitForCancellation = true, Settings = new UserSettings(Language: "en") };
        var waitingVm = new MainViewModel(waitingServices);
        var waitingWindow = Open(waitingVm);
        try
        {
            waitingVm.OpenAnalyze(AnalysisMode.Recent);
            Current<AnalyzeView>(waitingWindow).FindControl<Button>("RunAnalysis")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => waitingVm.IsBusy);
            Assert.True(waitingWindow.FindControl<Button>("CancelAnalysis")!.IsVisible);
            Assert.True(waitingVm.HasVisibleStatus);
            waitingWindow.FindControl<Button>("CancelAnalysis")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => !waitingVm.IsBusy);
            Assert.Equal("AnalysisCancelled", waitingVm.StatusKey);
        }
        finally { waitingWindow.Close(); }

        var failingServices = new TestServices { FailAnalysis = true, Settings = new UserSettings(Language: "en") };
        var failingVm = new MainViewModel(failingServices);
        var failingWindow = Open(failingVm);
        try
        {
            failingVm.OpenAnalyze(AnalysisMode.Recent);
            Current<AnalyzeView>(failingWindow).FindControl<Button>("RunAnalysis")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => !failingVm.IsBusy && failingVm.StatusKey == "AnalysisError");
            Assert.True(failingWindow.FindControl<TextBlock>("StatusText")!.IsVisible);
            Assert.True(failingWindow.FindControl<Expander>("TechnicalError")!.IsVisible);
        }
        finally { failingWindow.Close(); }
    }

    [AvaloniaFact]
    public void IncidentsAxaml_PreservesFiltersBackgroundSelectionAndResetOnlyForHiddenRows()
    {
        var viewModel = new MainViewModel(new TestServices { Settings = new UserSettings(Language: "en") });
        var window = Open(viewModel);
        try
        {
            viewModel.SetResult(SyntheticResults.Create(4));
            viewModel.Navigate(AppPage.Incidents);
            var view = Current<IncidentsView>(window);
            var list = view.FindControl<ListBox>("IncidentList")!;
            Assert.Equal(viewModel.FilteredRows.Count, list.Items.Count);
            Assert.False(view.FindControl<Button>("ResetFilters")!.IsVisible);

            view.FindControl<ComboBox>("PriorityFilter")!.SelectedIndex = (int)AttentionLevel.Background;
            Assert.Equal(AttentionLevel.Background, viewModel.Filter.Priority);
            Assert.NotEmpty(viewModel.FilteredRows);
            Assert.All(viewModel.FilteredRows, row => Assert.Equal(AttentionLevel.Background, row.Priority));

            view.FindControl<ComboBox>("CategoryFilter")!.SelectedIndex = Array.IndexOf(Enum.GetValues<IncidentCategory>(), IncidentCategory.Graphics) + 1;
            Assert.Equal(IncidentCategory.Graphics, viewModel.Filter.Category);
            view.FindControl<ComboBox>("StrengthFilter")!.SelectedIndex = Array.IndexOf(Enum.GetValues<EvidenceStrength>(), EvidenceStrength.Moderate) + 1;
            Assert.Equal(EvidenceStrength.Moderate, viewModel.Filter.Strength);
            viewModel.ChangeSettings(viewModel.Settings with { Language = "de", Theme = AppTheme.Dark });
            Assert.Same(view, Current<IncidentsView>(window));
            Assert.Equal((int)AttentionLevel.Background, view.FindControl<ComboBox>("PriorityFilter")!.SelectedIndex);
            Assert.Equal(Array.IndexOf(Enum.GetValues<IncidentCategory>(), IncidentCategory.Graphics) + 1, view.FindControl<ComboBox>("CategoryFilter")!.SelectedIndex);
            Assert.Equal(Array.IndexOf(Enum.GetValues<EvidenceStrength>(), EvidenceStrength.Moderate) + 1, view.FindControl<ComboBox>("StrengthFilter")!.SelectedIndex);

            var search = view.FindControl<TextBox>("IncidentSearch")!;
            search.Text = "no matching synthetic record";
            Assert.Empty(viewModel.FilteredRows);
            Assert.True(view.FindControl<Border>("FilterEmpty")!.IsVisible);
            Assert.True(view.FindControl<Button>("ResetFilters")!.IsVisible);
            Assert.Contains(viewModel.Text.Get("NoFilterMatches"), VisibleText(view), StringComparison.Ordinal);
            view.FindControl<Button>("ResetFilters")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.NotEmpty(viewModel.FilteredRows);
            Assert.False(view.FindControl<Button>("ResetFilters")!.IsVisible);

            viewModel.SetFilter(new());
            list.SelectedItem = viewModel.FilteredRows[0];
            Assert.Equal(AppPage.Detail, viewModel.Page);
            Assert.Equal(viewModel.FilteredRows[0].Incident.Id, viewModel.Selected!.Incident.Id);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task HistoryAxaml_PreservesEmptySelectedReopenKeyboardAndPresentationState()
    {
        var emptyVm = new MainViewModel(new TestServices { Settings = new UserSettings(Language: "en") });
        var emptyWindow = Open(emptyVm);
        try
        {
            emptyVm.Navigate(AppPage.History);
            var empty = Current<HistoryView>(emptyWindow);
            Assert.True(empty.FindControl<Border>("HistoryEmpty")!.IsVisible);
            Assert.False(empty.FindControl<ListBox>("HistoryList")!.IsVisible);
            Assert.False(empty.FindControl<ScrollViewer>("HistoryDetailScroll")!.IsVisible);
            Assert.True(empty.FindControl<Button>("RefreshHistory")!.IsVisible);
            Assert.Contains(emptyVm.Text.Get("HistoryEmptyTitle"), VisibleText(empty), StringComparison.Ordinal);
        }
        finally { emptyWindow.Close(); }

        var first = Stored("first", "SourceSystem=Complete", 1);
        var second = Stored("second", "SourceWer=Partial", 2);
        var services = new TestServices { History = [first, second], Settings = new UserSettings(Language: "en") };
        var viewModel = new MainViewModel(services);
        var window = Open(viewModel);
        try
        {
            await viewModel.RefreshHistoryAsync();
            viewModel.Navigate(AppPage.History);
            var view = Current<HistoryView>(window);
            var list = view.FindControl<ListBox>("HistoryList")!;
            Assert.Equal(2, list.Items.Count);
            var secondItem = list.Items.OfType<HistoryItemPresentation>().Single(item => item.Id == "second");
            list.SelectedItem = secondItem;
            Assert.Equal("second", viewModel.SelectedHistory!.Scan.Id);
            var coverage = view.FindControl<Expander>("HistoryCoverage")!;
            Assert.True(coverage.IsVisible, $"history coverage presentation: {((HistoryPresentation)view.DataContext!).Selected?.HasCoverageSummary}; metadata={viewModel.SelectedHistory?.Scan.Metadata?.CoverageSummary}");
            coverage.IsExpanded = true;
            window.UpdateLayout();
            Assert.Contains("SourceWer=Partial", VisibleText(view), StringComparison.Ordinal);
            Assert.Equal(viewModel.Text.Get("CopySavedSummary"), Avalonia.Automation.AutomationProperties.GetName(view.FindControl<Button>("CopyHistory")!));
            list.Focus();
            Assert.True(list.IsKeyboardFocusWithin, "History list should accept keyboard focus before runtime updates.");

            foreach (var language in Locales)
            foreach (var theme in new[] { AppTheme.System, AppTheme.Light, AppTheme.Dark })
            {
                viewModel.ChangeSettings(viewModel.Settings with { Language = language, Theme = theme });
                window.UpdateLayout();
                Assert.Same(view, Current<HistoryView>(window));
                Assert.Equal("second", viewModel.SelectedHistory!.Scan.Id);
                Assert.Equal("second", Assert.IsType<HistoryItemPresentation>(list.SelectedItem).Id);
                Assert.Contains(" · " + viewModel.Text.Get("SeverityHigh"), ((HistoryPresentation)view.DataContext!).Incidents[0].TimestampSeverity, StringComparison.Ordinal);
                Assert.True(list.IsKeyboardFocusWithin, $"History focus lost after {language}/{theme}.");
            }

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            window.UpdateLayout();
            Assert.Equal("first", viewModel.SelectedHistory!.Scan.Id);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EveryMigratedPage_RuntimeLocaleThemeAndAutomationStayInPlace()
    {
        var services = new TestServices { History = [Stored("runtime", "SourceSystem=Complete", 1)], Settings = new UserSettings(Language: "en") };
        var viewModel = new MainViewModel(services);
        var window = Open(viewModel);
        try
        {
            viewModel.SetResult(SyntheticResults.Create(1));
            foreach (var page in new[] { AppPage.Home, AppPage.Analyze, AppPage.Incidents, AppPage.History })
            {
                if (page == AppPage.Analyze) viewModel.OpenAnalyze(AnalysisMode.Recent);
                else
                {
                    if (page == AppPage.History) await viewModel.RefreshHistoryAsync();
                    viewModel.Navigate(page);
                }

                var original = window.FindControl<ContentControl>("PageHost")!.Content as Control ?? throw new InvalidOperationException("Missing AXAML page.");
                var target = page switch
                {
                    AppPage.Home => (Control)original.FindControl<Button>("PrimaryAnalyze")!,
                    AppPage.Analyze => original.FindControl<Button>("RunAnalysis")!,
                    AppPage.Incidents => original.FindControl<TextBox>("IncidentSearch")!,
                    _ => original.FindControl<ListBox>("HistoryList")!
                };
                var titleKey = page switch { AppPage.Home => "Home", AppPage.Analyze => "Analyze", AppPage.Incidents => "Incidents", _ => "History" };

                foreach (var language in Locales)
                foreach (var theme in new[] { AppTheme.System, AppTheme.Light, AppTheme.Dark })
                {
                    viewModel.ChangeSettings(viewModel.Settings with { Language = language, Theme = theme });
                    window.UpdateLayout();
                    Assert.Same(original, window.FindControl<ContentControl>("PageHost")!.Content);
                    Assert.Contains(viewModel.Text.Get(titleKey), VisibleText(original), StringComparison.Ordinal);
                    var expectedName = page switch
                    {
                        AppPage.Home => ((HomePresentation)original.DataContext!).PrimaryAction,
                        AppPage.Analyze => viewModel.Text.Get("RunSelectedAnalysis"),
                        AppPage.Incidents => viewModel.Text.Get("Search"),
                        _ => viewModel.Text.Get("History")
                    };
                    Assert.Equal(expectedName, AutomationProperties.GetName(target));
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MigratedPages_KeepTheirViewsAndFitTheRequiredWidths()
    {
        var services = new TestServices { History = [Stored("responsive", "SourceSystem=Complete", 1)], Settings = new UserSettings(Language: "en") };
        var viewModel = new MainViewModel(services);
        var window = Open(viewModel);
        try
        {
            viewModel.SetResult(SyntheticResults.Create(4));
            foreach (var (page, mode) in new[]
            {
                (AppPage.Home, AnalysisMode.Recent), (AppPage.Analyze, AnalysisMode.Recent),
                (AppPage.Incidents, AnalysisMode.Recent), (AppPage.History, AnalysisMode.Recent)
            })
            {
                if (page == AppPage.Analyze) viewModel.OpenAnalyze(mode);
                else viewModel.Navigate(page);
                if (page == AppPage.History) awaitHistory(viewModel).GetAwaiter().GetResult();
                var original = window.FindControl<ContentControl>("PageHost")!.Content;
                foreach (var width in Widths)
                {
                    window.Width = width; window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    Assert.Same(original, window.FindControl<ContentControl>("PageHost")!.Content);
                    var side = window.FindControl<Border>("NavigationSurface")!;
                    var main = window.FindControl<Grid>("MainRegion")!;
                    Assert.True(main.Bounds.X + 1 >= side.Bounds.Right, $"Navigation overlaps {page} at {width}: {side.Bounds} / {main.Bounds}");
                    AssertNoHorizontalOverflow((Control)original!, $"{page} at {width}");
                    AssertPrimaryAvailable(page, (Control)original!);
                }
            }
        }
        finally { window.Close(); }

        static Task awaitHistory(MainViewModel model) => model.RefreshHistoryAsync();
    }

    [AvaloniaFact]
    public void BoundedPass2b1VisualMatrix_RendersFortyOneSyntheticCases()
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2B1_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);
        var index = 0;
        var visualModes = new[]
        {
            ("en-light-desktop", "en", AppTheme.Light, 1280, 900),
            ("en-dark-desktop", "en", AppTheme.Dark, 1280, 900),
            ("de-dark-compact", "de", AppTheme.Dark, 600, 900),
            ("it-light-large", "it", AppTheme.Light, 1920, 1080)
        };

        foreach (var scenario in new[] { "relevant", "background", "quiet-complete", "quiet-limited" })
        foreach (var mode in visualModes)
        {
            usingWindow(RenderHome(scenario, mode), $"home-{scenario}-{mode.Item1}", mode.Item4, mode.Item5);
        }

        var analyzeCases = new (string State, string Variant)[]
        {
            ("recent", "en-light-desktop"), ("recent", "en-dark-desktop"), ("recent", "de-dark-compact"),
            ("custom", "en-light-desktop"), ("custom", "de-dark-compact"), ("custom", "it-light-large"),
            ("around", "en-dark-desktop"), ("around", "de-dark-compact"), ("around", "it-light-large"),
            ("import", "en-light-desktop"), ("import", "en-dark-desktop"), ("import", "it-light-large"),
            ("validation", "en-dark-desktop"), ("validation", "de-dark-compact"), ("validation", "it-light-large")
        };
        foreach (var (state, variant) in analyzeCases)
        {
            var mode = visualModes.Single(item => item.Item1 == variant);
            var window = RenderAnalyze(state, mode);
            usingWindow(window, $"analyze-{state}-{variant}", mode.Item4, mode.Item5);
        }

        foreach (var (state, variant) in new[]
        {
            ("populated", "en-light-desktop"), ("populated", "it-light-large"),
            ("filtered", "en-dark-desktop"), ("filtered", "de-dark-compact"),
            ("filter-empty", "de-dark-compact"), ("filter-empty", "en-light-desktop")
        })
        {
            var mode = visualModes.Single(item => item.Item1 == variant);
            var window = RenderIncidents(state, mode);
            usingWindow(window, $"incidents-{state}-{variant}", mode.Item4, mode.Item5);
        }

        foreach (var (state, variant) in new[]
        {
            ("empty", "en-light-desktop"), ("empty", "de-dark-compact"),
            ("selected", "en-dark-desktop"), ("selected", "it-light-large")
        })
        {
            var mode = visualModes.Single(item => item.Item1 == variant);
            var window = RenderHistory(state, mode);
            usingWindow(window, $"history-{state}-{variant}", mode.Item4, mode.Item5);
        }

        Assert.Equal(41, index);

        void usingWindow(MainWindow window, string name, int requestedWidth, int requestedHeight)
        {
            try
            {
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var page = window.FindControl<ContentControl>("PageHost")!.Content as Control ?? throw new InvalidOperationException("Missing page content.");
                AssertNoHorizontalOverflow(page, name);
                var side = window.FindControl<Border>("NavigationSurface")!;
                var main = window.FindControl<Grid>("MainRegion")!;
                Assert.True(main.Bounds.X + 1 >= side.Bounds.Right, $"Navigation overlaps in {name}.");
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{name} did not render.");
                VisualRenderGeometry.AssertFrameMatches(window, frame, requestedWidth, requestedHeight, name);
                if (!string.IsNullOrWhiteSpace(output)) frame.Save(Path.Combine(output, $"{index:D2}-{name}.png"), new PngBitmapEncoderOptions());
                index++;
            }
            finally { window.Close(); }
        }
    }

    private static MainWindow RenderHome(string scenario, (string Name, string Language, AppTheme Theme, int Width, int Height) mode)
    {
        ScanResult result = scenario switch
        {
            "relevant" => SyntheticResults.Create(4),
            "background" => BackgroundOnlyResult(),
            "quiet-complete" => SyntheticResults.Create(0) with { Coverage = [Coverage(CoverageState.Complete)] },
            _ => SyntheticResults.Create(0) with { Coverage = [Coverage(CoverageState.Partial)] }
        };
        var window = OpenForVisualRender(new MainViewModel(new TestServices { Result = result, Settings = new UserSettings(Language: mode.Language, Theme: mode.Theme) }), mode.Width, mode.Height);
        window.ViewModel.SetResult(result);
        return window;
    }

    private static MainWindow RenderAnalyze(string state, (string Name, string Language, AppTheme Theme, int Width, int Height) mode)
    {
        var window = OpenForVisualRender(new MainViewModel(new TestServices { Settings = new UserSettings(Language: mode.Language, Theme: mode.Theme) }), mode.Width, mode.Height);
        switch (state)
        {
            case "around": window.ViewModel.OpenAnalyze(AnalysisMode.Around); break;
            case "import": window.ViewModel.OpenAnalyze(AnalysisMode.Files); window.ViewModel.AddImports([Path.Combine(Path.GetTempPath(), "synthetic.evtx")]); break;
            default:
                window.ViewModel.OpenAnalyze(AnalysisMode.Recent);
                if (state == "custom") Current<AnalyzeView>(window).FindControl<ComboBox>("PeriodSelector")!.SelectedIndex = (int)AnalysisPeriod.Custom;
                if (state == "validation")
                {
                    Current<AnalyzeView>(window).FindControl<ComboBox>("PeriodSelector")!.SelectedIndex = (int)AnalysisPeriod.Custom;
                    Current<AnalyzeView>(window).FindControl<DatePicker>("FromDate")!.SelectedDate = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
                    Current<AnalyzeView>(window).FindControl<DatePicker>("ToDate")!.SelectedDate = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
                    Current<AnalyzeView>(window).FindControl<Button>("RunAnalysis")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                }
                break;
        }
        window.UpdateLayout();
        return window;
    }

    private static MainWindow RenderIncidents(string state, (string Name, string Language, AppTheme Theme, int Width, int Height) mode)
    {
        var viewModel = new MainViewModel(new TestServices { Settings = new UserSettings(Language: mode.Language, Theme: mode.Theme) });
        var window = OpenForVisualRender(viewModel, mode.Width, mode.Height);
        viewModel.SetResult(SyntheticResults.Create(4));
        viewModel.Navigate(AppPage.Incidents);
        if (state == "filtered") viewModel.SetFilter(new(Priority: AttentionLevel.Attention));
        if (state == "filter-empty") viewModel.SetFilter(new(Search: "no matching synthetic incident"));
        window.UpdateLayout();
        return window;
    }

    private static MainWindow RenderHistory(string state, (string Name, string Language, AppTheme Theme, int Width, int Height) mode)
    {
        StoredScan[] scans = state == "empty" ? [] : [Stored("selected", "SourceWer=Partial", 3), Stored("older", "SourceSystem=Complete", 1)];
        var viewModel = new MainViewModel(new TestServices { History = scans, Settings = new UserSettings(Language: mode.Language, Theme: mode.Theme) });
        var window = OpenForVisualRender(viewModel, mode.Width, mode.Height);
        if (scans.Length > 0)
        {
            WaitFor(() => viewModel.RefreshHistoryAsync()).GetAwaiter().GetResult();
            viewModel.Navigate(AppPage.History);
            viewModel.SelectHistory(viewModel.History[0]);
        }
        else viewModel.Navigate(AppPage.History);
        window.UpdateLayout();
        return window;
    }

    private static Incident BackgroundOnly()
    {
        var incident = SyntheticResults.Incident(0, DateTimeOffset.UtcNow);
        return incident with { Findings = incident.Findings.Select(item => item with { Disposition = FindingDisposition.Context }).ToArray() };
    }

    private static ScanResult BackgroundOnlyResult() => SyntheticResults.Create(0) with { Incidents = [BackgroundOnly()] };
    private static SourceCoverage Coverage(CoverageState state) => new(SourceType.EventLog, state, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, "synthetic", "System");

    private static StoredScan Stored(string id, string coverage, int incidentCount)
    {
        var started = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var incidents = Enumerable.Range(0, incidentCount).Select(index => new StoredIncident($"{id}-{index}", started.AddMinutes(index), "Graphics", "High", $"signature-{id}-{index}", "{}")).ToArray();
        var metadata = new ScanHistoryMetadata("recent", started, started.AddMinutes(1), 10, 1, 0, incidentCount, coverage);
        return new StoredScan(id, started, started.AddMinutes(1), "0.9.1", metadata, incidents);
    }

    private static MainWindow Open(MainViewModel viewModel, double width = 1280, double height = 900)
    {
#pragma warning disable CA2000 // MainWindow owns and disposes the supplied view model on Closed.
        var window = new MainWindow(viewModel);
#pragma warning restore CA2000
        window.Width = width; window.Height = height; window.Show(); window.UpdateLayout();
        return window;
    }

    private static MainWindow OpenForVisualRender(MainViewModel viewModel, int width, int height)
    {
#pragma warning disable CA2000 // MainWindow owns and disposes the supplied view model on Closed.
        var window = new MainWindow(viewModel);
#pragma warning restore CA2000
        VisualRenderGeometry.ShowAtRequestedGeometry(window, width, height);
        return window;
    }

    private static T Current<T>(MainWindow window) where T : Control
    {
        window.UpdateLayout();
        return Assert.IsType<T>(window.FindControl<ContentControl>("PageHost")!.Content);
    }

    private static string VisibleText(Control root) => string.Join("\n", root.GetVisualDescendants().OfType<TextBlock>().Where(item => item.IsVisible).Select(item => item.Text));

    private static async Task WaitFor(Func<bool> predicate)
    {
        for (var index = 0; index < 200 && !predicate(); index++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(predicate(), "The expected presentation operation did not complete.");
    }

    private static async Task WaitFor(Func<Task> action)
    {
        await action();
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertPrimaryAvailable(AppPage page, Control content)
    {
        var name = page switch
        {
            AppPage.Home => "PrimaryAnalyze", AppPage.Analyze => "RunAnalysis",
            AppPage.Incidents => "IncidentList", AppPage.History => "HistoryList", _ => string.Empty
        };
        var primary = content.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == name);
        Assert.NotNull(primary);
        Assert.True(primary!.IsVisible, $"{name} is hidden on {page}.");
    }

    private static void AssertNoHorizontalOverflow(Control root, string? context = null)
    {
        foreach (var viewer in root.GetVisualDescendants().OfType<ScrollViewer>().Where(item => item.Name != "PART_ScrollViewer"))
            Assert.True(viewer.Viewport.Width == 0 || viewer.Extent.Width <= viewer.Viewport.Width + 2,
                $"Horizontal overflow {context}: {viewer.Name} extent={viewer.Extent.Width} viewport={viewer.Viewport.Width}");
    }
}

#pragma warning restore CA2000
