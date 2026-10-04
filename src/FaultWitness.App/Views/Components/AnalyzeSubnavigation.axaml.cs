using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FaultWitness.App.Presentation;

namespace FaultWitness.App.Views.Components;

public sealed partial class AnalyzeSubnavigation : UserControl
{
    public event Action? AnalyzeRequested;
    public event Action? CaptureRequested;

    public AnalyzeSubnavigation() => AvaloniaXamlLoader.Load(this);

    public void Refresh(LocalizedLabels text, bool captureSelected)
    {
        SetDestination("AnalyzeWorkflow", text["Analyze"], !captureSelected, text["NavigationCurrent"]);
        SetDestination("AnalyzeCapture", text["CaptureTitle"], captureSelected, text["NavigationCurrent"]);
    }

    private void SetDestination(string name, string label, bool selected, string current)
    {
        var button = this.FindControl<Button>(name + "Tab")!;
        button.Classes.Set("selected", selected);
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetHelpText(button, selected ? current : string.Empty);
        var text = this.FindControl<TextBlock>(name + "Label")!;
        text.Text = label;
        text.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
        this.FindControl<Border>(name + "Indicator")!.Opacity = selected ? 1 : 0;
    }

    private void ShowAnalyze(object? sender, RoutedEventArgs args) => AnalyzeRequested?.Invoke();
    private void ShowCapture(object? sender, RoutedEventArgs args) => CaptureRequested?.Invoke();
}
