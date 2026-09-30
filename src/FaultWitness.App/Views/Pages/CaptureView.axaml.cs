using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FaultWitness.App.Presentation;
namespace FaultWitness.App.Views.Pages;
public sealed partial class CaptureView : UserControl
{
    private bool refreshing;
    public event Action<string>? TargetRequested;
    public event Action<bool>? ArchitectureRequested;
    public event Action? PreviewRequested;
    public event Action? RefreshRequested;
    public event Action? ConfigureRequested;
    public event Action<Guid>? RestoreRequested;
    public event Action? MoreRequested;
    public CaptureView()
    {
        AvaloniaXamlLoader.Load(this);
        var input = this.FindControl<TextBox>("CaptureExecutable")!;
        // TextChanged is deferred; observe the property synchronously so navigation cannot overwrite a pending edit.
        input.PropertyChanged += (_, args) =>
        {
            if (!refreshing && args.Property == TextBox.TextProperty) TargetRequested?.Invoke(input.Text ?? string.Empty);
        };
    }
    public void Refresh(CapturePresentation presentation, MainViewModel source, bool architectureConfirmed, int visibleCount)
    {
        refreshing = true;
        try
        {
            if (!ReferenceEquals(DataContext, presentation)) DataContext = presentation;
            presentation.Refresh(source, architectureConfirmed, visibleCount);
            var input = this.FindControl<TextBox>("CaptureExecutable")!;
            if (input.Text != presentation.Target) input.Text = presentation.Target;
            this.FindControl<CheckBox>("CaptureArchitectureConfirmation")!.IsChecked = architectureConfirmed;
        }
        finally { refreshing = false; }
    }
    private void ArchitectureChanged(object? sender, RoutedEventArgs args) { if (!refreshing && sender is CheckBox box) ArchitectureRequested?.Invoke(box.IsChecked == true); }
    private void Preview(object? sender, RoutedEventArgs args) { if (DataContext is CapturePresentation { CanRead: true }) PreviewRequested?.Invoke(); }
    private void RefreshState(object? sender, RoutedEventArgs args) { if (DataContext is CapturePresentation { CanRead: true }) RefreshRequested?.Invoke(); }
    private void Configure(object? sender, RoutedEventArgs args) => ConfigureRequested?.Invoke();
    private void ShowMore(object? sender, RoutedEventArgs args) => MoreRequested?.Invoke();
    private void RestoreEntry(object? sender, EventArgs args) { if (sender is Control { DataContext: CaptureJournalPresentation row }) RestoreRequested?.Invoke(row.Source.ActionId); }
}
