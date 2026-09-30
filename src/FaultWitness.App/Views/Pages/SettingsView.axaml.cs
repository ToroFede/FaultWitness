using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FaultWitness.App.Presentation;

namespace FaultWitness.App.Views.Pages;

public sealed partial class SettingsView : UserControl
{
    private static readonly string[] SelectorNames = ["LanguageSelector", "ThemeSelector", "DefaultPeriod", "RetentionSelector"];
    private bool synchronizing;
    public event Action<string, int>? SettingChanged;
    public event Action? ClearDataRequested;

    public SettingsView() => AvaloniaXamlLoader.Load(this);

    public void Refresh(SettingsPresentation presentation)
    {
        var focused = SelectorNames.Select(name => this.FindControl<ComboBox>(name)!)
            .FirstOrDefault(control => control.IsKeyboardFocusWithin);
        synchronizing = true;
        DataContext = presentation;
        SetOptions("LanguageSelector", presentation.Languages, presentation.LanguageIndex);
        SetOptions("ThemeSelector", presentation.Themes, presentation.ThemeIndex);
        SetOptions("DefaultPeriod", presentation.Periods, presentation.PeriodIndex);
        SetOptions("RetentionSelector", presentation.RetentionOptions, presentation.RetentionIndex);
        synchronizing = false;
        focused?.Focus();
    }

    private void SetOptions(string name, IReadOnlyList<string> options, int selectedIndex)
    {
        var combo = this.FindControl<ComboBox>(name)!;
        combo.ItemsSource = options;
        combo.SelectedIndex = selectedIndex;
    }

    private void LanguageChanged(object? sender, SelectionChangedEventArgs args) => RaiseChange("LanguageSelector", "Language");
    private void ThemeChanged(object? sender, SelectionChangedEventArgs args) => RaiseChange("ThemeSelector", "Theme");
    private void PeriodChanged(object? sender, SelectionChangedEventArgs args) => RaiseChange("DefaultPeriod", "Period");
    private void RetentionChanged(object? sender, SelectionChangedEventArgs args) => RaiseChange("RetentionSelector", "Retention");

    private void RaiseChange(string controlName, string setting)
    {
        if (!synchronizing && DataContext is SettingsPresentation presentation && this.FindControl<ComboBox>(controlName) is { SelectedIndex: >= 0 } combo)
            SettingChanged?.Invoke(setting, combo.SelectedIndex);
    }

    private void ClearRequested(object? sender, RoutedEventArgs args) => ClearDataRequested?.Invoke();
}
