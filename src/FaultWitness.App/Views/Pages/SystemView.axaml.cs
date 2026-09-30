using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Components;

namespace FaultWitness.App.Views.Pages;

public sealed partial class SystemView : UserControl
{
    public event Action? RefreshRequested;
    public event Action? InformationRequested;
    public event Action? ReadinessRequested;

    public SystemView()
    {
        AvaloniaXamlLoader.Load(this);
        var navigation = this.FindControl<SystemSubnavigation>("SystemNavigation")!;
        navigation.InformationRequested += () => InformationRequested?.Invoke();
        navigation.ReadinessRequested += () => ReadinessRequested?.Invoke();
    }

    public void Refresh(SystemPresentation presentation, MainViewModel source, string layoutClass, Control captureContent)
    {
        var scroll = this.FindControl<ScrollViewer>("SystemScroll")!;
        var offset = scroll.Offset;
        DataContext = presentation;
        presentation.Refresh(source, layoutClass);
        this.FindControl<SystemSubnavigation>("SystemNavigation")!.DataContext = presentation.Text;
        this.FindControl<SystemSubnavigation>("SystemNavigation")!.Refresh(presentation.Text, source.Page == AppPage.Readiness);
        this.FindControl<ContentControl>("CaptureHost")!.Content = captureContent;
        scroll.Offset = offset;
    }

    private void RefreshInventory(object? sender, RoutedEventArgs args) => RefreshRequested?.Invoke();
}
