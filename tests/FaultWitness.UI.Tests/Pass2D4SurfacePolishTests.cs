using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;
using FaultWitness.Storage;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Pass2D4SurfacePolish")]
public sealed class Pass2D4SurfacePolishTests
{
    [AvaloniaTheory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public async Task SemanticListsAndDisclosuresUseThemeSurfacesAndKeepInteractionStates(AppTheme theme)
    {
        var scans = new[] { Stored("selected"), Stored("ordinary") };
        var window = Open(new TestServices { History = scans, Settings = new UserSettings(Language: "en", Theme: theme) }, 1280);
        try
        {
            window.ViewModel.SetResult(SyntheticResults.Create(3));
            window.ViewModel.Navigate(AppPage.Home);
            window.UpdateLayout();
            var home = Current<HomeView>(window);
            AssertSemanticList(home.FindControl<ListBox>("RecentSignificantList")!);

            var disclosure = home.FindControl<Expander>("HomeCoverage")!;
            AssertDisclosureSurface(window, disclosure);

            await window.ViewModel.RefreshHistoryAsync();
            window.ViewModel.Navigate(AppPage.History);
            window.ViewModel.SelectHistory(window.ViewModel.History.Single(row => row.Scan.Id == scans[0].Id));
            window.UpdateLayout();

            var history = Current<HistoryView>(window).FindControl<ListBox>("HistoryList")!;
            AssertSemanticList(history);
            var items = history.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            var selected = Assert.Single(items, item => item.IsSelected);
            var ordinary = Assert.Single(items, item => !item.IsSelected);
            Assert.Equal(ResourceColor(history, "AppSelection"), BackgroundColor(Presenter(selected)));
            Assert.Equal(ResourceColor(history, "AppSurface"), BackgroundColor(Presenter(ordinary)));
            Assert.True(ordinary.Focusable);

            var hoverPoint = ordinary.TranslatePoint(new Point(ordinary.Bounds.Width / 2, ordinary.Bounds.Height / 2), window);
            Assert.NotNull(hoverPoint);
            window.MouseMove(hoverPoint.Value, RawInputModifiers.None);
            window.UpdateLayout();
            Assert.Equal(ResourceColor(history, "AppSurfaceMuted"), BackgroundColor(Presenter(ordinary)));
            Assert.Equal(ResourceColor(history, "AppSelection"), BackgroundColor(Presenter(selected)));

            Assert.True(history.Focus());
            window.UpdateLayout();
            Assert.True(history.IsKeyboardFocusWithin);
            Assert.Equal(scans[0].Id, window.ViewModel.SelectedHistory!.Scan.Id);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task HistorySpacingSeparatesHeadingListAndCompactDetailWhileRetainingSelection()
    {
        var scans = new[] { Stored("first"), Stored("second") };
        var window = Open(new TestServices { History = scans }, 600);
        try
        {
            await window.ViewModel.RefreshHistoryAsync();
            window.ViewModel.Navigate(AppPage.History);
            window.ViewModel.SelectHistory(window.ViewModel.History.Single(row => row.Scan.Id == scans[1].Id));
            window.UpdateLayout();

            var view = Current<HistoryView>(window);
            view.ApplyLayout("small");
            window.UpdateLayout();
            var grid = view.FindControl<Grid>("HistoryLayout")!;
            var heading = view.FindControl<StackPanel>("HistoryHeading")!;
            var list = view.FindControl<ListBox>("HistoryList")!;
            var detail = view.FindControl<ScrollViewer>("HistoryDetailScroll")!;
            var gap = Convert.ToDouble(Avalonia.Application.Current!.Resources["primitive.space.4"], System.Globalization.CultureInfo.InvariantCulture);

            Assert.Equal(gap, grid.RowSpacing);
            Assert.InRange(Math.Abs(list.Bounds.Y - heading.Bounds.Bottom - gap), 0, 1);
            Assert.InRange(Math.Abs(detail.Bounds.Y - list.Bounds.Bottom - gap), 0, 1);
            Assert.Equal(scans[1].Id, window.ViewModel.SelectedHistory!.Scan.Id);
            Assert.Equal(scans[1].Id, Assert.IsType<FaultWitness.App.Presentation.HistoryItemPresentation>(list.SelectedItem).SourceRow.Scan.Id);

            view.ApplyLayout("large");
            window.UpdateLayout();
            Assert.Equal(2, grid.RowDefinitions.Count);
            Assert.Equal(1, Grid.GetRow(list));
            Assert.Equal(1, Grid.GetRow(detail));
            Assert.Equal(1, Grid.GetColumn(detail));
            Assert.Equal(scans[1].Id, window.ViewModel.SelectedHistory!.Scan.Id);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(600, "pl", AppTheme.Light)]
    [InlineData(1280, "en", AppTheme.Light)]
    [InlineData(1280, "en", AppTheme.Dark)]
    public void MissingIncidentFallbackSizesToContentAndStaysAtTop(int width, string language, AppTheme theme)
    {
        var window = Open(new TestServices { Settings = new UserSettings(Language: language, Theme: theme) }, width);
        try
        {
            window.ViewModel.Navigate(AppPage.Detail);
            window.UpdateLayout();
            var view = Current<IncidentUnavailableView>(window);
            var surface = view.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("action-surface"));

            Assert.Equal(VerticalAlignment.Top, surface.VerticalAlignment);
            Assert.True(surface.Bounds.Height < view.Bounds.Height / 2,
                $"Fallback surface filled too much of the page: {surface.Bounds.Height:0.##}/{view.Bounds.Height:0.##}.");
            Assert.Contains(window.ViewModel.Text.Get("SelectIncident"), VisibleText(view), StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(((FaultWitness.App.Presentation.IncidentUnavailablePresentation)view.DataContext!).Help));
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertSemanticList(ListBox list)
    {
        Assert.Contains("semantic-list", list.Classes);
        Assert.Equal(ResourceColor(list, "AppSurface"), Assert.IsAssignableFrom<ISolidColorBrush>(list.Background).Color);
        Assert.Equal(ResourceColor(list, "AppBorder"), Assert.IsAssignableFrom<ISolidColorBrush>(list.BorderBrush).Color);
        var rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Assert.NotEmpty(rows);
        foreach (var row in rows.Where(item => !item.IsSelected))
            Assert.Equal(ResourceColor(list, "AppSurface"), BackgroundColor(Presenter(row)));
        foreach (var row in rows.Where(item => item.IsSelected))
            Assert.Equal(ResourceColor(list, "AppSelection"), BackgroundColor(Presenter(row)));
    }

    private static void AssertDisclosureSurface(MainWindow window, Expander expander)
    {
        window.UpdateLayout();
        Assert.Contains("semantic-disclosure", expander.Classes);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(expander)));
        Assert.Equal(ResourceColor(expander, "AppSurface"), Assert.IsAssignableFrom<ISolidColorBrush>(expander.Background).Color);

        var header = expander.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "ExpanderHeader");
        Assert.Equal(expander.ActualThemeVariant, header.ActualThemeVariant);
        var headerSurface = header.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ToggleButtonBackground");
        var body = expander.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ExpanderContent");
        var expectedHeaderBackground = ResourceColor(expander, "AppSurfaceMuted");
        var headerThemeSurface = ResourceColor(header, "AppSurfaceMuted");
        Assert.True(expectedHeaderBackground == headerThemeSurface,
            $"Theme variants: expander={expander.ActualThemeVariant}, header={header.ActualThemeVariant}; resources {expectedHeaderBackground}/{headerThemeSurface}; header={header.Background}; style theme={header.Theme}.");
        var actualHeaderProperty = Assert.IsAssignableFrom<ISolidColorBrush>(header.Background).Color;
        Assert.True(expectedHeaderBackground == actualHeaderProperty,
            $"Header property mismatch: {actualHeaderProperty}; pointer={header.IsPointerOver}; pressed={header.IsPressed}; focused={header.IsFocused}; theme={header.Theme}.");
        var actualHeaderBackground = Assert.IsAssignableFrom<ISolidColorBrush>(headerSurface.Background).Color;
        Assert.True(expectedHeaderBackground == actualHeaderBackground,
            $"Header palette mismatch: {actualHeaderBackground}; pointer={header.IsPointerOver}; pressed={header.IsPressed}; focused={header.IsFocused}; header background={header.Background}; theme={header.Theme}.");
        Assert.Equal(ResourceColor(expander, "AppBorder"), Assert.IsAssignableFrom<ISolidColorBrush>(headerSurface.BorderBrush).Color);

        expander.IsExpanded = true;
        window.UpdateLayout();
        Assert.True(expander.IsExpanded);
        Assert.True(body.IsVisible);
        Assert.Equal(ResourceColor(expander, "AppSurface"), Assert.IsAssignableFrom<ISolidColorBrush>(body.Background).Color);
        Assert.True(header.Focusable);

        var hoverPoint = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window);
        Assert.NotNull(hoverPoint);
        window.MouseMove(hoverPoint.Value, RawInputModifiers.None);
        window.UpdateLayout();
        Assert.Equal(ResourceColor(expander, "AppSelection"), Assert.IsAssignableFrom<ISolidColorBrush>(headerSurface.Background).Color);

        Assert.True(header.Focus());
        window.UpdateLayout();
        Assert.Equal(ResourceColor(expander, "AppAccent"), Assert.IsAssignableFrom<ISolidColorBrush>(headerSurface.BorderBrush).Color);

        expander.IsEnabled = false;
        window.UpdateLayout();
        Assert.Equal(ResourceColor(expander, "AppSurfaceMuted"), Assert.IsAssignableFrom<ISolidColorBrush>(headerSurface.Background).Color);
        Assert.Equal(ResourceColor(expander, "AppMuted"), Assert.IsAssignableFrom<ISolidColorBrush>(header.Foreground).Color);
    }

    private static Color ResourceColor(Control control, string key)
    {
        Assert.True(control.TryFindResource(key, control.ActualThemeVariant, out var value), $"Missing {key} for {control.ActualThemeVariant}.");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static ContentPresenter Presenter(ListBoxItem row) =>
        row.GetVisualChildren().OfType<ContentPresenter>().Single(item => item.Name == "PART_ContentPresenter");

    private static Color BackgroundColor(ContentPresenter presenter) => Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color;

    private static StoredScan Stored(string id)
    {
        var started = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        return new StoredScan(id, started, started.AddMinutes(1), "1.0.0",
            new ScanHistoryMetadata("recent", started.AddDays(-7), started, 7, 1, 0, 0, "SourceSystem=Complete"), []);
    }

    private static MainWindow Open(TestServices services, int width)
    {
#pragma warning disable CA2000 // MainWindow owns its view model and disposes it on Closed.
        var window = new MainWindow(new MainViewModel(services));
#pragma warning restore CA2000
        VisualRenderGeometry.ShowAtRequestedGeometry(window, width, 900);
        return window;
    }

    private static T Current<T>(MainWindow window) where T : Control =>
        Assert.IsType<T>(window.FindControl<ContentControl>("PageHost")!.Content);

    private static string VisibleText(Control root) =>
        string.Join("\n", root.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
}
