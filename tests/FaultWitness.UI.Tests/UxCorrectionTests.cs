using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Storage;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Headless")]
public sealed class UxCorrectionTests
{
    private static readonly string[] SummaryMetricNames = ["CountAttention", "CountKnowing", "CountBackground"];
    private static readonly string[] MainNavigationNames = ["NavHome", "NavAnalyze", "NavIncidents", "NavHistory", "NavSystem", "NavSettings"];

    [AvaloniaTheory]
    [InlineData(639)]
    [InlineData(640)]
    [InlineData(641)]
    public void NavigationBrand_RemainsSingleLineAndFullyVisibleAtCompactBoundary(double width)
    {
        foreach (var language in new[] { "en", "it", "es", "fr", "de", "pt", "ru", "pl" })
        {
            var window = Open(new TestServices { Settings = new UserSettings(Language: language) }, width);
            try
            {
                var brand = Find<TextBlock>(window, "NavigationBrand");
                var measurement = new TextBlock
                {
                    Text = brand.Text,
                    FontFamily = brand.FontFamily,
                    FontSize = brand.FontSize,
                    FontStyle = brand.FontStyle,
                    FontWeight = brand.FontWeight,
                    TextWrapping = TextWrapping.NoWrap
                };
                measurement.Measure(Size.Infinity);

                Assert.Equal(TextWrapping.NoWrap, brand.TextWrapping);
                Assert.True(brand.Bounds.Width + 0.5 >= measurement.DesiredSize.Width, $"{language}/{width}: navigation brand is truncated");
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void MainNavigation_ExposesSelectedStateAndMapsChildDestinations()
    {
        var window = Open();
        try
        {
            AssertMainNavigation(window, "NavHome");

            window.ViewModel.SetResult(SyntheticResults.Create(3));
            window.ViewModel.Select(window.ViewModel.AllRows[0]);
            AssertMainNavigation(window, "NavIncidents");

            window.ViewModel.Navigate(AppPage.Readiness);
            AssertMainNavigation(window, "NavSystem");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(640, true)]
    [InlineData(641, false)]
    [InlineData(1008, false)]
    public void HomeSummary_UsesResponsiveDenseContainerAndRendersAllPriorities(double width, bool stacked)
    {
        var window = Open(width: width);
        try
        {
            window.ViewModel.SetResult(SyntheticResults.Create(3));
            window.UpdateLayout();
            var summary = Find<Panel>(window, "HomeSummary");

            if (stacked)
            {
                Assert.IsType<StackPanel>(summary);
                Assert.Contains("summary-stacked", summary.Classes);
            }
            else
            {
                Assert.IsType<WrapPanel>(summary);
                Assert.Contains("summary-strip", summary.Classes);
            }

            Assert.Equal(3, summary.Children.Count);
            Assert.All(SummaryMetricNames, name => Assert.True(Find<Button>(window, name).IsVisible));
            Assert.True(summary.Bounds.Height <= 3 * Convert.ToDouble(Avalonia.Application.Current!.Resources["component.row.minHeight"], CultureInfo.InvariantCulture));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DetailCopy_IsSecondaryAndNotACompetingPrimaryAction()
    {
        var window = Open();
        try
        {
            window.ViewModel.SetResult(SyntheticResults.Create(3));
            window.ViewModel.Select(window.ViewModel.AllRows[0]);
            var copy = Find<Button>(window, "DetailCopy");

            Assert.Contains("secondary-action", copy.Classes);
            Assert.DoesNotContain("primary-action", copy.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public async Task HistorySelection_UsesSemanticSelectionSurface(AppTheme theme)
    {
        var scan = Stored("selected-history");
        var window = Open(new TestServices { History = [scan, Stored("other-history")], Settings = new UserSettings(Language: "en", Theme: theme) });
        try
        {
            await window.ViewModel.RefreshHistoryAsync();
            window.ViewModel.Navigate(AppPage.History);
            window.UpdateLayout();

            var list = Find<ListBox>(window, "HistoryList");
            var item = list.GetVisualDescendants().OfType<ListBoxItem>().Single(row => row.IsSelected);
            Assert.True(item.IsSelected, "Initial History container must be selected.");
            var presenter = item.GetVisualDescendants().OfType<ContentPresenter>().Single(control => control.Name == "PART_ContentPresenter");
            Assert.True(presenter.TryFindResource("AppSelection", presenter.ActualThemeVariant, out var selection));
            var expected = Assert.IsAssignableFrom<ISolidColorBrush>(selection);
            var actual = Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background);
            Assert.Equal(expected.Color, actual.Color);
            Assert.True(item.GetVisualDescendants().OfType<Border>().Single(row => row.Name == "HistorySelectionIndicator").IsVisible);

            window.ViewModel.SelectHistory(window.ViewModel.History[1]);
            window.UpdateLayout();
            Assert.Same(list, Find<ListBox>(window, "HistoryList"));
            Assert.Same(window.ViewModel.SelectedHistory, list.SelectedItem);
            Assert.False(item.IsSelected);
            Assert.False(item.GetVisualDescendants().OfType<Border>().Single(row => row.Name == "HistorySelectionIndicator").IsVisible);
            var next = list.GetVisualDescendants().OfType<ListBoxItem>().Single(row => row.IsSelected);
            var nextPresenter = next.GetVisualDescendants().OfType<ContentPresenter>().Single(control => control.Name == "PART_ContentPresenter");
            Assert.Equal(expected.Color, Assert.IsAssignableFrom<ISolidColorBrush>(nextPresenter.Background).Color);
            Assert.NotEqual(expected.Color, (presenter.Background as ISolidColorBrush)?.Color);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SettingsHeading_UsesSettingsPurposeInsteadOfGenericTagline()
    {
        var window = Open();
        try
        {
            window.ViewModel.Navigate(AppPage.Settings);
            var text = VisibleText(window);

            Assert.Contains(window.ViewModel.Text.Get("SettingsPurpose"), text, StringComparison.Ordinal);
            Assert.DoesNotContain(window.ViewModel.Text.Get("Tagline"), text, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CompletedStatus_IsScopedToItsPageWhileActiveCancellationRemainsVisibleAcrossNavigation()
    {
        var window = Open();
        try
        {
            Assert.False(Find<TextBlock>(window, "StatusText").IsVisible);
            window.ViewModel.Navigate(AppPage.Readiness);
            await window.ViewModel.RefreshReadinessAsync();
            Assert.True(Find<TextBlock>(window, "StatusText").IsVisible);

            window.ViewModel.Navigate(AppPage.Settings);
            Assert.False(Find<TextBlock>(window, "StatusText").IsVisible);
        }
        finally
        {
            window.Close();
        }

        var cancellationServices = new TestServices { WaitForCancellation = true };
        var cancellationWindow = Open(cancellationServices);
        Task? running = null;
        try
        {
            running = cancellationWindow.ViewModel.AnalyzeAsync();
            cancellationWindow.ViewModel.Navigate(AppPage.Settings);

            Assert.True(Find<TextBlock>(cancellationWindow, "StatusText").IsVisible);
            Assert.True(Find<ProgressBar>(cancellationWindow, "AnalysisProgress").IsVisible);
            cancellationWindow.ViewModel.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            cancellationWindow.ViewModel.Cancel();
            if (running is not null) await running.WaitAsync(TimeSpan.FromSeconds(5));
            cancellationWindow.Close();
        }
    }

    [AvaloniaFact]
    public void SystemNavigation_AutoLoadsInventoryAndMarksItsTwoDestinations()
    {
        var window = Open();
        try
        {
            window.ViewModel.Navigate(AppPage.System);
            window.UpdateLayout();
            Assert.NotEmpty(window.ViewModel.Inventory);
            AssertSubnavigation(window, "SystemInformationTab", "SystemReadinessTab");

            window.ViewModel.Navigate(AppPage.Readiness);
            AssertSubnavigation(window, "SystemReadinessTab", "SystemInformationTab");
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertMainNavigation(MainWindow window, string selectedName)
    {
        window.UpdateLayout();
        var buttons = MainNavigationNames
            .Select(name => Find<Button>(window, name)).ToArray();
        var selected = buttons.Single(button => button.Name == selectedName);
        Assert.Contains("selected", selected.Classes);
        Assert.Equal(1, ((Grid)selected.Content!).Children[0].Opacity);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(selected)));

        foreach (var inactive in buttons.Where(button => button != selected))
        {
            Assert.DoesNotContain("selected", inactive.Classes);
            Assert.True(inactive.IsEnabled);
            Assert.Equal(1, inactive.Opacity);
        }
    }

    private static void AssertSubnavigation(MainWindow window, string selectedName, string inactiveName)
    {
        var selected = Find<Button>(window, selectedName);
        var inactive = Find<Button>(window, inactiveName);
        Assert.Contains("selected", selected.Classes);
        Assert.DoesNotContain("selected", inactive.Classes);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(selected)));
        Assert.Equal(1, inactive.Opacity);
    }

    private static StoredScan Stored(string id)
    {
        var started = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        return new StoredScan(id, started, started.AddMinutes(1), "1.0.0", new ScanHistoryMetadata("recent", started, started.AddMinutes(1), 10, 0, 0, 0, "synthetic"), []);
    }

    private static MainWindow Open(TestServices? services = null, double? width = null)
    {
#pragma warning disable CA2000 // MainWindow owns this view model and disposes it on Closed.
        var window = new MainWindow(new MainViewModel(services ?? new TestServices()));
#pragma warning restore CA2000
        if (width is not null) window.Width = width.Value;
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static T Find<T>(MainWindow window, string name) where T : Control
    {
        window.UpdateLayout();
        return window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    }

    private static string VisibleText(MainWindow window)
    {
        window.UpdateLayout();
        return string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
    }
}
