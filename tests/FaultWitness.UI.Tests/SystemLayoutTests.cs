using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Headless")]
public sealed class SystemLayoutTests
{
    [AvaloniaTheory]
    [InlineData(560, false)]
    [InlineData(600, false)]
    [InlineData(640, false)]
    [InlineData(641, false)]
    [InlineData(1008, true)]
    [InlineData(1280, true)]
    [InlineData(1920, true)]
    public void Inventory_FollowsResponsiveScreenContract(double width, bool twoColumns)
    {
        using var vm = new MainViewModel(new TestServices
        {
            StructuredInventory = new SystemInventorySnapshot([
                new InventoryGroup("InventoryGroupOperatingSystem", [new InventoryDevice("os", [new InventoryField("InventoryOperatingSystem", "Synthetic Windows"), new InventoryField("InventoryOsVersion", "Synthetic NT")])]),
                new InventoryGroup("InventoryGroupProcessorMemory", [new InventoryDevice("cpu", [new InventoryField("InventoryProcessorLogical", "8"), new InventoryField("InventoryArchitecture", "X64")])]),
                new InventoryGroup("InventoryGroupGraphics", [])])
        });
        var window = new MainWindow(vm) { Width = width };
        window.Show();
        try
        {
            vm.Navigate(AppPage.System); window.UpdateLayout();
            var inventory = window.GetVisualDescendants().OfType<ItemsControl>().Single(item => item.Name == "SystemInventory");
            var layout = inventory.GetVisualDescendants().OfType<UniformGrid>().Single(item => item.Name == "SystemInventoryLayout");
            Assert.Equal(twoColumns ? 2 : 1, layout.Columns);
            Assert.Equal(3, layout.Children.Count);
            Assert.Contains(layout.Children[0].GetVisualDescendants().OfType<TextBlock>(), item => item.Text!.Contains("Synthetic Windows", StringComparison.Ordinal));
            var first = layout.Children[0].Bounds;
            var second = layout.Children[1].Bounds;
            if (twoColumns)
            {
                Assert.Equal(first.Y, second.Y);
                Assert.True(second.X >= first.Right);
                Assert.Equal(first.Width, second.Width, 1);
            }
            else
            {
                Assert.Equal(first.X, second.X);
                Assert.True(second.Y >= first.Bottom);
            }
            Assert.All(layout.Children, child => Assert.True(child.Bounds.Right <= layout.Bounds.Right + 1));
        }
        finally { window.Close(); }
    }
}
