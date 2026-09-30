using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using FaultWitness.App.Presentation;
using FaultWitness.Core;

namespace FaultWitness.App.Views.Pages;

public sealed partial class HomeView : UserControl
{
    private bool refreshing;
    public event Action? RecentAnalysisRequested;
    public event Action<AnalysisMode>? AnalysisModeRequested;
    public event Action<AttentionLevel>? PriorityRequested;
    public event Action? ViewAllRequested;
    public event Action? HistoryRequested;
    public event Action? ExportRequested;
    public event Action<IncidentRow>? IncidentRequested;

    public HomeView() => AvaloniaXamlLoader.Load(this);

    public void Refresh(HomePresentation presentation, string layoutClass)
    {
        refreshing = true;
        if (!ReferenceEquals(DataContext, presentation)) DataContext = presentation;
        presentation.Refresh();
        SetLayout(layoutClass);
        refreshing = false;
    }

    public void SetLayout(string layoutClass)
    {
        var summary = this.FindControl<WrapPanel>("HomeSummary")!;
        var compact = layoutClass == "small";
        summary.Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
        summary.Classes.Set("summary-stacked", compact);
        summary.Classes.Set("summary-strip", !compact);
    }

    private void AnalyzeRecent(object? sender, RoutedEventArgs args) => RecentAnalysisRequested?.Invoke();
    private void AnalyzeAround(object? sender, RoutedEventArgs args) => AnalysisModeRequested?.Invoke(AnalysisMode.Around);
    private void AnalyzeFiles(object? sender, RoutedEventArgs args) => AnalysisModeRequested?.Invoke(AnalysisMode.Files);
    private void ShowAttention(object? sender, RoutedEventArgs args) => PriorityRequested?.Invoke(AttentionLevel.Attention);
    private void ShowKnowing(object? sender, RoutedEventArgs args) => PriorityRequested?.Invoke(AttentionLevel.Knowing);
    private void ShowBackground(object? sender, RoutedEventArgs args) => PriorityRequested?.Invoke(AttentionLevel.Background);
    private void ViewAll(object? sender, RoutedEventArgs args) => ViewAllRequested?.Invoke();
    private void OpenHistoryClick(object? sender, RoutedEventArgs args) => HistoryRequested?.Invoke();
    private void OpenExport(object? sender, RoutedEventArgs args) => ExportRequested?.Invoke();

    private void IncidentSelected(object? sender, SelectionChangedEventArgs args)
    {
        if (!refreshing && sender is ListBox { SelectedItem: IncidentRow row }) IncidentRequested?.Invoke(row);
    }
}
