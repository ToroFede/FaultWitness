using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FaultWitness.Design;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "BetaUxPolish")]
public sealed class BetaInteractionTests
{
    [AvaloniaTheory]
    [InlineData("light", "primary-action")]
    [InlineData("dark", "primary-action")]
    [InlineData("light", "secondary-action")]
    [InlineData("dark", "secondary-action")]
    [InlineData("light", "subtle-action")]
    [InlineData("dark", "subtle-action")]
    [InlineData("light", "danger-action")]
    [InlineData("dark", "danger-action")]
    [InlineData("light", "state-change-action")]
    [InlineData("dark", "state-change-action")]
    public void SharedActions_KeepContrastFocusAndDisabledGuard(string theme, string action)
    {
        var button = new Button { Content = "Review action", Margin = new Thickness(24), Width = 200, Height = 48 };
        button.Classes.Add(action);
        var invoked = 0;
        button.Click += (_, _) => invoked++;
        var window = new Window { Width = 400, Height = 200, Content = button, RequestedThemeVariant = theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show(); window.UpdateLayout();
        try
        {
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single(item => item.Name == "PART_ContentPresenter");
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            Assert.True(button.Focus(NavigationMethod.Tab));
            Assert.True(button.IsKeyboardFocusWithin);
            window.MouseMove(point); window.UpdateLayout();
            AssertColor(presenter.Background, theme, action == "primary-action" ? "accentHover" : "surfaceMuted");
            window.MouseDown(point, MouseButton.Left); window.UpdateLayout();
            AssertColor(presenter.Background, theme, action == "primary-action" ? "accentPressed" : "surfaceMuted");
            window.MouseUp(point, MouseButton.Left); window.UpdateLayout();
            Assert.Equal(1, invoked);
            button.IsEnabled = false; window.UpdateLayout();
            AssertColor(presenter.Foreground, theme, "textMuted");
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.Equal(1, invoked);
            Assert.False(button.IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    }

    private static void AssertColor(IBrush? brush, string theme, string token)
    {
        var actual = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        Assert.Equal(Color.Parse(DesignTokenCatalog.LoadDefault().Text($"semantic.{theme}.color.{token}")), actual.Color);
    }
}
