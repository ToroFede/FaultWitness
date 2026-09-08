using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FaultWitness.App;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Headless")]
public sealed class SystemLayoutTests
{
    [AvaloniaTheory]
    [InlineData(640, false)]
    [InlineData(1000, false)]
    [InlineData(1280, true)]
    public void Inventory_FollowsResponsiveScreenContract(double width, bool twoColumns)
    {
        using var vm = new MainViewModel(new TestServices
        {
            Inventory = new Dictionary<string, string>
            {
                ["ProcessorCount"] = "8", ["Architecture"] = "X64",
                ["OperatingSystem"] = "Synthetic Windows", ["NtVersion"] = "Synthetic NT"
            }
        });
        var window = new MainWindow(vm) { Width = width };
        window.Show();
        try
        {
            vm.Navigate(AppPage.System); window.UpdateLayout();
            var inventory = window.GetVisualDescendants().OfType<Grid>().Single(item => item.Name == "SystemInventory");
            Assert.Equal(3, inventory.Children.Count);
            Assert.Contains(inventory.Children[0].GetVisualDescendants().OfType<TextBlock>(), item => item.Text == "Synthetic Windows");
            var first = inventory.Children[0].Bounds;
            var second = inventory.Children[1].Bounds;
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
            Assert.All(inventory.Children, child => Assert.True(child.Bounds.Right <= inventory.Bounds.Width + 1));
        }
        finally { window.Close(); }
    }
}
