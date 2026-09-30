using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FaultWitness.App.Presentation;

namespace FaultWitness.App.Views.Components;

public sealed partial class SystemSubnavigation : UserControl
{
    public event Action? InformationRequested;
    public event Action? ReadinessRequested;

    public SystemSubnavigation() => AvaloniaXamlLoader.Load(this);

    public void Refresh(LocalizedLabels text, bool readinessSelected)
    {
        SetDestination("SystemInformation", text["SystemInformation"], !readinessSelected);
        SetDestination("SystemReadiness", text["Readiness"], readinessSelected);
    }

    private void SetDestination(string name, string label, bool selected)
    {
        var button = this.FindControl<Button>(name + "Tab")!;
        button.Classes.Set("selected", selected);
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetHelpText(button, selected ? this.FindResourceLabel("NavigationCurrent") : string.Empty);
        var text = this.FindControl<TextBlock>(name + "Label")!;
        text.Text = label;
        text.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
        this.FindControl<Border>(name + "Indicator")!.Opacity = selected ? 1 : 0;
    }

    private string FindResourceLabel(string key) => DataContext is LocalizedLabels text ? text[key] : key;
    private void ShowInformation(object? sender, RoutedEventArgs args) => InformationRequested?.Invoke();
    private void ShowReadiness(object? sender, RoutedEventArgs args) => ReadinessRequested?.Invoke();
}
