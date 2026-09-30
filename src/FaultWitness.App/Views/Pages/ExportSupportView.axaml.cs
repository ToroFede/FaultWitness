using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FaultWitness.App.Presentation;

namespace FaultWitness.App.Views.Pages;

public sealed partial class ExportSupportView : UserControl
{
    private static readonly string[] SelectorNames = ["ExportFormat", "ExportScope"];
    private bool synchronizing;
    public event Action<int, int>? OptionsChanged;
    public event Action? CopyRequested;
    public event Action? SaveRequested;

    public ExportSupportView() => AvaloniaXamlLoader.Load(this);

    public void Refresh(ExportSupportPresentation presentation)
    {
        var focused = SelectorNames.Select(name => this.FindControl<ComboBox>(name)!)
            .FirstOrDefault(control => control.IsKeyboardFocusWithin);
        synchronizing = true;
        DataContext = presentation;
        SetOptions("ExportFormat", presentation.Formats, presentation.FormatIndex);
        SetOptions("ExportScope", presentation.Scopes, presentation.ScopeIndex);
        synchronizing = false;
        focused?.Focus();
    }

    private void SetOptions(string name, IReadOnlyList<string> options, int selectedIndex)
    {
        var combo = this.FindControl<ComboBox>(name)!;
        combo.ItemsSource = options;
        combo.SelectedIndex = selectedIndex;
    }

    private void FormatChanged(object? sender, SelectionChangedEventArgs args) => RaiseOptionsChanged();
    private void ScopeChanged(object? sender, SelectionChangedEventArgs args) => RaiseOptionsChanged();

    private void RaiseOptionsChanged()
    {
        if (!synchronizing && this.FindControl<ComboBox>("ExportFormat") is { SelectedIndex: >= 0 } format &&
            this.FindControl<ComboBox>("ExportScope") is { SelectedIndex: >= 0 } scope)
            OptionsChanged?.Invoke(format.SelectedIndex, scope.SelectedIndex);
    }

    private void Copy(object? sender, RoutedEventArgs args) => CopyRequested?.Invoke();
    private void Save(object? sender, RoutedEventArgs args) => SaveRequested?.Invoke();
}
