using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using FaultWitness.App.Presentation;
using FaultWitness.Core;

namespace FaultWitness.App.Views.Pages;

public sealed partial class AnalyzeView : UserControl
{
    private bool synchronizing;
    public event Action? BrowseRequested;
    public event Action? ImportAnalysisRequested;
    public event Action<IEnumerable<string>>? ImportsDropped;
    public event Action<AnalyzeRunRequest>? RunRequested;
    public event Action? CancelRequested;

    public AnalyzeView()
    {
        AvaloniaXamlLoader.Load(this);
        var drop = this.FindControl<Border>("ImportDropSurface")!;
        DragDrop.SetAllowDrop(drop, true);
        drop.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        drop.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public void Refresh(AnalyzePresentation presentation)
    {
        synchronizing = true;
        if (!ReferenceEquals(DataContext, presentation)) DataContext = presentation;
        presentation.Refresh();
        var mode = this.FindControl<ComboBox>("AnalysisMode")!;
        var period = this.FindControl<ComboBox>("PeriodSelector")!;
        var window = this.FindControl<ComboBox>("AroundWindow")!;
        if (mode.SelectedIndex != presentation.ModeIndex) mode.SelectedIndex = presentation.ModeIndex;
        if (period.SelectedIndex != presentation.PeriodIndex) period.SelectedIndex = presentation.PeriodIndex;
        if (window.SelectedIndex != presentation.WindowIndex) window.SelectedIndex = presentation.WindowIndex;
        synchronizing = false;
    }

    private void ModeChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!synchronizing && DataContext is AnalyzePresentation presentation && sender is ComboBox { SelectedIndex: >= 0 } combo)
            presentation.SelectMode(combo.SelectedIndex);
    }

    private void PeriodChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!synchronizing && DataContext is AnalyzePresentation presentation && sender is ComboBox { SelectedIndex: >= 0 } combo)
            presentation.SelectPeriod(combo.SelectedIndex);
    }

    private void RunRecent(object? sender, RoutedEventArgs args) => RaiseRun(false);
    private void Cancel(object? sender, RoutedEventArgs args) => CancelRequested?.Invoke();
    private void RunAround(object? sender, RoutedEventArgs args) => RaiseRun(true);
    private void Browse(object? sender, RoutedEventArgs args) => BrowseRequested?.Invoke();
    private void ImportAnalyze(object? sender, RoutedEventArgs args) => ImportAnalysisRequested?.Invoke();

    private void RaiseRun(bool around)
    {
        if (DataContext is not AnalyzePresentation state) return;
        RunRequested?.Invoke(new AnalyzeRunRequest(around, state.CustomFromDate, state.CustomToDate,
            state.AroundDate, state.AroundClock, state.WindowMinutes));
    }

    private void OnDragOver(object? sender, DragEventArgs args)
    {
        args.DragEffects = DataContext is AnalyzePresentation { IsBusy: true } ? DragDropEffects.None : DragDropEffects.Copy;
        args.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs args)
    {
        var paths = args.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>() ?? [];
        ImportsDropped?.Invoke(paths);
        args.Handled = true;
    }
}
