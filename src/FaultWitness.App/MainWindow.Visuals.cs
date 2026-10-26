using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using FaultWitness.App.Views.Pages;

namespace FaultWitness.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Avalonia Window has a Closed lifetime, not IDisposable; every animation token is canceled and disposed on replacement and Closed.")]
public sealed partial class MainWindow
{
    private CancellationTokenSource? pageAnimation;

    private void ApplyVisualPreferences()
    {
        Classes.Set("motion-enabled", !ViewModel.Settings.ReduceMotion);
        Classes.Set("reduced-motion", ViewModel.Settings.ReduceMotion);
        if (ViewModel.Settings.ReduceMotion) StopPageAnimation();
        TransparencyLevelHint = ViewModel.Settings.UseSystemBackdrop
            ? [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None] : [WindowTransparencyLevel.None];
        RefreshBackdrop();
    }

    private void RefreshBackdrop()
    {
        if (ViewModel is null || this.FindResource(ActualThemeVariant, "AppBackground") is not IBrush solid) return;
        TransparencyBackgroundFallback = solid;
        // Only native compositor materials admit translucency. No transparent-only window,
        // platform calls, whole-control opacity, or simulated backdrop when unsupported.
        var material = ViewModel.Settings.UseSystemBackdrop &&
            (ActualTransparencyLevel == WindowTransparencyLevel.Mica || ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur);
        Classes.Set("native-material", material);
        IBrush Surface(string key, string opacityKey)
        {
            var brush = (ISolidColorBrush)this.FindResource(ActualThemeVariant, key)!;
            return material ? new SolidColorBrush(brush.Color, D(opacityKey)) : brush;
        }
        Background = Surface("AppBackground", ActualThemeVariant == ThemeVariant.Light
            ? "component.redesign.lightCanvasOpacity" : "component.redesign.canvasOpacity");
        Resources["AppNavigation"] = Surface("AppSurface", "component.redesign.navigationOpacity");
        Resources["AppCard"] = Surface("AppSurface", "component.redesign.cardOpacity");
        Resources["AppHero"] = Surface("AppSurfaceMuted", "component.redesign.cardOpacity");
    }

    private void AnimateCurrentPage()
    {
        StopPageAnimation();
        if (ViewModel.Settings.ReduceMotion || pageHost.Content is not UserControl page ||
            !page.Classes.Contains("redesigned")) return;
        pageAnimation = new CancellationTokenSource();
        _ = AnimateEntranceAsync(page, pageAnimation.Token);
    }

    private static async Task AnimateEntranceAsync(Control page, CancellationToken cancellation)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(D("component.redesign.entranceMilliseconds")),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.72), new Setter(TranslateTransform.YProperty, D("primitive.space.1")) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } }
            }
        };
        try { await animation.RunAsync(page, cancellation).ConfigureAwait(true); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    private void StopPageAnimation()
    {
        pageAnimation?.Cancel();
        pageAnimation?.Dispose();
        pageAnimation = null;
    }
}
