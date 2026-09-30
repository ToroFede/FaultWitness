using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FaultWitness.App.Presentation;
namespace FaultWitness.App.Views.Components;
public sealed partial class CaptureRestoreCard : UserControl
{
    public event EventHandler? RestoreRequested;
    public CaptureRestoreCard() => AvaloniaXamlLoader.Load(this);
    private void Restore(object? sender, RoutedEventArgs args)
    {
        if (DataContext is CaptureJournalPresentation { CanRestore: true, CanInvoke: true }) RestoreRequested?.Invoke(this, EventArgs.Empty);
    }
}
