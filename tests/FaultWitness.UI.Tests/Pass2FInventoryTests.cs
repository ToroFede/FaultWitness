using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Presentation;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

public sealed class Pass2FInventoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [AvaloniaFact]
    public async Task ExpansionChangesOnlyContainingCardHeight()
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2F_INVENTORY_OUTPUT");
        var reproduce = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2F_REPRO") == "1";
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);
        var measurements = new List<object>();
        foreach (var (width, locale, theme) in new[] { (1280, "en", AppTheme.Light), (1280, "en", AppTheme.Dark),
            (1920, "de", AppTheme.Light), (641, "ru", AppTheme.Dark), (600, "pl", AppTheme.Light) })
        {
            using var vm = new MainViewModel(new TestServices
            {
                Settings = new UserSettings(Language: locale, Theme: theme),
                StructuredInventory = Inventory()
            });
            var window = new MainWindow(vm);
            VisualRenderGeometry.ShowAtRequestedGeometry(window, width, 1100);
            try
            {
                vm.Navigate(AppPage.System);
                await vm.RefreshInventoryAsync();
                CaptureAxamlTests.Settle(window);
                var cards = window.GetVisualDescendants().OfType<Border>()
                    .Where(item => item.DataContext is InventoryGroupPresentation && item.Classes.Contains("action-surface"))
                    .ToDictionary(item => ((InventoryGroupPresentation)item.DataContext!).Key, StringComparer.Ordinal);
                var memoryCard = cards["InventoryGroupProcessorMemory"];
                // The current product has no RAM disclosure. A test-only nested disclosure exercises the same
                // container boundary without changing production inventory data/grouping/presentation.
                var memory = new Expander { Header = "Synthetic RAM details", Content = new TextBlock
                    { Text = string.Join(Environment.NewLine, Enumerable.Repeat("Synthetic RAM bank — illustrative sample", 9)) } };
                ((StackPanel)memoryCard.Child!).Children.Add(memory);
                CaptureAxamlTests.Settle(window);
                var baseline = cards.ToDictionary(pair => pair.Key, pair => pair.Value.Bounds.Height, StringComparer.Ordinal);
                Save(window, output, $"inventory-{width}-{locale}-{theme}-collapsed");
                foreach (var state in new[] { "memory", "storage", "multiple-devices" })
                {
                    var expanded = state == "memory" ? memory : cards["InventoryGroupStorage"].GetVisualDescendants().OfType<Expander>().Single();
                    expanded.IsExpanded = true;
                    if (state == "multiple-devices")
                        cards["InventoryGroupDrivers"].GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
                    CaptureAxamlTests.Settle(window);
                    var containing = state == "memory" ? "InventoryGroupProcessorMemory" : "InventoryGroupStorage";
                    Assert.True(cards[containing].Bounds.Height > baseline[containing] + 20);
                    foreach (var key in new[] { "InventoryGroupOperatingSystem", "InventoryGroupFirmware", "InventoryGroupGraphics" })
                    {
                        var height = cards[key].Bounds.Height;
                        if (!reproduce) Assert.Equal(baseline[key], height, 1);
                        measurements.Add(new { width, locale, theme = theme.ToString(), state, card = key,
                            baselineHeight = baseline[key], expandedHeight = height, stretched = height > baseline[key] + 1 });
                    }
                    CaptureAxamlTests.AssertNoOverflow(window);
                    cards[state == "multiple-devices" ? "InventoryGroupDrivers" : containing].BringIntoView();
                    CaptureAxamlTests.Settle(window);
                    Save(window, output, $"inventory-{width}-{locale}-{theme}-{state}");
                    expanded.IsExpanded = false;
                    cards["InventoryGroupDrivers"].GetVisualDescendants().OfType<Expander>().Single().IsExpanded = false;
                    CaptureAxamlTests.Settle(window);
                }
            }
            finally { window.Close(); }
        }
        if (!string.IsNullOrWhiteSpace(output))
            await File.WriteAllTextAsync(Path.Combine(output, "card-measurements.json"), JsonSerializer.Serialize(measurements, JsonOptions));
    }

    private static SystemInventorySnapshot Inventory() => new([
        new("InventoryGroupOperatingSystem", [new("os", [new("InventoryOperatingSystem", "Synthetic Windows 11"), new("InventoryOsVersion", "Synthetic NT")])]),
        new("InventoryGroupProcessorMemory", [new("cpu", [new("InventoryProcessor", "Synthetic processor"), new("InventoryMemoryGiB", "32")])]),
        new("InventoryGroupGraphics", [new("gpu", [new("InventoryGraphicsName", "Synthetic GPU")])]),
        new("InventoryGroupFirmware", [new("bios", [new("InventoryBiosVendor", "Synthetic firmware")])]),
        Devices("InventoryGroupStorage", "InventoryStorageModel"), Devices("InventoryGroupDrivers", "InventoryDriverName")]);

    private static InventoryGroup Devices(string group, string field) => new(group, Enumerable.Range(0, 4)
        .Select(index => new InventoryDevice("synthetic-" + index, [new(field, "Synthetic device " + index +
            " — Beispiel mit langem lokalisiertem Wert / длинное значение устройства / długi opis urządzenia"),
            new("InventoryDriverVersion", "Synthetic 1.2.3"), new("InventoryDriverProvider", "Synthetic provider")])).ToArray());

    private static void Save(MainWindow window, string? output, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Inventory frame missing.");
        VisualRenderGeometry.AssertFrameMatches(window, frame, (int)window.Width, 1100, name);
        if (!string.IsNullOrWhiteSpace(output)) frame.Save(Path.Combine(output, name + ".png"), new PngBitmapEncoderOptions());
    }
}
