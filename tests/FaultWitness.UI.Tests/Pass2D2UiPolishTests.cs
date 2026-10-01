using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;
using FaultWitness.Localization;
using FaultWitness.Storage;

#pragma warning disable CA2000 // MainWindow owns and disposes its view model.

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Pass2D2UiPolish")]
public sealed class Pass2D2UiPolishTests
{
    private static readonly string[] Locales = ["en", "it", "es", "fr", "de", "pt", "ru", "pl"];
    private static readonly string[] FilterFieldNames = ["PriorityFilterField", "CategoryFilterField", "StrengthFilterField", "SearchFilterField"];

    [AvaloniaFact]
    public void IncidentsFilters_ReflowFromAvailableContentWidthAndRetainState()
    {
        var viewModel = new MainViewModel(new TestServices { Settings = new UserSettings(Language: "en") });
        var window = Open(viewModel);
        try
        {
            viewModel.SetResult(SyntheticResults.Create(8));
            viewModel.Navigate(AppPage.Incidents);
            var view = Current<IncidentsView>(window);
            var filters = view.FindControl<Grid>("IncidentFilters")!;
            var priority = view.FindControl<ComboBox>("PriorityFilter")!;
            var category = view.FindControl<ComboBox>("CategoryFilter")!;
            var strength = view.FindControl<ComboBox>("StrengthFilter")!;
            var search = view.FindControl<TextBox>("IncidentSearch")!;
            priority.SelectedIndex = 3;
            category.SelectedIndex = Array.IndexOf(Enum.GetValues<IncidentCategory>(), IncidentCategory.Graphics) + 1;
            strength.SelectedIndex = Array.IndexOf(Enum.GetValues<EvidenceStrength>(), EvidenceStrength.Moderate) + 1;
            search.Text = "SearchTarget";
            Dispatcher.UIThread.RunJobs();
            search.Focus(NavigationMethod.Tab);

            var expectedFilter = viewModel.Filter;
            foreach (var width in new[] { 560, 600, 640, 641, 1008, 1280, 1920 })
            {
                Resize(window, width);
                var availableWidth = filters.Bounds.Width;
                var columns = ColumnsFor(availableWidth);
                Assert.Equal(columns, filters.ColumnDefinitions.Count);
                Assert.Equal((4 + columns - 1) / columns, filters.RowDefinitions.Count);
                Assert.Equal(expectedFilter, viewModel.Filter);
                Assert.Equal("SearchTarget", search.Text);
                Assert.Equal(viewModel.Text.Get("Search"), AutomationProperties.GetName(search));
                Assert.True(search.IsKeyboardFocusWithin, $"Search lost focus at {width}px outer width / {availableWidth:0.##}px available filter width.");
                AssertFilterRows(filters, columns);
                AssertNoHorizontalOverflow(view, $"Incidents at {width}px");
            }

            foreach (var (locale, theme) in Locales.Select((locale, index) => (locale, (index % 2) == 0 ? AppTheme.Dark : AppTheme.Light)))
            {
                viewModel.ChangeSettings(viewModel.Settings with { Language = locale, Theme = theme });
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Same(view, Current<IncidentsView>(window));
                Assert.Equal(expectedFilter, viewModel.Filter);
                Assert.Equal("SearchTarget", search.Text);
                Assert.Equal(viewModel.Text.Get("Search"), AutomationProperties.GetName(search));
                Assert.Equal(viewModel.Text.Get("Search"), view.FindControl<StackPanel>("SearchFilterField")!.Children.OfType<TextBlock>().First().Text);
                Assert.True(search.IsKeyboardFocusWithin, $"Search lost focus after {locale}/{theme} update.");
                Assert.Equal(3, priority.SelectedIndex);
                Assert.Equal(Array.IndexOf(Enum.GetValues<IncidentCategory>(), IncidentCategory.Graphics) + 1, category.SelectedIndex);
                Assert.Equal(Array.IndexOf(Enum.GetValues<EvidenceStrength>(), EvidenceStrength.Moderate) + 1, strength.SelectedIndex);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void IncidentFilterSelectors_KeepEveryExistingValueMapping()
    {
        var viewModel = new MainViewModel(new TestServices { Settings = new UserSettings(Language: "en") });
        var window = Open(viewModel);
        try
        {
            viewModel.SetResult(SyntheticResults.Create(8));
            viewModel.Navigate(AppPage.Incidents);
            var view = Current<IncidentsView>(window);
            var priority = view.FindControl<ComboBox>("PriorityFilter")!;
            var category = view.FindControl<ComboBox>("CategoryFilter")!;
            var strength = view.FindControl<ComboBox>("StrengthFilter")!;

            for (var index = 0; index < priority.Items.Count; index++)
            {
                priority.SelectedIndex = index;
                Assert.Equal(index == 3 ? null : (AttentionLevel?)index, viewModel.Filter.Priority);
            }

            category.SelectedIndex = 0;
            Assert.Null(viewModel.Filter.Category);
            foreach (var (value, index) in Enum.GetValues<IncidentCategory>().Select((value, index) => (value, index)))
            {
                category.SelectedIndex = index + 1;
                Assert.Equal(value, viewModel.Filter.Category);
            }

            strength.SelectedIndex = 0;
            Assert.Null(viewModel.Filter.Strength);
            foreach (var (value, index) in Enum.GetValues<EvidenceStrength>().Select((value, index) => (value, index)))
            {
                strength.SelectedIndex = index + 1;
                Assert.Equal(value, viewModel.Filter.Strength);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SettingsAnalysisAndPrivacyFieldsShareGeneralLeftInset()
    {
        var viewModel = new MainViewModel(new TestServices { Settings = new UserSettings(Language: "en") });
        var window = Open(viewModel);
        try
        {
            viewModel.Navigate(AppPage.Settings);
            var settings = Current<SettingsView>(window);
            var content = settings.FindControl<StackPanel>("SettingsContent")!;
            var language = settings.FindControl<StackPanel>("LanguageSettingField")!;
            var period = settings.FindControl<StackPanel>("PeriodSettingField")!;
            var retention = settings.FindControl<StackPanel>("RetentionSettingField")!;
            Assert.Equal(HorizontalAlignment.Left, period.HorizontalAlignment);
            Assert.Equal(HorizontalAlignment.Left, retention.HorizontalAlignment);

            foreach (var width in new[] { 600, 1280, 1920 })
            {
                Resize(window, width);
                var languageX = LeftOf(language, content);
                Assert.InRange(Math.Abs(LeftOf(period, content) - languageX), 0, 0.5);
                Assert.InRange(Math.Abs(LeftOf(retention, content) - languageX), 0, 0.5);
                AssertNoHorizontalOverflow(settings, $"Settings at {width}px");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AnalyzeForm_IsBoundedAtLargeWidthsAndFillsCompactAvailableWidth()
    {
        var viewModel = new MainViewModel(new TestServices { Settings = new UserSettings(Language: "en") });
        var window = Open(viewModel);
        try
        {
            viewModel.OpenAnalyze(AnalysisMode.Recent);
            var view = Current<AnalyzeView>(window);
            var content = view.FindControl<StackPanel>("AnalyzeContent")!;
            var pageRegion = view.FindControl<Grid>("AnalyzePageRegion")!;
            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
            view.FindControl<ComboBox>("PeriodSelector")!.SelectedIndex = (int)AnalysisPeriod.Custom;
            var fromDate = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
            var toDate = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
            view.FindControl<DatePicker>("FromDate")!.SelectedDate = fromDate;
            view.FindControl<DatePicker>("ToDate")!.SelectedDate = toDate;

            foreach (var width in new[] { 600, 1280, 1920 })
            {
                Resize(window, width);
                Assert.InRange(content.Bounds.Width, 0.1, 800.1);
                Assert.True(content.Bounds.Width <= scroll.Viewport.Width + 1,
                    $"Analyze content {content.Bounds.Width:0.##}px exceeds available page width {scroll.Viewport.Width:0.##}px at {width}px.");
                Assert.InRange(Math.Abs(content.TranslatePoint(new Point(0, 0), pageRegion)!.Value.X), 0, 0.5);
                Assert.InRange(Math.Abs(pageRegion.Bounds.Width - scroll.Viewport.Width), 0, 1);
                if (scroll.Viewport.Width >= 800) Assert.InRange(content.Bounds.Width, 799.5, 800.1);
                Assert.Equal(AnalysisMode.Recent, viewModel.AnalysisMode);
                Assert.Equal((int)AnalysisPeriod.Custom, view.FindControl<ComboBox>("PeriodSelector")!.SelectedIndex);
                Assert.Equal(fromDate, view.FindControl<DatePicker>("FromDate")!.SelectedDate);
                Assert.Equal(toDate, view.FindControl<DatePicker>("ToDate")!.SelectedDate);
                AssertNoHorizontalOverflow(view, $"Analyze at {width}px");
            }

            foreach (var locale in Locales)
            {
                viewModel.ChangeSettings(viewModel.Settings with { Language = locale });
                window.UpdateLayout();
                Assert.Equal(fromDate, view.FindControl<DatePicker>("FromDate")!.SelectedDate);
                Assert.Equal(toDate, view.FindControl<DatePicker>("ToDate")!.SelectedDate);
                Assert.Equal((int)AnalysisPeriod.Custom, view.FindControl<ComboBox>("PeriodSelector")!.SelectedIndex);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AnalyzeDatePickers_KeepWindowsRegionalCultureAndQueryIntervalsAcrossUiLocaleChanges()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var regionalCulture = CultureInfo.GetCultureInfo("it-IT");
        CultureInfo.CurrentCulture = regionalCulture;
        var services = new TestServices { Settings = new UserSettings(Language: "en") };
        var viewModel = new MainViewModel(services);
        var window = Open(viewModel);
        try
        {
            viewModel.OpenAnalyze(AnalysisMode.Around);
            var view = Current<AnalyzeView>(window);
            var date = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
            var clock = new TimeSpan(10, 23, 0);
            var aroundDate = view.FindControl<DatePicker>("AroundDate")!;
            aroundDate.SelectedDate = date;
            window.UpdateLayout();
            var monthDisplay = aroundDate.GetVisualDescendants().OfType<TextBlock>().Single(textBlock => textBlock.Name == "PART_MonthTextBlock");
            var regionalMonth = date.ToString(aroundDate.MonthFormat, regionalCulture);
            Assert.Equal(regionalMonth, monthDisplay.Text);
            view.FindControl<TimePicker>("AroundTime")!.SelectedTime = clock;
            view.FindControl<ComboBox>("AroundWindow")!.SelectedIndex = 2;

            foreach (var locale in new[] { "en", "it", "de", "ru", "pl" })
            {
                viewModel.ChangeSettings(viewModel.Settings with { Language = locale });
                window.UpdateLayout();
                Assert.Equal(regionalCulture, CultureInfo.CurrentCulture);
                Assert.Equal(regionalMonth, monthDisplay.Text);
                Assert.Equal(date, view.FindControl<DatePicker>("AroundDate")!.SelectedDate);
                Assert.Equal(clock, view.FindControl<TimePicker>("AroundTime")!.SelectedTime);
                Assert.Equal(2, view.FindControl<ComboBox>("AroundWindow")!.SelectedIndex);
            }

            view.FindControl<Button>("RunAroundAnalysis")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => services.Saved == 1);
            var centerUtc = DateTimeInput.Combine(date, clock).ToUniversalTime();
            Assert.Equal(centerUtc.AddMinutes(-10), services.From);
            Assert.Equal(centerUtc.AddMinutes(10), services.To);
            Assert.Equal(AnalysisMode.Around, viewModel.AnalysisMode);
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void HistorySeverityProjection_LocalizesEveryStoredSeverityWithoutChangingItsValue()
    {
        var text = new LocalizationService();
        var occurred = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);
        foreach (var locale in Locales)
        {
            text.SetCulture(locale);
            foreach (var severity in Enum.GetValues<IncidentSeverity>())
            {
                var storedSeverity = severity.ToString();
                var incident = new StoredIncident("synthetic", occurred, "Graphics", storedSeverity, "synthetic-signature", "{}");
                var projection = HistoryIncidentPresentation.From(incident, text);
                Assert.EndsWith(" · " + text.Get("Severity" + severity), projection.TimestampSeverity, StringComparison.Ordinal);
                Assert.Equal(storedSeverity, incident.Severity);
            }
        }
    }

    private static MainWindow Open(MainViewModel viewModel, int width = 1280, int height = 900)
    {
        var window = new MainWindow(viewModel);
        VisualRenderGeometry.ShowAtRequestedGeometry(window, width, height);
        return window;
    }

    private static T Current<T>(MainWindow window) where T : Control
    {
        window.UpdateLayout();
        return Assert.IsType<T>(window.FindControl<ContentControl>("PageHost")!.Content);
    }

    private static void Resize(MainWindow window, int width)
    {
        window.Width = width;
        window.Height = 900;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.InRange(Math.Abs(window.Bounds.Width - width), 0, 0.1);
    }

    private static int ColumnsFor(double availableWidth) => availableWidth >= 920 ? 3 : availableWidth >= 680 ? 2 : 1;

    private static void AssertFilterRows(Grid grid, int columns)
    {
        var fields = FilterFieldNames.Select(name => grid.FindControl<StackPanel>(name)!).ToArray();
        Assert.Equal(0, Grid.GetRow(fields[0]));
        if (columns == 3)
        {
            Assert.Equal(Grid.GetRow(fields[0]), Grid.GetRow(fields[1]));
            Assert.Equal(Grid.GetRow(fields[0]), Grid.GetRow(fields[2]));
            Assert.Equal(1, Grid.GetRow(fields[3]));
            Assert.Equal(3, Grid.GetColumnSpan(fields[3]));
        }
        else if (columns == 2)
        {
            Assert.Equal(Grid.GetRow(fields[0]), Grid.GetRow(fields[1]));
            Assert.Equal(Grid.GetRow(fields[2]), Grid.GetRow(fields[3]));
            Assert.Equal(0, Grid.GetColumn(fields[2]));
            Assert.Equal(1, Grid.GetColumn(fields[3]));
        }
        else
        {
            Assert.Equal([0, 1, 2, 3], fields.Select(Grid.GetRow));
        }
    }

    private static double LeftOf(Control control, Control relativeTo) => control.TranslatePoint(new Point(0, 0), relativeTo)!.Value.X;

    private static void AssertNoHorizontalOverflow(Control root, string context)
    {
        foreach (var viewer in root.GetVisualDescendants().OfType<ScrollViewer>().Where(item => item.Name != "PART_ScrollViewer"))
            Assert.True(viewer.Viewport.Width == 0 || viewer.Extent.Width <= viewer.Viewport.Width + 2,
                $"Horizontal overflow {context}: {viewer.Name} extent={viewer.Extent.Width} viewport={viewer.Viewport.Width}.");
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var index = 0; index < 200 && !condition(); index++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The requested analysis did not complete.");
    }
}

#pragma warning restore CA2000
