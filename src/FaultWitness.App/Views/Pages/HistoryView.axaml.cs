using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FaultWitness.App.Presentation;
using FaultWitness.Core;

namespace FaultWitness.App.Views.Pages;

public sealed partial class HistoryView : UserControl
{
    private bool synchronizing;
    private string layout = string.Empty;
    public event Action? RefreshRequested;
    public event Action? CopyRequested;
    public event Action? SaveRequested;
    public event Action<HistoryRow>? SelectionRequested;

    public HistoryView() => AvaloniaXamlLoader.Load(this);

    public void Refresh(HistoryPresentation presentation, MainViewModel viewModel, bool refreshItems, string layoutClass)
    {
        var list = this.FindControl<ListBox>("HistoryList")!;
        var restoreFocus = list.IsKeyboardFocusWithin;
        synchronizing = true;
        if (!ReferenceEquals(DataContext, presentation)) DataContext = presentation;
        presentation.Refresh(viewModel, refreshItems);
        ApplyLayout(layoutClass);
        SynchronizeSelection(presentation);
        synchronizing = false;
        if (restoreFocus) list.Focus();
    }

    public void RefreshSelection(HistoryPresentation presentation, MainViewModel viewModel)
    {
        synchronizing = true;
        presentation.Refresh(viewModel, refreshItems: false);
        SynchronizeSelection(presentation);
        synchronizing = false;
    }

    private void SynchronizeSelection(HistoryPresentation presentation)
    {
        var list = this.FindControl<ListBox>("HistoryList")!;
        if (!ReferenceEquals(list.SelectedItem, presentation.Selected)) list.SelectedItem = presentation.Selected;
    }

    public void ApplyLayout(string layoutClass)
    {
        if (layout == layoutClass) return;
        layout = layoutClass;
        var large = layoutClass == "large";
        var grid = this.FindControl<Grid>("HistoryLayout")!;
        var heading = this.FindControl<StackPanel>("HistoryHeading")!;
        var list = this.FindControl<ListBox>("HistoryList")!;
        var detail = this.FindControl<ScrollViewer>("HistoryDetailScroll")!;
        var empty = this.FindControl<Border>("HistoryEmpty")!;
        grid.ColumnDefinitions = new ColumnDefinitions(large ? "2*,3*" : "*");
        grid.RowDefinitions = new RowDefinitions(large ? "Auto,*" : "Auto,Auto,*");
        Grid.SetColumnSpan(heading, large ? 2 : 1);
        Grid.SetRow(list, 1); Grid.SetColumn(list, 0);
        Grid.SetRow(detail, large ? 1 : 2); Grid.SetColumn(detail, large ? 1 : 0);
        Grid.SetRow(empty, 1); Grid.SetColumn(empty, 0);
        Grid.SetColumnSpan(empty, large ? 2 : 1); Grid.SetRowSpan(empty, large ? 1 : 2);
        list.MaxHeight = large ? double.PositiveInfinity : 340;
    }

    private void HistorySelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!synchronizing && sender is ListBox { SelectedItem: HistoryItemPresentation row })
            SelectionRequested?.Invoke(row.SourceRow);
    }

    private void ReloadHistory(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => RefreshRequested?.Invoke();
    private void Copy(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => CopyRequested?.Invoke();
    private void Save(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => SaveRequested?.Invoke();
}
