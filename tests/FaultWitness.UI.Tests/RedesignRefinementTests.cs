using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Views.Components;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;

#pragma warning disable CA2000 // Window.Closed owns the view-model lifetime.
namespace FaultWitness.UI.Tests;

public sealed class RedesignRefinementTests
{
    private static readonly string[] EntryPoints = ["PrimaryAnalyze", "StartAroundFlow", "StartFilesFlow"];

    [AvaloniaFact]
    public void MetricKeyboardFocusDoesNotChangeGeometry()
    {
        var vm = new MainViewModel(new TestServices { Settings = new UserSettings(ReduceMotion: true) });
        var window = new MainWindow(vm); window.Show();
        try
        {
            vm.SetResult(SyntheticResults.Create(3)); window.UpdateLayout();
            var metric = window.GetVisualDescendants().OfType<MetricCard>().First();
            var bounds = metric.Bounds; var thickness = metric.BorderThickness;
            Assert.True(metric.Focus(NavigationMethod.Tab)); window.UpdateLayout();
            Assert.True(metric.IsKeyboardFocusWithin);
            Assert.Equal(bounds, metric.Bounds);
            Assert.Equal(thickness, metric.BorderThickness);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.System)]
    public void UnsupportedMaterialRemainsOpaqueAcrossThemeAndPreferenceChanges(AppTheme theme)
    {
        var vm = new MainViewModel(new TestServices { Settings = new UserSettings(Theme: theme, UseSystemBackdrop: true) });
        var window = new MainWindow(vm); window.Show();
        try
        {
            foreach (var enabled in new[] { true, false, true })
            {
                vm.ChangeSettings(vm.Settings with { UseSystemBackdrop = enabled });
                window.UpdateLayout();
                Assert.Equal(WindowTransparencyLevel.None, window.ActualTransparencyLevel);
                Assert.DoesNotContain("native-material", window.Classes);
                Assert.IsAssignableFrom<ISolidColorBrush>(window.Resources["AppCard"]);
                Assert.Equal(1d, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Opacity);
                Assert.Equal(1d, Assert.IsAssignableFrom<ISolidColorBrush>(window.FindControl<Border>("NavigationSurface")!.Background).Opacity);
                Assert.All(window.GetVisualDescendants().OfType<SurfaceCard>(), card =>
                {
                    Assert.Equal(1d, Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Opacity);
                    Assert.Equal(1d, card.Opacity); // Text is never made translucent with the surface.
                    Assert.True(card.GetVisualDescendants().OfType<Border>().Single().BoxShadow.Count > 0);
                });
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(560, "de")]
    [InlineData(1280, "it")]
    public void CompactHomeRetainsAllEntryPointsAndMakesResultsEarlier(int width, string language)
    {
        var vm = new MainViewModel(new TestServices { Settings = new UserSettings(Language: language, ReduceMotion: true) });
        var window = new MainWindow(vm); window.Show(); window.Width = width; window.Height = 900;
        try
        {
            vm.SetResult(SyntheticResults.Create(3)); window.UpdateLayout();
            var home = window.GetVisualDescendants().OfType<HomeView>().Single();
            var hero = home.GetVisualDescendants().OfType<SurfaceCard>().Single();
            Assert.Equal(new Thickness(16), hero.Padding);
            Assert.All(EntryPoints, name => Assert.True(home.FindControl<Button>(name)!.IsEffectivelyVisible));
            Assert.All(home.GetVisualDescendants().OfType<ScrollViewer>().Where(v => v.Viewport.Width > 0),
                v => Assert.True(v.Extent.Width <= v.Viewport.Width + 2));
            if (width == 1280) Assert.True(hero.Bounds.Height < 300);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task InventoryBindingsRefreshFromEmptyToDataAndPreserveDataOnFailure()
    {
        var services = new TestServices { StructuredInventory = new SystemInventorySnapshot([]) };
        var vm = new MainViewModel(services); var window = new MainWindow(vm); window.Show();
        try
        {
            vm.Navigate(AppPage.System); await vm.RefreshInventoryAsync(); window.UpdateLayout();
            var page = window.GetVisualDescendants().OfType<SystemView>().Single();
            Assert.True(page.FindControl<TextBlock>("SystemInventoryStatus")!.IsVisible);
            Assert.Empty(vm.SystemInventory.Groups);
            services.StructuredInventory = new SystemInventorySnapshot([
                new InventoryGroup("InventoryGroupOperatingSystem", [new("fixture-os", [new("InventoryOperatingSystem", "Synthetic Windows")])]),
                new InventoryGroup("InventoryGroupGraphics", [new("fixture-gpu", [new("InventoryGraphicsName", null, InventoryAvailability.AccessDenied)])])]);
            await vm.RefreshInventoryAsync(); window.UpdateLayout();
            Assert.Same(page, window.GetVisualDescendants().OfType<SystemView>().Single());
            Assert.False(page.FindControl<TextBlock>("SystemInventoryStatus")!.IsVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Synthetic Windows", StringComparison.Ordinal) == true);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains(vm.Text.Get("InventoryAvailabilityAccessDenied"), StringComparison.Ordinal) == true);
            services.FailInventory = true; await vm.RefreshInventoryAsync(); window.UpdateLayout();
            Assert.Equal("SourceUnavailable", vm.StatusKey);
            Assert.Equal(2, vm.SystemInventory.Groups.Count);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Synthetic Windows", StringComparison.Ordinal) == true);
        }
        finally { window.Close(); }
    }
}
