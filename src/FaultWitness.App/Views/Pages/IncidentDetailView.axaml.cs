using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace FaultWitness.App.Views.Pages;

public sealed partial class IncidentDetailView : UserControl
{
    public event EventHandler? BackRequested;
    public event EventHandler? SupportRequested;
    public event EventHandler? OccurrencesRequested;
    public IncidentDetailView() => AvaloniaXamlLoader.Load(this);
    private void Back(object? sender, RoutedEventArgs args) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void Copy(object? sender, RoutedEventArgs args) => SupportRequested?.Invoke(this, EventArgs.Empty);
    private void Occurrences(object? sender, RoutedEventArgs args) => OccurrencesRequested?.Invoke(this, EventArgs.Empty);
}
