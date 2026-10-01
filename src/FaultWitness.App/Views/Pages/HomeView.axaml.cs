using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using FaultWitness.App.Presentation;
using FaultWitness.Core;

namespace FaultWitness.App.Views.Pages;

public sealed partial class HomeView : UserControl
{
    private bool refreshing;
    private string layoutClass = "large";
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
        this.layoutClass = layoutClass;
        ApplySummaryLayout();
    }

    private void SummarySizeChanged(object? sender, SizeChangedEventArgs args) => ApplySummaryLayout();

    private void ApplySummaryLayout()
    {
        var summary = this.FindControl<Grid>("HomeSummary")!;
        var buttons = summary.Children.OfType<Button>().ToArray();
        if (buttons.Length == 0) return;

        var width = summary.Bounds.Width;
        var columns = 1;
        if (width > 0)
        {
            foreach (var button in buttons) button.Measure(Size.Infinity);
            var requiredWidth = buttons.Sum(static button => button.DesiredSize.Width)
                + Math.Max(0, buttons.Length - 1) * summary.ColumnSpacing;
            if (requiredWidth <= width + 0.5) columns = buttons.Length;
        }
        else if (layoutClass != "small")
        {
            columns = buttons.Length;
        }

        summary.ColumnDefinitions = new ColumnDefinitions(columns == buttons.Length ? "Auto,Auto,Auto" : "*");
        summary.RowDefinitions = new RowDefinitions(columns == buttons.Length ? "Auto" : "Auto,Auto,Auto");
        for (var index = 0; index < buttons.Length; index++)
        {
            Grid.SetColumn(buttons[index], columns == buttons.Length ? index : 0);
            Grid.SetRow(buttons[index], columns == buttons.Length ? 0 : index);
            buttons[index].HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        }

        var compact = columns == 1;
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
