using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Components;

namespace FaultWitness.App.Views.Pages;

public sealed partial class DiagnosticReadinessView : UserControl
{
    public event Action? RefreshRequested;
    public event Action? InformationRequested;
    public event Action? ReadinessRequested;

    public DiagnosticReadinessView()
    {
        AvaloniaXamlLoader.Load(this);
        var navigation = this.FindControl<SystemSubnavigation>("SystemNavigation")!;
        navigation.InformationRequested += () => InformationRequested?.Invoke();
        navigation.ReadinessRequested += () => ReadinessRequested?.Invoke();
    }

    public void Refresh(ReadinessPresentation presentation, MainViewModel source)
    {
        var scroll = this.FindControl<ScrollViewer>("ReadinessScroll")!;
        var offset = scroll.Offset;
        DataContext = presentation;
        presentation.Refresh(source);
        this.FindControl<SystemSubnavigation>("SystemNavigation")!.DataContext = presentation.Text;
        this.FindControl<SystemSubnavigation>("SystemNavigation")!.Refresh(presentation.Text, readinessSelected: true);
        scroll.Offset = offset;
    }

    private void RefreshSources(object? sender, RoutedEventArgs args) => RefreshRequested?.Invoke();
}
