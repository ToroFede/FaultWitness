using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using FaultWitness.Design;

namespace FaultWitness.App;

public sealed class Application : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        var tokens = DesignTokenCatalog.LoadDefault();
        Resources.ThemeDictionaries[ThemeVariant.Light] = Palette(tokens, "light");
        Resources.ThemeDictionaries[ThemeVariant.Dark] = Palette(tokens, "dark");
        foreach (var key in tokens.Keys.Where(key => key.StartsWith("primitive.space.", StringComparison.Ordinal) || key.StartsWith("primitive.radius.", StringComparison.Ordinal) || key.StartsWith("primitive.fontSize.", StringComparison.Ordinal) || key.StartsWith("component.", StringComparison.Ordinal)))
            Resources[key] = tokens.Resolve(key);
        Resources["UiSurfacePadding"] = new Thickness(tokens.Number("primitive.space.4"));
        Resources["UiSurfaceRadius"] = new CornerRadius(tokens.Number("primitive.radius.small"));
        Resources["UiActionRadius"] = new CornerRadius(tokens.Number("component.action.primary.radius"));
        Resources["UiButtonPadding"] = new Thickness(tokens.Number("primitive.space.3"), tokens.Number("primitive.space.2"));
        Resources["UiNavPadding"] = new Thickness(tokens.Number("primitive.space.2"));
        Resources["UiCommandMargin"] = new Thickness(0, 0, tokens.Number("primitive.space.3"), tokens.Number("primitive.space.2"));
        Resources["UiGroupMargin"] = new Thickness(0, tokens.Number("primitive.space.2"), 0, 0);
        Resources["UiNavMargin"] = new Thickness(tokens.Number("primitive.space.3"), tokens.Number("primitive.space.6"), tokens.Number("primitive.space.3"), tokens.Number("primitive.space.4"));
        Resources["UiDestinationsMargin"] = new Thickness(0, tokens.Number("primitive.space.8"), 0, 0);
        Resources["UiStatusMargin"] = new Thickness(0, tokens.Number("primitive.space.4"), 0, 0);
        Styles.Add(new StyleInclude(new Uri("avares://FaultWitness/")) { Source = new Uri("avares://FaultWitness/Styles/SharedStyles.axaml") });
    }

    private static ResourceDictionary Palette(DesignTokenCatalog tokens, string theme)
    {
        SolidColorBrush Brush(string name) => SolidColorBrush.Parse(tokens.Text($"semantic.{theme}.color.{name}"));
        return new ResourceDictionary
        {
            ["AppBackground"] = Brush("canvas"), ["AppSurface"] = Brush("surface"), ["AppSurfaceMuted"] = Brush("surfaceMuted"),
            ["AppText"] = Brush("text"), ["AppMuted"] = Brush("textMuted"), ["AppBorder"] = Brush("border"),
            ["AppAccent"] = Brush("accent"), ["AppAccentHover"] = Brush("accentHover"), ["AppAccentPressed"] = Brush("accentPressed"), ["AppAttention"] = Brush("attention"), ["AppError"] = Brush("error"),
            ["AppReady"] = Brush("ready"), ["AppContext"] = Brush("context"), ["AppUnknown"] = Brush("unknown"), ["AppSelection"] = Brush("selection")
        };
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
