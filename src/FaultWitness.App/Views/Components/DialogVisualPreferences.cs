using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace FaultWitness.App.Views.Components;

internal static class DialogVisualPreferences
{
    public static void Apply(Window dialog, Window owner)
    {
        dialog.RequestedThemeVariant = owner.RequestedThemeVariant;
        dialog.Classes.Set("motion-enabled", owner.Classes.Contains("motion-enabled"));
        dialog.Classes.Set("reduced-motion", owner.Classes.Contains("reduced-motion"));
        var width = owner.Bounds.Width > 0 ? owner.Bounds.Width : owner.Width;
        dialog.Width = Math.Clamp(width - 32, 280, dialog.Width);
        var gutter = (Thickness)dialog.FindResource("UiSurfacePadding")!;
        void ConstrainActions()
        {
            var available = (dialog.ClientSize.Width > 0 ? dialog.ClientSize.Width : dialog.Width) - gutter.Left - gutter.Right;
            foreach (var button in dialog.GetLogicalDescendants().OfType<Button>())
                button.MaxWidth = Math.Max(1, available - button.Margin.Left - button.Margin.Right);
        }
        ConstrainActions();
        dialog.SizeChanged += (_, _) => ConstrainActions();
    }
}
