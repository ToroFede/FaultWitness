using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml.MarkupExtensions;
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
        var windowStyle = new Style(selector => selector.OfType<Window>());
        windowStyle.Setters.Add(new Setter(Window.BackgroundProperty, new DynamicResourceExtension("AppBackground")));
        windowStyle.Setters.Add(new Setter(Window.ForegroundProperty, new DynamicResourceExtension("AppText")));
        Styles.Add(windowStyle);
        var primary = new Style(selector => selector.OfType<Button>().Class("primary-action"));
        primary.Setters.Add(new Setter(Button.BackgroundProperty, new DynamicResourceExtension("AppAccent")));
        primary.Setters.Add(new Setter(Button.ForegroundProperty, new DynamicResourceExtension("AppSurface")));
        primary.Setters.Add(new Setter(Button.FontWeightProperty, FontWeight.SemiBold)); Styles.Add(primary);
        var subtle = new Style(selector => selector.OfType<Button>().Class("subtle-action"));
        subtle.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        subtle.Setters.Add(new Setter(Button.ForegroundProperty, new DynamicResourceExtension("AppAccent"))); Styles.Add(subtle);
        var danger = new Style(selector => selector.OfType<Button>().Class("danger-action"));
        danger.Setters.Add(new Setter(Button.ForegroundProperty, new DynamicResourceExtension("AppError")));
        danger.Setters.Add(new Setter(Button.BorderBrushProperty, new DynamicResourceExtension("AppError"))); Styles.Add(danger);
        var navigation = new Style(selector => selector.OfType<Button>().Class("navigation-item"));
        navigation.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        navigation.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
        navigation.Setters.Add(new Setter(Button.ForegroundProperty, new DynamicResourceExtension("AppText"))); Styles.Add(navigation);
        var selectedNavigation = new Style(selector => selector.OfType<Button>().Class("navigation-item").Class("selected"));
        selectedNavigation.Setters.Add(new Setter(Button.BackgroundProperty, new DynamicResourceExtension("AppSelection"))); Styles.Add(selectedNavigation);
        foreach (var state in new[] { ":pointerover", ":pressed" })
        {
            var interaction = new Style(selector => selector.OfType<Button>().Class("navigation-item").Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            interaction.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, new DynamicResourceExtension("AppSelection")));
            interaction.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, new DynamicResourceExtension("AppText"))); Styles.Add(interaction);
        }
        var metric = new Style(selector => selector.OfType<Button>().Class("summary-metric"));
        metric.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        metric.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
        metric.Setters.Add(new Setter(Button.ForegroundProperty, new DynamicResourceExtension("AppText"))); Styles.Add(metric);
        var historyItem = new Style(selector => selector.OfType<ListBox>().Class("history-list").Descendant().OfType<ListBoxItem>());
        historyItem.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(0)));
        historyItem.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Stretch)); Styles.Add(historyItem);
        var selectedHistory = new Style(selector => selector.OfType<ListBox>().Class("history-list").Descendant().OfType<ListBoxItem>().Class(":selected").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
        selectedHistory.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, new DynamicResourceExtension("AppSelection")));
        selectedHistory.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, new DynamicResourceExtension("AppText"))); Styles.Add(selectedHistory);
    }
    private static ResourceDictionary Palette(DesignTokenCatalog tokens, string theme)
    {
        SolidColorBrush Brush(string name) => SolidColorBrush.Parse(tokens.Text($"semantic.{theme}.color.{name}"));
        return new ResourceDictionary
        {
            ["AppBackground"] = Brush("canvas"), ["AppSurface"] = Brush("surface"), ["AppSurfaceMuted"] = Brush("surfaceMuted"),
            ["AppText"] = Brush("text"), ["AppMuted"] = Brush("textMuted"), ["AppBorder"] = Brush("border"),
            ["AppAccent"] = Brush("accent"), ["AppAttention"] = Brush("attention"), ["AppError"] = Brush("error"),
            ["AppReady"] = Brush("ready"), ["AppContext"] = Brush("context"), ["AppUnknown"] = Brush("unknown"), ["AppSelection"] = Brush("selection")
        };
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
