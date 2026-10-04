using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Components;

namespace FaultWitness.App.Views.Pages;

public sealed partial class AnalyzeCaptureView : UserControl
{
    public event Action? AnalyzeRequested;

    public AnalyzeCaptureView()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<AnalyzeSubnavigation>("AnalyzeNavigation")!.AnalyzeRequested += () => AnalyzeRequested?.Invoke();
    }

    public void Refresh(LocalizedLabels text, CaptureView capture)
    {
        DataContext = text;
        this.FindControl<AnalyzeSubnavigation>("AnalyzeNavigation")!.Refresh(text, true);
        var host = this.FindControl<ContentControl>("CaptureHost")!;
        if (!ReferenceEquals(host.Content, capture)) host.Content = capture;
    }
}
