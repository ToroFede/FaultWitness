using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Pages;
using FaultWitness.App.Views.Components;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

public sealed class SystemColumnLayoutTests
{
    private static readonly string[] Keys = ["InventoryGroupOperatingSystem", "InventoryGroupProcessorMemory", "InventoryGroupGraphics", "InventoryGroupFirmware", "InventoryGroupStorage", "InventoryGroupDrivers"];

    [AvaloniaFact]
    public void GrowingAnEarlierCardMovesOnlySuccessorsInItsColumn()
    {
        var panel = new IndependentColumnsPanel { Columns = 2, ColumnSpacing = 12, RowSpacing = 12 };
        var cards = new[] { new Border { Height = 100 }, new Border { Height = 200 }, new Border { Height = 50 }, new Border { Height = 75 } };
        panel.Children.AddRange(cards);
        var window = new Window { Content = panel };
        VisualRenderGeometry.ShowAtRequestedGeometry(window, 600, 1000);
        void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        try
        {
            Layout();
            Assert.Equal(287, panel.DesiredSize.Height);
            Assert.Equal(112, cards[2].Bounds.Y);
            Assert.Equal(212, cards[3].Bounds.Y);
            var otherColumn = new[] { cards[1].Bounds, cards[3].Bounds };
            cards[0].Height = 500; Layout();
            Assert.Equal(512, cards[2].Bounds.Y);
            Assert.Equal(562, panel.DesiredSize.Height);
            Assert.Equal(otherColumn, new[] { cards[1].Bounds, cards[3].Bounds });
            Assert.Equal(cards, panel.Children);
            panel.Columns = 1; Layout();
            Assert.Equal(new[] { 0d, 512, 724, 786 }, cards.Select(card => card.Bounds.Y));
            Assert.Equal(861, panel.DesiredSize.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")]
    [InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public async Task CardsUseIndependentColumnsWithConstantGapsInEveryExpansionState(string language)
    {
        var snapshot = Fixture();
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_SYSTEM_LAYOUT_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "fixture.json"), JsonSerializer.Serialize(snapshot));
        }
        using var vm = new MainViewModel(new TestServices { StructuredInventory = snapshot,
            Settings = new UserSettings(Language: language, ReduceMotion: true) });
        var window = new MainWindow(vm);
        VisualRenderGeometry.ShowAtRequestedGeometry(window, 1280, 1000);
        try
        {
            vm.Navigate(AppPage.System); await vm.RefreshInventoryAsync();
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark, AppTheme.System })
            foreach (var width in new[] { 1280, 560 })
            {
                vm.ChangeSettings(vm.Settings with { Theme = theme });
                window.Width = width;
                window.SetRenderScaling(width == 560 ? 2 : 1.5);
                Settle(window);
                var inventory = window.GetVisualDescendants().OfType<SystemView>().Single().FindControl<ItemsControl>("SystemInventory")!;
                var cards = Cards(inventory);
                Assert.Equal(Keys, cards.Select(card => ((InventoryGroupPresentation)card.DataContext!).Key));
                var storage = cards[4].GetVisualDescendants().OfType<Expander>().Single();
                var drivers = cards[5].GetVisualDescendants().OfType<Expander>().Single();
                storage.IsExpanded = drivers.IsExpanded = false; Settle(window);
                var baseline = Rectangles(cards, inventory);
                foreach (var mode in new[] { "collapsed", "storage", "drivers", "both" })
                {
                    storage.IsExpanded = mode is "storage" or "both";
                    drivers.IsExpanded = mode is "drivers" or "both";
                    Settle(window);
                    var bounds = Rectangles(cards, inventory);
                    var columns = width == 560 ? 1 : 2;
                    var gap = (double)window.FindResource("primitive.space.3")!;
                    for (var i = 0; i < cards.Length; i++)
                    {
                        Assert.InRange(Math.Abs(cards[i].DesiredSize.Height - cards[i].Margin.Top - cards[i].Margin.Bottom - cards[i].Bounds.Height), 0, 1);
                        Assert.True(bounds[i].Right <= inventory.Bounds.Width + 1);
                        if (i < columns) Assert.InRange(Math.Abs(bounds[i].Y), 0, 1);
                        else Assert.InRange(Math.Abs(bounds[i].Y - bounds[i - columns].Bottom - gap), 0, 1);
                        if (i % columns != 0) Assert.True(bounds[i].X > bounds[i - 1].X);
                    }
                    if (columns == 2)
                    {
                        Assert.Equal(baseline[0], bounds[0]); Assert.Equal(baseline[1], bounds[1]);
                        Assert.Equal(baseline[2], bounds[2]); Assert.Equal(baseline[3], bounds[3]);
                        if (mode == "storage") Assert.Equal(baseline[5], bounds[5]);
                        if (mode == "drivers") Assert.Equal(baseline[4], bounds[4]);
                    }
                    Assert.Equal(snapshot.Groups.Sum(group => group.Devices.Count), cards.Sum(card =>
                        ((InventoryGroupPresentation)card.DataContext!).Devices.Count + ((InventoryGroupPresentation)card.DataContext!).AdditionalDevices.Count));
                    Assert.Same(snapshot, vm.SystemInventory);
                    Assert.All(inventory.GetVisualDescendants().OfType<ScrollViewer>(), scroll =>
                        Assert.True(scroll.Viewport.Width == 0 || scroll.Extent.Width <= scroll.Viewport.Width + 1));
                }
                storage.IsExpanded = drivers.IsExpanded = false;
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ExpansionAndKeyboardOrderSurviveResponsiveAndLocaleRefresh()
    {
        using var vm = new MainViewModel(new TestServices { StructuredInventory = Fixture(), Settings = new UserSettings(ReduceMotion: true) });
        var window = new MainWindow(vm); VisualRenderGeometry.ShowAtRequestedGeometry(window, 1280, 1000);
        try
        {
            vm.Navigate(AppPage.System); await vm.RefreshInventoryAsync(); Settle(window);
            var inventory = window.GetVisualDescendants().OfType<SystemView>().Single().FindControl<ItemsControl>("SystemInventory")!;
            Cards(inventory)[4].GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
            foreach (var width in new[] { 560, 1280 })
            {
                window.Width = width; vm.ChangeSettings(vm.Settings with { Language = "de" }); Settle(window);
                var cards = Cards(inventory);
                var storage = cards[4].GetVisualDescendants().OfType<Expander>().Single();
                var drivers = cards[5].GetVisualDescendants().OfType<Expander>().Single();
                Assert.True(storage.IsExpanded);
                Assert.True(storage.GetVisualDescendants().OfType<ToggleButton>().First().Focus(NavigationMethod.Tab));
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
                window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t"); Settle(window);
                Assert.True(drivers.IsKeyboardFocusWithin);
            }
        }
        finally { window.Close(); }
    }

    internal static SystemInventorySnapshot Fixture() => new([
        new(Keys[0], [new("os", [new("InventoryOperatingSystem", "Synthetic Windows"), new("InventoryOsVersion", "Synthetic 26"), new("InventoryArchitecture", "x64")])]),
        new(Keys[1], [new("cpu", Enumerable.Range(0, 8).Select(i => new InventoryField("InventoryProcessorLogical", "Synthetic processor information " + i)).ToArray())]),
        new(Keys[2], [new("gpu", [new("InventoryGraphicsName", null, InventoryAvailability.AccessDenied)])]),
        new(Keys[3], []),
        Devices(Keys[4], "InventoryStorageModel", 8), Devices(Keys[5], "InventoryDriverName", 24)]);

    private static InventoryGroup Devices(string key, string field, int count) => new(key, Enumerable.Range(0, count)
        .Select(i => new InventoryDevice("synthetic-" + i, [new(field, "Synthetic device " + i + " — langer Gerätename / длинное описание / dispositivo con descrizione lunga"),
            new("InventoryDriverProvider", i % 3 == 0 ? null : "Synthetic provider", i % 3 == 0 ? InventoryAvailability.Unavailable : InventoryAvailability.Available),
            new("InventoryDriverVersion", "Synthetic 1.2.3"), new("InventoryDriverDate", "Synthetic date")])).ToArray());

    private static Border[] Cards(ItemsControl inventory) => inventory.GetVisualDescendants().OfType<Border>()
        .Where(card => card.Classes.Contains("action-surface") && card.DataContext is InventoryGroupPresentation).ToArray();
    private static Rect[] Rectangles(Border[] cards, Control relative) => cards.Select(card =>
        new Rect(card.TranslatePoint(default, relative)!.Value, card.Bounds.Size)).ToArray();
    private static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
}
