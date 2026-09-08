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
    private ListBox? incidentList;
    private TextBlock? filterCount;
    private Border? filterEmpty;
    private Expander technicalDetails = new();
    private ContentControl? historyDetailHost;
    private ListBox? historyList;
    private readonly DispatcherTimer activityTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string layoutClass = "large";
    private bool rebuildingShell;
    public MainViewModel ViewModel { get; }
    public long ResponsiveTicks { get; private set; }
    public MainWindow() : this(new MainViewModel(new DesktopServices())) { }
    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        Title = "FaultWitness";
        Width = 1280; Height = 800; MinWidth = 560; MinHeight = 600;
        FontSize = D("primitive.fontSize.body");
        BuildShell();
        ViewModel.Changed += OnChanged;
        activityTimer.Tick += (_, _) => { if (ViewModel.IsBusy) ResponsiveTicks++; };
        Opened += (_, _) => activityTimer.Start();
        SizeChanged += (_, args) => { if (rebuildingShell || args.NewSize.Width < MinWidth) return; var next = LayoutFor(args.NewSize.Width); if (next != layoutClass) { layoutClass = next; BuildShell(); } };
        Closed += (_, _) => { activityTimer.Stop(); ViewModel.Changed -= OnChanged; ViewModel.Dispose(); };
    }
    private string T(string key) => ViewModel.Text.Get(key);
    private void OnChanged(ViewChange change)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => OnChanged(change)); return; }
        if (change == ViewChange.Language) BuildShell();
        else if (change == ViewChange.Theme) { ApplyTheme(); RenderPage(); }
        else if (change is ViewChange.Page or ViewChange.Results) RenderPage();
        else if (change == ViewChange.HistorySelection) RenderHistoryDetail();
        else if (change == ViewChange.Filter && incidentList is not null)
        { incidentList.ItemsSource = ViewModel.FilteredRows; if (filterCount is not null) filterCount.Text = ViewModel.Text.Format("ItemsShown", ViewModel.FilteredRows.Count, ViewModel.AllRows.Count); if (filterEmpty is not null) filterEmpty.IsVisible = ViewModel.FilteredRows.Count == 0; }
        UpdateStatus();
    }
    private void ApplyTheme() => RequestedThemeVariant = ViewModel.Settings.Theme switch
    { AppTheme.Light => ThemeVariant.Light, AppTheme.Dark => ThemeVariant.Dark, _ => ThemeVariant.Default };
    private void BuildShell()
    {
        rebuildingShell = true;
        try
        {
        ApplyTheme();
        var compact = layoutClass == "small";
        shell = new Grid { ColumnDefinitions = new ColumnDefinitions(compact ? "Auto,*" : FormattableString.Invariant($"{D("component.navigation.width")},*")) };
        var nav = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(D("primitive.space.3"), D("primitive.space.6"), D("primitive.space.3"), D("primitive.space.4")) };
        var brandText = Label("FaultWitness", TextRole.SectionTitle);
        brandText.Name = "NavigationBrand";
        brandText.TextWrapping = TextWrapping.NoWrap;
        brandText.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(brandText, "FaultWitness");
        var brand = Stack(brandText, Muted(T("LocalFirst")));
        nav.Children.Add(brand);
        var destinations = new StackPanel { Spacing = D("primitive.space.1"), Margin = new Thickness(0, D("primitive.space.8"), 0, 0) };
        Grid.SetRow(destinations, 1);
        foreach (var (key, page) in new[] { ("Home", AppPage.Home), ("Analyze", AppPage.Analyze), ("Incidents", AppPage.Incidents), ("History", AppPage.History), ("System", AppPage.System) })
        {
            var button = NavigationButton(key, page == AppPage.History ? async () => { ViewModel.Navigate(page); await ViewModel.RefreshHistoryAsync(); } : () => ViewModel.Navigate(page), "Nav" + page);
            destinations.Children.Add(button);
        }
        nav.Children.Add(destinations);
        var settings = NavigationButton("Settings", () => ViewModel.Navigate(AppPage.Settings), "NavSettings");
        Grid.SetRow(settings, 2); nav.Children.Add(settings);
        var side = Surface(nav, 0); side.BorderThickness = new Thickness(0, 0, 1, 0);
        if (compact) { side.MinWidth = D("component.navigation.compactMinWidth"); side.MaxWidth = D("component.navigation.width"); }
        shell.Children.Add(side);
        var gutter = D(layoutClass == "small" ? "component.layout.gutterSmall" : "component.layout.gutterNormal");
        var main = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(gutter, D("primitive.space.6"), gutter, D("primitive.space.4")) };
        Grid.SetColumn(main, 1);
        pageHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            MaxWidth = layoutClass == "large" ? D("component.layout.readingWidth") : double.PositiveInfinity, HorizontalAlignment = HorizontalAlignment.Stretch };
        main.Children.Add(pageHost);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), Margin = new Thickness(0, D("primitive.space.4"), 0, 0) };
        statusArea = bottom;
        statusText = Label(string.Empty); statusText.Name = "StatusText";
        AutomationProperties.SetLiveSetting(statusText, AutomationLiveSetting.Polite);
        bottom.Children.Add(statusText);
        cancelButton = Button("Cancel", ViewModel.Cancel, "CancelAnalysis"); Grid.SetColumn(cancelButton, 1); bottom.Children.Add(cancelButton);
        progress = new ProgressBar { IsIndeterminate = true, Height = 3, Margin = new Thickness(0, D("primitive.space.2"), 0, 0), Name = "AnalysisProgress" };
        AutomationProperties.SetName(progress, T("AnalysisInProgress"));
        Grid.SetRow(progress, 1); Grid.SetColumnSpan(progress, 2); bottom.Children.Add(progress);
        technicalDetails = Expand("TechnicalDetails", Label(string.Empty));
        technicalDetails.Name = "TechnicalError";
        Grid.SetRow(technicalDetails, 2); Grid.SetColumnSpan(technicalDetails, 2); bottom.Children.Add(technicalDetails);
        Grid.SetRow(bottom, 1); main.Children.Add(bottom);
        shell.Children.Add(main); Content = shell;
        RenderPage(); UpdateStatus();
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
    }
    private void RenderPage()
    {
        historyDetailHost = null;
        historyList = null;
        incidentList = null; filterCount = null; filterEmpty = null;
        pageHost.Content = ViewModel.Page switch
        {
            AppPage.Analyze => BuildAnalysis(ViewModel.AnalysisMode), AppPage.Incidents => BuildIncidents(), AppPage.History => BuildHistory(),
            AppPage.Detail => BuildDetail(), AppPage.Readiness => BuildReadiness(), AppPage.System => BuildSystem(),
            AppPage.Settings => BuildSettings(), AppPage.Export => BuildExport(), _ => BuildOverview()
        };
        foreach (var button in shell.GetVisualDescendants().OfType<Button>().Where(item => item.Name?.StartsWith("Nav", StringComparison.Ordinal) == true))
        {
            var destination = ViewModel.Page switch { AppPage.Detail => AppPage.Incidents, AppPage.Readiness => AppPage.System, _ => ViewModel.Page };
            SetNavigationSelected(button, button.Name == "Nav" + destination);
        }
        UpdateStatus();
    }
    private void RenderHistoryDetail()
    {
        if (ViewModel.Page == AppPage.History && historyDetailHost is not null)
        {
            if (historyList is not null) historyList.SelectedItem = ViewModel.SelectedHistory;
            historyDetailHost.Content = HistoryDetail();
        }
    }
    private static string LayoutFor(double width) => width <= 640 ? "small" : width <= 1007 ? "medium" : "large";
    private static double D(string key) => Convert.ToDouble(Avalonia.Application.Current?.Resources[key] ?? 0, System.Globalization.CultureInfo.InvariantCulture);
    private enum TextRole { Caption, Body, RowTitle, SectionTitle, PageTitle }
    private static TextBlock Label(string text, TextRole role = TextRole.Body, bool bold = false) => new()
    { Text = text, FontSize = D(role switch { TextRole.Caption => "primitive.fontSize.caption", TextRole.RowTitle => "primitive.fontSize.row", TextRole.SectionTitle => "primitive.fontSize.section", TextRole.PageTitle => "primitive.fontSize.page", _ => "primitive.fontSize.body" }),
        FontWeight = bold || role is TextRole.RowTitle or TextRole.SectionTitle or TextRole.PageTitle ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Muted(string text)
    {
        var label = Label(text, TextRole.Caption);
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
        var button = new Button { Content = T(key), Name = name, Padding = new Thickness(D("primitive.space.3"), D("primitive.space.2")), MinHeight = D("component.action.standard.minHeight") };
        button.Classes.Add("secondary-action");
        AutomationProperties.SetName(button, T(key)); button.Click += (_, _) => action();
        return button;
    }
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
    private StackPanel Heading(string key, string? subtitle = null) => Stack(Label(T(key), TextRole.PageTitle), Muted(T(subtitle ?? "Tagline")));
    private Expander Expand(string key, Control content, bool open = false) => new()
    { Header = T(key), Content = content, IsExpanded = open, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private StackPanel Field(string key, Control control)
    {
        AutomationProperties.SetName(control, T(key));
        return Stack(Muted(T(key)), control);
    }
    private Border Empty(string key = "NoSignificant") => Surface(Stack(Label(T(key), TextRole.SectionTitle), Muted(T(ViewModel.HasAnalysis ? "CheckCoverage" : "NoHistory"))));
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
    private ListBox IncidentList(IEnumerable<IncidentRow> rows, bool overview = false)
    {
        var list = new ListBox { ItemsSource = rows, Name = overview ? "RecentSignificantList" : "IncidentList",
            Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        var stretch = new Style(selector => selector.OfType<ListBoxItem>());
        stretch.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        list.Styles.Add(stretch);
        list.ItemTemplate = new FuncDataTemplate<IncidentRow>((row, _) =>
        {
            if (row is null) return null;
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            top.Children.Add(Label(row.Title, TextRole.RowTitle)); var time = Muted(row.Timestamp); Grid.SetColumn(time, 1); top.Children.Add(time);
            var body = Stack(top, Label(row.Assessment), Muted(row.Strength + "   ·   " + row.PriorityText + "   ·   " + row.Context));
            if (row.RecurrenceCount > 1) body.Children.Add(Muted(row.Recurrence));
            if (row.SharedReportCount > 1) body.Children.Add(Muted(row.SharedReport));
            if (row.DevelopmentContext.Length > 0) body.Children.Add(Muted(row.DevelopmentContext));
            var rowBorder = new Border { Child = body, Padding = new Thickness(D("component.row.padding")), MinHeight = D("component.row.minHeight"), BorderThickness = new Thickness(0,0,0,1) };
            rowBorder.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("AppBorder"));
            return rowBorder;
        }, true);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is IncidentRow row) ViewModel.Select(row); };
        return list;
    }
}
