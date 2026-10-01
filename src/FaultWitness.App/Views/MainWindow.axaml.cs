using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;

namespace FaultWitness.App;

public sealed partial class MainWindow : Window
{
    private readonly Grid shell;
    private readonly ContentControl pageHost;
    private readonly TextBlock statusText;
    private readonly Grid statusArea;
    private readonly ProgressBar progress;
    private readonly Button cancelButton;
    private readonly Expander technicalDetails;
    private readonly DispatcherTimer activityTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private HomeView? homeView;
    private HomePresentation? homePresentation;
    private readonly Dictionary<AnalysisMode, (AnalyzeView View, AnalyzePresentation Presentation)> analyzePages = [];
    private IncidentsView? incidentsView;
    private IncidentListPresentation? incidentsPresentation;
    private HistoryView? historyView;
    private HistoryPresentation? historyPresentation;
    private SystemView? systemView;
    private SystemPresentation? systemPresentation;
    private DiagnosticReadinessView? readinessView;
    private ReadinessPresentation? readinessPresentation;
    private SettingsView? settingsView;
    private SettingsPresentation? settingsPresentation;
    private string layoutClass = "large";
    private bool rebuildingShell;
    private IncidentDetailView? detailView;
    private IncidentDetailPresentation? detailPresentation;
    private ScanResult? detailResult;
    private string? detailCulture;
    private double normalWidth;
    private double normalHeight;
    private int normalSizeRevision;
    public MainViewModel ViewModel { get; }
    public long ResponsiveTicks { get; private set; }
    public MainWindow() : this(new MainViewModel(Avalonia.Controls.Design.IsDesignMode ? new IncidentDetailDesignServices() : new DesktopServices())) { }
    public MainWindow(MainViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        shell = this.FindControl<Grid>("Shell")!;
        pageHost = this.FindControl<ContentControl>("PageHost")!;
        statusText = this.FindControl<TextBlock>("StatusText")!;
        statusArea = this.FindControl<Grid>("StatusArea")!;
        progress = this.FindControl<ProgressBar>("AnalysisProgress")!;
        cancelButton = this.FindControl<Button>("CancelAnalysis")!;
        technicalDetails = this.FindControl<Expander>("TechnicalError")!;
        ViewModel = viewModel;
        DataContext = viewModel;
        Title = "FaultWitness";
        var settings = ViewModel.Settings;
        Width = settings.WindowWidth; Height = settings.WindowHeight; MinWidth = WindowLifecyclePolicy.MinimumWidth; MinHeight = WindowLifecyclePolicy.MinimumHeight;
        normalWidth = Width; normalHeight = Height;
        var restoreMaximized = settings.WindowState == AppWindowState.Maximized;
        FontSize = D("primitive.fontSize.body");
        BuildShell();
        ViewModel.Changed += OnChanged;
        activityTimer.Tick += (_, _) => { if (ViewModel.IsBusy) ResponsiveTicks++; };
        Opened += (_, _) =>
        {
            activityTimer.Start(); RecoverWindowGeometry();
            if (restoreMaximized) Dispatcher.UIThread.Post(() => WindowState = WindowState.Maximized);
        };
        SizeChanged += (_, args) =>
        {
            var settledSize = args.NewSize;
            var revision = ++normalSizeRevision;
            // Avalonia can raise the maximized size before WindowState changes. Defer
            // normal-bounds tracking until the native transition has settled.
            DispatcherTimer.RunOnce(() =>
            {
                if (revision == normalSizeRevision && WindowState == WindowState.Normal)
                { normalWidth = settledSize.Width; normalHeight = settledSize.Height; }
            }, TimeSpan.FromMilliseconds(300));
            if (rebuildingShell || args.NewSize.Width < MinWidth) return;
            var next = LayoutFor(args.NewSize.Width); if (next != layoutClass) { layoutClass = next; BuildShell(refreshPageData: false); }
        };
        Closed += (_, _) => { activityTimer.Stop(); ViewModel.Changed -= OnChanged; SaveWindowSettings(); ViewModel.Dispose(); };
    }
    private string T(string key) => ViewModel.Text.Get(key);
    private void OnChanged(ViewChange change)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => OnChanged(change)); return; }
        if (change == ViewChange.Language) { PreserveActiveAnalyzePeriod(); BuildShell(); }
        else if (change == ViewChange.Theme) { PreserveActiveAnalyzePeriod(); ApplyTheme(); RenderPage(); }
        else if (change is ViewChange.Page or ViewChange.Results || change == ViewChange.State && (ViewModel.Page is AppPage.System or AppPage.Readiness)) RenderPage();
        else if (change == ViewChange.HistorySelection && historyView is not null && historyPresentation is not null)
            historyView.RefreshSelection(historyPresentation, ViewModel);
        else if (change == ViewChange.Filter && incidentsView is not null && incidentsPresentation is not null)
            incidentsView.Refresh(incidentsPresentation);
        UpdateStatus();
    }
    private void ApplyTheme() => RequestedThemeVariant = ViewModel.Settings.Theme switch
    { AppTheme.Light => ThemeVariant.Light, AppTheme.Dark => ThemeVariant.Dark, _ => ThemeVariant.Default };
    private void BuildShell(bool refreshPageData = true)
    {
        rebuildingShell = true;
        try
        {
        ApplyTheme();
        var compact = layoutClass == "small";
        shell.ColumnDefinitions[0].Width = compact ? GridLength.Auto : new GridLength(D("component.navigation.width"));
        var side = this.FindControl<Border>("NavigationSurface")!;
        side.MinWidth = compact ? D("component.navigation.compactMinWidth") : 0;
        side.MaxWidth = compact ? D("component.navigation.width") : double.PositiveInfinity;
        var gutter = D(compact ? "component.layout.gutterSmall" : "component.layout.gutterNormal");
        this.FindControl<Grid>("MainRegion")!.Margin = new Thickness(gutter, D("primitive.space.6"), gutter, D("primitive.space.4"));
        pageHost.MaxWidth = layoutClass == "large" ? D("component.layout.readingWidth") : double.PositiveInfinity;
        statusArea.MaxWidth = pageHost.MaxWidth;
        this.FindControl<TextBlock>("LocalFirstLabel")!.Text = T("LocalFirst");
        foreach (var key in new[] { "Home", "Analyze", "Incidents", "History", "System", "Settings" })
        {
            this.FindControl<TextBlock>("Nav" + key + "Label")!.Text = T(key);
            AutomationProperties.SetName(this.FindControl<Button>("Nav" + key)!, T(key));
        }
        this.FindControl<TextBlock>("CancelLabel")!.Text = T("Cancel");
        AutomationProperties.SetName(cancelButton, T("Cancel"));
        AutomationProperties.SetName(progress, T("AnalysisInProgress"));
        technicalDetails.Header = T("TechnicalDetails");
        RenderPage(refreshPageData); UpdateStatus();
        }
        finally { rebuildingShell = false; }
    }
    private void UpdateStatus()
    {
        var local = ViewModel.HasLocalFeedback;
        // One visible live owner; the shell resumes ownership away from the origin.
        AutomationProperties.SetLiveSetting(statusText, local ? AutomationLiveSetting.Off : AutomationLiveSetting.Polite);
        statusText.Text = ViewModel.StatusText;
        statusText.IsVisible = ViewModel.HasVisibleStatus && !local;
        statusArea.IsVisible = (ViewModel.IsBusy || ViewModel.HasVisibleStatus) && !local;
        progress.IsVisible = ViewModel.IsBusy && !local;
        cancelButton.IsVisible = ViewModel.IsBusy && !local;
        ToolTip.SetTip(statusText, string.IsNullOrEmpty(ViewModel.TechnicalError) ? null : ViewModel.TechnicalError);
        technicalDetails.IsVisible = ViewModel.HasVisibleStatus && !local && !string.IsNullOrEmpty(ViewModel.TechnicalError);
        if (technicalDetails.Content is TextBlock detail) detail.Text = ViewModel.TechnicalError;
        if (pageHost.Content is AnalyzeView analyze) CommandFeedback.Refresh(analyze, "Analyze", ViewModel);
        if (pageHost.Content is ExportSupportView export) CommandFeedback.Refresh(export, "Export", ViewModel);
        if (pageHost.Content is HistoryView history)
        {
            historyPresentation?.RefreshFeedback();
            CommandFeedback.Refresh(history, "History", ViewModel);
            history.FindControl<Button>("RetryHistory")!.IsVisible = local && ViewModel.StatusKey == "HistoryError";
        }
        // A newly attached cached page may not have its visual children yet.
        var pageButtons = (pageHost.Content as Control)?.GetLogicalDescendants().OfType<Button>() ?? [];
        foreach (var button in shell.GetVisualDescendants().OfType<Button>().Concat(pageButtons).Distinct().Where(item => item.Classes.Contains("operation")))
            button.IsEnabled = !ViewModel.IsBusy;
        RefreshCapture();
    }
    private void RenderPage(bool refreshPageData = true)
    {
        pageHost.Content = ViewModel.Page switch
        {
            AppPage.Analyze => AnalyzePage(ViewModel.AnalysisMode, refreshPageData),
            AppPage.Incidents => IncidentsPage(refreshPageData),
            AppPage.History => HistoryPage(refreshPageData),
            AppPage.Detail => DetailPage(), AppPage.Readiness => ReadinessPage(), AppPage.System => SystemPage(),
            AppPage.Settings => SettingsPage(), AppPage.Export => ExportSupportPage(), _ => HomePage(refreshPageData)
        };
        foreach (var button in shell.GetVisualDescendants().OfType<Button>().Where(item => item.Name?.StartsWith("Nav", StringComparison.Ordinal) == true))
        {
            var destination = ViewModel.Page switch { AppPage.Detail => AppPage.Incidents, AppPage.Readiness => AppPage.System, _ => ViewModel.Page };
            SetNavigationSelected(button, button.Name == "Nav" + destination);
        }
        UpdateStatus();
    }

    private SystemView SystemPage()
    {
        if (systemView is null)
        {
            systemPresentation = new SystemPresentation();
            systemView = new SystemView();
            systemView.RefreshRequested += async () => await RunGuardedAsync(ViewModel.RefreshInventoryAsync).ConfigureAwait(true);
            systemView.InformationRequested += () => ViewModel.Navigate(AppPage.System);
            systemView.ReadinessRequested += () => ViewModel.Navigate(AppPage.Readiness);
        }
        systemView.Refresh(systemPresentation!, ViewModel, layoutClass, CapturePage());
        return systemView;
    }

    private DiagnosticReadinessView ReadinessPage()
    {
        if (readinessView is null)
        {
            readinessPresentation = new ReadinessPresentation();
            readinessView = new DiagnosticReadinessView();
            readinessView.RefreshRequested += async () => await RunGuardedAsync(ViewModel.RefreshReadinessAsync).ConfigureAwait(true);
            readinessView.InformationRequested += () => ViewModel.Navigate(AppPage.System);
            readinessView.ReadinessRequested += () => ViewModel.Navigate(AppPage.Readiness);
        }
        readinessView.Refresh(readinessPresentation!, ViewModel);
        return readinessView;
    }

    private SettingsView SettingsPage()
    {
        if (settingsView is null)
        {
            settingsPresentation = new SettingsPresentation();
            settingsView = new SettingsView();
            settingsView.SettingChanged += ApplySetting;
            settingsView.ClearDataRequested += async () => await RunGuardedAsync(ConfirmClearAsync).ConfigureAwait(true);
        }
        settingsPresentation!.Refresh(ViewModel);
        settingsView.Refresh(settingsPresentation);
        return settingsView;
    }

    private void ApplySetting(string setting, int index)
    {
        var presentation = settingsPresentation;
        if (presentation is null) return;
        var current = ViewModel.Settings;
        var updated = setting switch
        {
            "Language" when index >= 0 && index < presentation.Languages.Count => current with { Language = SettingsPresentation.LanguageCodeAt(index) },
            "Theme" when index >= 0 && index < presentation.Themes.Count => current with { Theme = SettingsPresentation.ThemeAt(index) },
            "Period" when index >= 0 && index < presentation.Periods.Count => current with { Period = SettingsPresentation.PeriodAt(index) },
            "Retention" when index >= 0 && index < presentation.RetentionOptions.Count => current with { RetentionDays = SettingsPresentation.RetentionDaysAt(index) },
            _ => current
        };
        if (updated != current) ViewModel.ChangeSettings(updated);
    }

    private HomeView HomePage(bool refresh)
    {
        if (homeView is null)
        {
            homePresentation = new HomePresentation(ViewModel);
            homeView = new HomeView();
            homeView.RecentAnalysisRequested += () => ViewModel.OpenAnalyze(AnalysisMode.Recent);
            homeView.AnalysisModeRequested += ViewModel.OpenAnalyze;
            homeView.PriorityRequested += priority => ViewModel.ShowPriority(priority);
            homeView.ViewAllRequested += () => ViewModel.ShowPriority(null);
            homeView.HistoryRequested += async () =>
            {
                ViewModel.Navigate(AppPage.History);
                await ViewModel.RefreshHistoryAsync().ConfigureAwait(true);
            };
            homeView.ExportRequested += () => ViewModel.Navigate(AppPage.Export);
            homeView.IncidentRequested += ViewModel.Select;
        }
        if (refresh) homeView.Refresh(homePresentation!, layoutClass);
        else homeView.SetLayout(layoutClass);
        return homeView;
    }

    private AnalyzeView AnalyzePage(AnalysisMode mode, bool refresh)
    {
        if (!analyzePages.TryGetValue(mode, out var pair))
        {
            var presentation = new AnalyzePresentation(ViewModel);
            var view = new AnalyzeView();
            view.BrowseRequested += async () => await RunGuardedAsync(BrowseImportsAsync).ConfigureAwait(true);
            view.ImportAnalysisRequested += async () => await RunGuardedAsync(ViewModel.AnalyzeImportsAsync).ConfigureAwait(true);
            view.ImportsDropped += ViewModel.AddImports;
            view.RunRequested += request => _ = RunAnalysisAsync(request);
            view.CancelRequested += ViewModel.Cancel;
            pair = (view, presentation);
            analyzePages.Add(mode, pair);
        }
        if (refresh)
        {
            pair.Presentation.SyncPeriodFromViewModel();
            pair.View.Refresh(pair.Presentation);
        }
        return pair.View;
    }

    private IncidentsView IncidentsPage(bool refresh)
    {
        if (incidentsView is null)
        {
            incidentsPresentation = new IncidentListPresentation(ViewModel);
            incidentsView = new IncidentsView();
            incidentsView.FilterRequested += ViewModel.SetFilter;
            incidentsView.ResetRequested += () => ViewModel.SetFilter(new());
            incidentsView.IncidentRequested += ViewModel.Select;
        }
        if (refresh) incidentsView.Refresh(incidentsPresentation!);
        return incidentsView;
    }

    private HistoryView HistoryPage(bool refresh)
    {
        if (historyView is null)
        {
            historyPresentation = new HistoryPresentation(ViewModel);
            historyView = new HistoryView();
            historyView.RefreshRequested += async () => await RunGuardedAsync(ViewModel.RefreshHistoryAsync).ConfigureAwait(true);
            historyView.CopyRequested += async () => await RunGuardedAsync(CopyHistoryAsync).ConfigureAwait(true);
            historyView.SaveRequested += async () => await RunGuardedAsync(SaveHistoryAsync).ConfigureAwait(true);
            historyView.SelectionRequested += ViewModel.SelectHistory;
        }
        historyView.Refresh(historyPresentation!, ViewModel, refreshItems: refresh, layoutClass: layoutClass);
        return historyView;
    }

    private async Task RunAnalysisAsync(AnalyzeRunRequest request)
    {
        await RunGuardedAsync(async () =>
        {
            ViewModel.CustomFrom = DateTimeInput.Combine(request.From, TimeSpan.Zero);
            ViewModel.CustomTo = DateTimeInput.Combine(request.To, new TimeSpan(23, 59, 59));
            if (request.Around)
            {
                ViewModel.AroundTime = DateTimeInput.Combine(request.AroundDate, request.AroundTime);
                ViewModel.WindowMinutes = request.WindowMinutes;
            }
            await ViewModel.AnalyzeAsync(request.Around).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task RunGuardedAsync(Func<Task> action)
    {
        var origin = ViewModel.Page;
        var revision = ViewModel.FeedbackRevision;
        try
        {
            var pending = action();
            revision = ViewModel.FeedbackRevision;
            await pending.ConfigureAwait(true);
        }
        catch (Exception exception) { ViewModel.Fail("OperationError", exception, origin, revision); }
    }
    private Control DetailPage()
    {
        if (ViewModel.Selected is not { } row) return new IncidentUnavailableView { DataContext = new IncidentUnavailablePresentation(new(ViewModel.Text), T(ViewModel.HasAnalysis ? "CheckCoverage" : "NoHistory")) };
        if (detailPresentation?.Incident.Id != row.Incident.Id)
        {
            detailResult = null;
            detailPresentation = new IncidentDetailPresentation();
            detailView = new IncidentDetailView { DataContext = detailPresentation };
            detailView.BackRequested += (_, _) => ViewModel.Navigate(AppPage.Incidents);
            detailView.SupportRequested += (_, _) => { exportSelectedOnly = true; ViewModel.Navigate(AppPage.Export); };
            detailView.OccurrencesRequested += (_, _) => ViewModel.ViewOccurrences();
        }
        if (!ReferenceEquals(detailResult, ViewModel.Result) || detailCulture != ViewModel.Text.Culture.Name || !ReferenceEquals(detailPresentation.Incident, row.Incident))
        {
            detailPresentation.Refresh(row, ViewModel.Result, ViewModel.Text, ViewModel.Origin);
            detailResult = ViewModel.Result;
            detailCulture = ViewModel.Text.Culture.Name;
        }
        return detailView!;
    }

    private void PreserveActiveAnalyzePeriod()
    {
        if (ViewModel.Page == AppPage.Analyze && analyzePages.TryGetValue(ViewModel.AnalysisMode, out var pair))
            ViewModel.Period = pair.Presentation.SelectedPeriod;
    }
    private async void NavigateFromShell(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Name: { } name } || !Enum.TryParse<AppPage>(name[3..], out var page)) return;
        ViewModel.Navigate(page);
        if (page == AppPage.History) await ViewModel.RefreshHistoryAsync();
    }
    private void CancelFromShell(object? sender, RoutedEventArgs args) => ViewModel.Cancel();
    private static string LayoutFor(double width) => width <= 640 ? "small" : width <= 1007 ? "medium" : "large";
    private void RecoverWindowGeometry()
    {
        var area = Screens.Primary?.WorkingArea;
        if (area is null) return;
        var (width, height) = WindowLifecyclePolicy.ClampSize(normalWidth, normalHeight, area.Value.Width, area.Value.Height);
        normalWidth = width; normalHeight = height;
        Width = width; Height = height;
        var placement = WindowLifecyclePolicy.SafePlacement(Position.X, Position.Y, width, height, area.Value.X, area.Value.Y, area.Value.Width, area.Value.Height);
        Position = new PixelPoint((int)Math.Round(placement.X), (int)Math.Round(placement.Y));
    }
    private void SaveWindowSettings()
    {
        var state = WindowState == WindowState.Maximized ? AppWindowState.Maximized : AppWindowState.Normal;
        var size = WindowLifecyclePolicy.ClampSize(normalWidth, normalHeight, double.MaxValue, double.MaxValue);
        try { ViewModel.ChangeSettings(ViewModel.Settings with { WindowWidth = size.Width, WindowHeight = size.Height, WindowState = state }); } catch { /* shutdown persistence must not prevent closing */ }
    }
    private static double D(string key) => Convert.ToDouble(Avalonia.Application.Current?.Resources[key] ?? 0, System.Globalization.CultureInfo.InvariantCulture);
    private void SetNavigationSelected(Button button, bool selected)
    {
        button.Classes.Set("selected", selected);
        AutomationProperties.SetHelpText(button, selected ? T("NavigationCurrent") : string.Empty);
        if (button.Content is Grid content)
        {
            content.Children[0].Opacity = selected ? 1 : 0;
            ((TextBlock)content.Children[1]).FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }
}
