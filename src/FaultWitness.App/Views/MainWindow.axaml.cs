using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;

namespace FaultWitness.App;

public sealed partial class MainWindow : Window
{
    private Grid shell = new();
    private ContentControl pageHost = new();
    private TextBlock statusText = new();
    private Grid statusArea = new();
    private ProgressBar progress = new();
    private Button cancelButton = new();
    private Expander technicalDetails = new();
    private readonly DispatcherTimer activityTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private HomeView? homeView;
    private HomePresentation? homePresentation;
    private readonly Dictionary<AnalysisMode, (AnalyzeView View, AnalyzePresentation Presentation)> analyzePages = [];
    private IncidentsView? incidentsView;
    private IncidentListPresentation? incidentsPresentation;
    private HistoryView? historyView;
    private HistoryPresentation? historyPresentation;
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
        statusText.Text = ViewModel.StatusText;
        statusText.IsVisible = ViewModel.HasVisibleStatus;
        statusArea.IsVisible = ViewModel.IsBusy || ViewModel.HasVisibleStatus;
        progress.IsVisible = ViewModel.IsBusy;
        cancelButton.IsVisible = ViewModel.IsBusy;
        ToolTip.SetTip(statusText, string.IsNullOrEmpty(ViewModel.TechnicalError) ? null : ViewModel.TechnicalError);
        technicalDetails.IsVisible = ViewModel.HasVisibleStatus && !string.IsNullOrEmpty(ViewModel.TechnicalError);
        if (technicalDetails.Content is TextBlock detail) detail.Text = ViewModel.TechnicalError;
        foreach (var button in shell.GetVisualDescendants().OfType<Button>().Where(item => item.Classes.Contains("operation")))
            button.IsEnabled = !ViewModel.IsBusy;
        updateCaptureControls?.Invoke();
    }
    private void RenderPage(bool refreshPageData = true)
    {
        updateCaptureControls = null;
        pageHost.Content = ViewModel.Page switch
        {
            AppPage.Analyze => AnalyzePage(ViewModel.AnalysisMode, refreshPageData),
            AppPage.Incidents => IncidentsPage(refreshPageData),
            AppPage.History => HistoryPage(refreshPageData),
            AppPage.Detail => DetailPage(), AppPage.Readiness => BuildReadiness(), AppPage.System => BuildSystem(),
            AppPage.Settings => BuildSettings(), AppPage.Export => BuildExport(), _ => HomePage(refreshPageData)
        };
        foreach (var button in shell.GetVisualDescendants().OfType<Button>().Where(item => item.Name?.StartsWith("Nav", StringComparison.Ordinal) == true))
        {
            var destination = ViewModel.Page switch { AppPage.Detail => AppPage.Incidents, AppPage.Readiness => AppPage.System, _ => ViewModel.Page };
            SetNavigationSelected(button, button.Name == "Nav" + destination);
        }
        UpdateStatus();
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
                await ViewModel.RefreshHistoryAsync().ConfigureAwait(true);
                ViewModel.Navigate(AppPage.History);
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
        try { await action().ConfigureAwait(true); }
        catch (Exception exception) { ViewModel.Fail("OperationError", exception); }
    }
    private Control DetailPage()
    {
        if (ViewModel.Selected is not { } row) return Empty("SelectIncident");
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
    private StackPanel Section(string title, Control content)
    {
        var section = Stack(Label(T(title), TextRole.SectionTitle), content);
        section.Margin = new Thickness(0, D("primitive.space.2"), 0, 0);
        return section;
    }
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
    private enum TextRole { Caption, Body, RowTitle, SectionTitle, PageTitle }
    private static TextBlock Label(string text, TextRole role = TextRole.Body, bool bold = false) => new()
    { Text = text, FontSize = D(role switch { TextRole.Caption => "primitive.fontSize.caption", TextRole.RowTitle => "primitive.fontSize.row", TextRole.SectionTitle => "primitive.fontSize.section", TextRole.PageTitle => "primitive.fontSize.page", _ => "primitive.fontSize.body" }),
        FontWeight = bold || role is TextRole.RowTitle or TextRole.SectionTitle or TextRole.PageTitle ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Muted(string text, TextRole role = TextRole.Caption)
    {
        var label = Label(text, role);
        label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("AppMuted"));
        return label;
    }
    private static StackPanel Stack(params Control[] controls)
    {
        var panel = new StackPanel { Spacing = D("primitive.space.3") };
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }
    private static WrapPanel Actions(params Control[] controls)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls) { control.Margin = new Thickness(0, 0, D("primitive.space.3"), D("primitive.space.2")); panel.Children.Add(control); }
        return panel;
    }
    private static Border Surface(Control child, double? padding = null)
    {
        var border = new Border { Child = child, Padding = new Thickness(padding ?? D("primitive.space.4")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(D("primitive.radius.small")) };
        border.Bind(Border.BackgroundProperty, new DynamicResourceExtension("AppSurface"));
        border.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("AppBorder"));
        return border;
    }
    private Button Button(string key, Action action, string? name = null)
    {
        var button = new Button { Content = new TextBlock { Text = T(key), TextWrapping = TextWrapping.Wrap }, Name = name, Padding = new Thickness(D("primitive.space.3"), D("primitive.space.2")), MinHeight = D("component.action.standard.minHeight") };
        button.Classes.Add("secondary-action");
        AutomationProperties.SetName(button, T(key)); button.Click += (_, _) => action();
        return button;
    }
    // Retained for the System page tabs until their Pass 2B migration. The shell is AXAML.
    private Button NavigationButton(string key, Action action, string name, bool selected = false)
    {
        var button = Button(key, action, name);
        button.Classes.Clear(); button.Classes.Add("navigation-item");
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.MinHeight = D("component.navigation.itemHeight");
        button.Padding = new Thickness(D("primitive.space.2"));
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = D("primitive.space.2") };
        var indicator = new Border { Width = D("component.selection.indicatorWidth"), CornerRadius = new CornerRadius(D("primitive.radius.small")), Margin = new Thickness(0, D("primitive.space.1")) };
        indicator.Bind(Border.BackgroundProperty, new DynamicResourceExtension("AppAccent"));
        content.Children.Add(indicator);
        var label = Label(T(key)); Grid.SetColumn(label, 1); content.Children.Add(label);
        button.Content = content;
        SetNavigationSelected(button, selected);
        return button;
    }
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
    private Button PrimaryButton(string key, Action action, string? name = null) { var button = Button(key, action, name); button.Classes.Remove("secondary-action"); button.Classes.Add("primary-action"); button.MinHeight = D("component.action.primary.minHeight"); return button; }
    private Button SubtleButton(string key, Action action, string? name = null) { var button = Button(key, action, name); button.Classes.Remove("secondary-action"); button.Classes.Add("subtle-action"); return button; }
    private Button DangerButton(string key, Action action, string? name = null) { var button = Button(key, action, name); button.Classes.Remove("secondary-action"); button.Classes.Add("danger-action"); return button; }
    private Button AsyncButton(string key, Func<Task> action, string? name = null)
    {
        var button = Button(key, () => { }, name);
        button.Classes.Add("operation");
        button.IsEnabled = !ViewModel.IsBusy;
        button.Click += async (_, _) =>
        {
            try { await action().ConfigureAwait(true); }
            catch (Exception exception) { ViewModel.Fail("OperationError", exception); }
        };
        return button;
    }
    private Button PrimaryAsyncButton(string key, Func<Task> action, string? name = null) { var button = AsyncButton(key, action, name); button.Classes.Remove("secondary-action"); button.Classes.Add("primary-action"); button.MinHeight = D("component.action.primary.minHeight"); return button; }
    private static ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private StackPanel Heading(string key, string? subtitle = null) => Stack(Label(T(key), TextRole.PageTitle), Muted(T(subtitle ?? "Tagline"), TextRole.Body));
    private Expander Expand(string key, Control content, bool open = false) => new()
    { Header = T(key), Content = content, IsExpanded = open, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private StackPanel Field(string key, Control control)
    {
        AutomationProperties.SetName(control, T(key));
        return Stack(Muted(T(key)), control);
    }
    private Border Empty(string key = "NoSignificant", string? helpKey = null) => Surface(Stack(Label(T(key), TextRole.RowTitle), Muted(T(helpKey ?? (ViewModel.HasAnalysis ? "CheckCoverage" : "NoHistory")), TextRole.Body)));
    private StackPanel Coverage(IEnumerable<SourceCoverage> coverage)
    {
        var panel = new StackPanel { Spacing = D("primitive.space.2"), Name = "CoveragePanel" };
        foreach (var source in coverage)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(Label(T(PresentationPolicy.SourceKey(source))));
            var state = Label(T("Coverage" + source.State), bold: true); Grid.SetColumn(state, 1); row.Children.Add(state);
            var details = Stack(Label(T("CoverageHelp" + source.State)), Muted(source.ExaminedFromUtc is null ? T("IntervalUnknown") :
                ViewModel.Text.Format("IntervalValue", source.ExaminedFromUtc.Value.ToLocalTime().ToString("g", ViewModel.Text.Culture), source.ExaminedToUtc?.ToLocalTime().ToString("g", ViewModel.Text.Culture) ?? T("NotAvailable"))));
            var expander = new Expander { Header = row, Content = details, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(expander, T(PresentationPolicy.SourceKey(source)) + " — " + T("Coverage" + source.State));
            ToolTip.SetTip(expander, T("CoverageHelp" + source.State)); panel.Children.Add(expander);
        }
        if (panel.Children.Count == 0) panel.Children.Add(Muted(T("CoverageNotChecked")));
        return panel;
    }
}
