using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Headless")]
public sealed class HeadlessTests
{
    [AvaloniaTheory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)]
    public void ExportFormatSelection_ProvidesPreviewForEveryAvailableFormat(int index)
    {
        var window = Open();
        try
        {
            window.ViewModel.SetResult(SyntheticResults.Create(3)); window.ViewModel.Navigate(AppPage.Export);
            Find<ComboBox>(window, "ExportFormat").SelectedIndex = index;
            Assert.Contains(index == 2 ? "Incidents" : "FaultWitness", window.ExportPreview, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-private", window.ExportPreview, StringComparison.Ordinal);
            Assert.NotNull(Find<Button>(window, "SaveExport"));
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(AppTheme.Light)][InlineData(AppTheme.Dark)]
    public void PrimaryPages_InBothThemesStayWithinCompactWindow(AppTheme theme)
    {
        var window = Open();
        try
        {
            window.Width = 1280; window.Height = 720;
            window.ViewModel.SetResult(SyntheticResults.Create(3)); window.ViewModel.Select(window.ViewModel.AllRows[0]);
            window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Theme = theme });
            foreach (var page in Enum.GetValues<AppPage>())
            {
                window.ViewModel.Navigate(page); window.UpdateLayout();
                foreach (var viewer in window.GetVisualDescendants().OfType<ScrollViewer>())
                    Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 2 || viewer.Viewport.Width == 0, $"{page}/{theme}: horizontal overflow");
            }
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void LargeExport_PreviewIsBoundedButExportScopeRemainsComplete()
    {
        var window = Open();
        try
        {
            window.ViewModel.SetResult(SyntheticResults.Create(1000)); window.ViewModel.Navigate(AppPage.Export);
            Assert.Equal(MainWindow.PreviewCharacterLimit, Find<TextBox>(window, "ExportPreview").Text!.Length);
            Assert.True(window.ExportPreview.Length > MainWindow.PreviewCharacterLimit);
            Assert.Equal(1000, window.ViewModel.Result.Incidents.Count);
            Assert.Contains("excerpt", VisibleText(window), StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void NoMatchingFilter_ShowsExplicitEmptyState()
    {
        var window = Open();
        try
        {
            window.ViewModel.SetResult(SyntheticResults.Create(3)); window.ViewModel.Navigate(AppPage.Incidents);
            Find<TextBox>(window, "IncidentSearch").Focus(); window.KeyTextInput("not-present");
            Assert.True(Find<Border>(window, "FilterEmpty").IsVisible);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task SourceFailure_OffersExpandableSafeTechnicalDetails()
    {
        var window = Open(new TestServices { FailAnalysis = true });
        try
        {
            await window.ViewModel.AnalyzeAsync();
            var details = Find<Expander>(window, "TechnicalError");
            Assert.True(details.IsVisible); Assert.False(details.IsExpanded);
            Assert.DoesNotContain("synthetic-private", VisibleText(window), StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }
    private static MainWindow Open(TestServices? services = null)
    {
#pragma warning disable CA2000 // MainWindow owns this view model and disposes it on Closed in every test's finally.
        var window = new MainWindow(new MainViewModel(services ?? new TestServices())); window.Show(); window.UpdateLayout(); return window;
#pragma warning restore CA2000
    }
    private static T Find<T>(MainWindow window, string name) where T : Control
    { window.UpdateLayout(); return window.GetVisualDescendants().OfType<T>().Single(item => item.Name == name); }
    private static string VisibleText(MainWindow window)
    { window.UpdateLayout(); return string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text)); }
    private static void Click(MainWindow window, string name) => Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void Startup_ShowsOverviewAndUsefulEmptyState()
    {
        var window = Open();
        try { Assert.Contains("No analysis yet", VisibleText(window), StringComparison.Ordinal); Assert.NotNull(Find<Button>(window, "PrimaryAnalyze")); }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(AppPage.Home)][InlineData(AppPage.Incidents)][InlineData(AppPage.Analyze)][InlineData(AppPage.History)]
    [InlineData(AppPage.Readiness)][InlineData(AppPage.System)][InlineData(AppPage.Settings)]
    public void Navigation_CreatesFunctionalPageWithoutBlankContent(AppPage page)
    {
        var window = Open();
        try { window.ViewModel.Navigate(page); window.UpdateLayout(); Assert.NotEmpty(VisibleText(window)); Assert.Equal(page, window.ViewModel.Page); }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void Overview_ThousandsOfContextItemsDoNotFillRecentList()
    {
        var window = Open();
        try
        {
            window.ViewModel.SetResult(SyntheticResults.Create(5000)); window.UpdateLayout();
            Assert.Equal(2, Find<ListBox>(window, "RecentSignificantList").ItemCount);
            Assert.Contains("4998", VisibleText(window), StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void AnalysisPeriod_SelectionUpdatesViewModel()
    {
        var window = Open();
        try { Click(window, "PrimaryAnalyze"); Find<ComboBox>(window, "PeriodSelector").SelectedIndex = 2; Assert.Equal(AnalysisPeriod.Month, window.ViewModel.Period); }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task AnalysisCancellation_ShowsProgressAndRemainsInteractive()
    {
        var window = Open(new TestServices { WaitForCancellation = true });
        try
        {
            var running = window.ViewModel.AnalyzeAsync(); Assert.True(Find<ProgressBar>(window, "AnalysisProgress").IsVisible);
            Click(window, "NavSettings"); Assert.Equal(AppPage.Settings, window.ViewModel.Page);
            Click(window, "CancelAnalysis"); await running.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(Find<ProgressBar>(window, "AnalysisProgress").IsVisible);
            Assert.Contains("cancelled", Find<TextBlock>(window, "StatusText").Text, StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(0, 1)][InlineData(2, 98)]
    public void PriorityFilter_ChangesVisibleItems(int index, int expected)
    {
        var window = Open();
        try
        {
            window.ViewModel.SetResult(SyntheticResults.Create(100)); window.ViewModel.Navigate(AppPage.Incidents);
            Find<ComboBox>(window, "PriorityFilter").SelectedIndex = index;
            Assert.Equal(expected, Find<ListBox>(window, "IncidentList").ItemCount);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void SearchBox_UpdatesResultsWithoutRecreatingFocusedControl()
    {
        var window = Open();
        try
        {
            window.ViewModel.SetResult(SyntheticResults.Create(100)); window.ViewModel.Navigate(AppPage.Incidents);
            var search = Find<TextBox>(window, "IncidentSearch"); search.Focus(); window.KeyTextInput("SearchTarget");
            Assert.Single(window.ViewModel.FilteredRows); Assert.Same(search, Find<TextBox>(window, "IncidentSearch"));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void IncidentDetail_RendersEvidenceTypesCoverageRecurrenceAndNoCauseContract()
    {
        var window = Open();
        try
        {
            window.ViewModel.SetResult(SyntheticResults.Create(3)); window.ViewModel.Select(window.ViewModel.AllRows.Single(row => row.Priority == AttentionLevel.Attention));
            window.UpdateLayout(); var text = VisibleText(window);
            Assert.Contains("Observed", text, StringComparison.Ordinal); Assert.Contains("Not observed", text, StringComparison.Ordinal); Assert.Contains("Unknown", text, StringComparison.Ordinal);
            Assert.Contains("Recurring", text, StringComparison.Ordinal); Assert.Contains("Best next step", text, StringComparison.Ordinal);
            Assert.NotNull(Find<StackPanel>(window, "CoveragePanel")); Assert.DoesNotContain("<Event>", text, StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task Import_RejectionIsVisibleWithoutExceptionDump()
    {
        var window = Open(new TestServices { RejectImport = true });
        try
        {
            window.ViewModel.OpenAnalyze(AnalysisMode.Files); window.ViewModel.AddImports(["broken.zip"]); await window.ViewModel.AnalyzeImportsAsync();
            window.UpdateLayout(); Assert.Contains("Rejected", VisibleText(window), StringComparison.Ordinal); Assert.DoesNotContain("StackTrace", VisibleText(window), StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task Readiness_UnavailableSourceIsExplicit()
    {
        var window = Open(new TestServices { SourceUnavailable = true });
        try
        {
            window.ViewModel.Navigate(AppPage.Readiness); await window.ViewModel.RefreshReadinessAsync(); window.UpdateLayout();
            Assert.Contains("Unavailable", VisibleText(window), StringComparison.Ordinal); Assert.Contains("Limited", VisibleText(window), StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task System_ShowsOnlyReturnedInventory()
    {
        var window = Open();
        try { window.ViewModel.Navigate(AppPage.System); await window.ViewModel.RefreshInventoryAsync(); Assert.Contains("Synthetic Windows", VisibleText(window), StringComparison.Ordinal); Assert.DoesNotContain("RTX", VisibleText(window), StringComparison.Ordinal); }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(1, "en")][InlineData(2, "it")][InlineData(3, "es")][InlineData(4, "fr")]
    [InlineData(5, "de")][InlineData(6, "pt")][InlineData(7, "ru")][InlineData(8, "pl")]
    public void Settings_LanguageSwitchRecreatesCurrentViewWithoutRestart(int index, string culture)
    {
        var window = Open();
        try
        {
            window.ViewModel.Navigate(AppPage.Settings); Find<ComboBox>(window, "LanguageSelector").SelectedIndex = index;
            window.UpdateLayout(); Assert.Equal(culture, window.ViewModel.Text.Culture.Name);
            Assert.Contains(window.ViewModel.Text.Get("General"), VisibleText(window), StringComparison.Ordinal);
            Assert.True(window.IsVisible);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(1)][InlineData(2)]
    public void Settings_ThemeSwitchUpdatesWindow(int index)
    {
        var window = Open();
        try { window.ViewModel.Navigate(AppPage.Settings); Find<ComboBox>(window, "ThemeSelector").SelectedIndex = index; Assert.Equal(index == 1 ? ThemeVariant.Light : ThemeVariant.Dark, window.RequestedThemeVariant); }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void ExportPage_ShowsRedactedPreviewAndExplicitDumpExclusion()
    {
        var window = Open();
        try
        {
            window.ViewModel.SetResult(SyntheticResults.Create(3)); window.ViewModel.Navigate(AppPage.Export); window.UpdateLayout();
            Assert.DoesNotContain("synthetic-private", window.ExportPreview, StringComparison.Ordinal);
            Assert.Contains("dump files are excluded", VisibleText(window), StringComparison.Ordinal);
            Assert.NotNull(Find<Button>(window, "CopySupport"));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task CopyForSupport_WritesSummaryAndConfirms()
    {
        var window = Open();
        try
        {
            window.ViewModel.SetResult(SyntheticResults.Create(1)); await window.CopySupportAsync();
            Assert.Equal("SummaryCopied", window.ViewModel.StatusKey);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(100)][InlineData(1000)][InlineData(5000)]
    public void IncidentList_VirtualizesLargeResults(int count)
    {
        var window = Open();
        try
        {
            window.ViewModel.SetResult(SyntheticResults.Create(count)); window.ViewModel.Navigate(AppPage.Incidents); window.UpdateLayout();
            var list = Find<ListBox>(window, "IncidentList"); Assert.Equal(count, list.ItemCount);
            Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 30);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(640, 800)][InlineData(800, 800)][InlineData(1000, 800)][InlineData(1280, 720)][InlineData(1280, 800)][InlineData(1920, 1080)][InlineData(2560, 1440)]
    public void MainLayouts_StayWithinWindowAtRequestedSizes(int width, int height)
    {
        var window = Open();
        try
        {
            window.Width = width; window.Height = height; window.ViewModel.SetResult(SyntheticResults.Create(100));
            foreach (var page in new[] { AppPage.Home, AppPage.Incidents, AppPage.Analyze, AppPage.Settings, AppPage.Readiness, AppPage.History })
            {
                window.ViewModel.Navigate(page); window.UpdateLayout();
                foreach (var viewer in window.GetVisualDescendants().OfType<ScrollViewer>())
                    Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 2 || viewer.Viewport.Width == 0, $"{page}: horizontal overflow at {width}x{height}");
            }
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void KeyboardAndAccessibility_PrimaryActionsAreNamedAndFocusable()
    {
        var window = Open();
        try
        {
            var start = Find<Button>(window, "PrimaryAnalyze"); Assert.Equal("Analyze the last 7 days", AutomationProperties.GetName(start));
            Assert.True(start.Focus()); window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t"); window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
            Assert.NotNull(window.FocusManager?.GetFocusedElement());
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task History_RendersPersistedSummaryAndDoesNotExposeRawXml()
    {
        var result = SyntheticResults.Create(2); var metadata = new FaultWitness.Storage.ScanHistoryMetadata("recent", result.StartedUtc, result.FinishedUtc, 950, 1, 1, 0, "SourceSystem=Complete");
        var stored = new FaultWitness.Storage.StoredScan("scan", result.StartedUtc, result.FinishedUtc, "0.9.1", metadata,
            [new("i", result.StartedUtc, "Graphics", "High", "sig", "{\"Findings\":[]}")]);
        var window = Open(new TestServices { History = [stored] });
        try { await window.ViewModel.RefreshHistoryAsync(); window.ViewModel.Navigate(AppPage.History); window.UpdateLayout(); var text = VisibleText(window); Assert.Contains("Previous local analyses", text, StringComparison.Ordinal); Assert.Contains("need attention", text, StringComparison.Ordinal); Assert.DoesNotContain("<Event>", text, StringComparison.Ordinal); Assert.NotNull(Find<Button>(window, "SaveHistory")); }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void Navigation_ContainsOnlyDestinationArchitectureAndSeparatedSettings()
    {
        var window = Open();
        try
        {
            string[] present = ["NavHome","NavAnalyze","NavIncidents","NavHistory","NavSystem","NavSettings"];
            Assert.All(present, name => Assert.NotNull(Find<Button>(window, name)));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.Name is "NavImport" or "NavAround" or "NavReadiness");
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void EachScreen_HasAtMostOneVisuallyPrimaryAction()
    {
        var window = Open();
        try
        {
            foreach (var page in new[] { AppPage.Home, AppPage.Analyze, AppPage.Incidents, AppPage.History, AppPage.System, AppPage.Readiness, AppPage.Settings, AppPage.Export })
            {
                window.ViewModel.Navigate(page); window.UpdateLayout();
                Assert.True(window.GetVisualDescendants().OfType<Button>().Count(item => item.Classes.Contains("primary-action")) <= 1, page.ToString());
            }
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")][InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public void EveryLanguage_RemainsUsableAtSmallWidth(string language)
    {
        var services = new TestServices { Settings = new UserSettings(Language: language) }; var window = Open(services); window.Width = 640; window.Height = 800;
        try
        {
            foreach (var page in new[] { AppPage.Home, AppPage.Analyze, AppPage.Settings })
            {
                window.ViewModel.Navigate(page); window.UpdateLayout(); Assert.NotEmpty(VisibleText(window));
                Assert.All(window.GetVisualDescendants().OfType<ScrollViewer>(), viewer => Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 2 || viewer.Viewport.Width == 0, $"{language}/{page} overflow"));
            }
        }
        finally { window.Close(); }
    }
}
