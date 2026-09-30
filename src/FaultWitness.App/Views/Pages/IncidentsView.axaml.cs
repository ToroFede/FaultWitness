using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FaultWitness.App.Presentation;
using FaultWitness.Core;

namespace FaultWitness.App.Views.Pages;

public sealed partial class IncidentsView : UserControl
{
    private bool synchronizing;
    public event Action<IncidentFilter>? FilterRequested;
    public event Action? ResetRequested;
    public event Action<IncidentRow>? IncidentRequested;

    public IncidentsView() => AvaloniaXamlLoader.Load(this);

    public void Refresh(IncidentListPresentation presentation)
    {
        synchronizing = true;
        if (!ReferenceEquals(DataContext, presentation)) DataContext = presentation;
        presentation.Refresh();
        SetIndex("PriorityFilter", presentation.PriorityIndex);
        SetIndex("CategoryFilter", presentation.CategoryIndex);
        SetIndex("StrengthFilter", presentation.StrengthIndex);
        var search = this.FindControl<TextBox>("IncidentSearch")!;
        if (search.Text != presentation.SearchText) search.Text = presentation.SearchText;
        var from = this.FindControl<DatePicker>("FilterFrom")!;
        if (from.SelectedDate != presentation.FromDate) from.SelectedDate = presentation.FromDate;
        var to = this.FindControl<DatePicker>("FilterTo")!;
        if (to.SelectedDate != presentation.ToDate) to.SelectedDate = presentation.ToDate;
        synchronizing = false;
    }

    private void SetIndex(string name, int index)
    {
        var combo = this.FindControl<ComboBox>(name)!;
        if (combo.SelectedIndex != index) combo.SelectedIndex = index;
    }

    private void PriorityChanged(object? sender, SelectionChangedEventArgs args) => ApplyFilter();
    private void CategoryChanged(object? sender, SelectionChangedEventArgs args) => ApplyFilter();
    private void StrengthChanged(object? sender, SelectionChangedEventArgs args) => ApplyFilter();
    private void SearchChanged(object? sender, TextChangedEventArgs args) => ApplyFilter();
    private void FilterDateChanged(object? sender, DatePickerSelectedValueChangedEventArgs args) => ApplyFilter();

    private void ApplyFilter()
    {
        if (synchronizing) return;
        var priority = this.FindControl<ComboBox>("PriorityFilter")!;
        var category = this.FindControl<ComboBox>("CategoryFilter")!;
        var strength = this.FindControl<ComboBox>("StrengthFilter")!;
        var search = this.FindControl<TextBox>("IncidentSearch")!;
        var from = this.FindControl<DatePicker>("FilterFrom")!;
        var to = this.FindControl<DatePicker>("FilterTo")!;
        FilterRequested?.Invoke(new IncidentFilter(
            Priority: priority.SelectedIndex == 3 ? null : priority.SelectedIndex >= 0 ? (AttentionLevel)priority.SelectedIndex : null,
            Category: category.SelectedIndex <= 0 ? null : (IncidentCategory)(category.SelectedIndex - 1),
            Strength: strength.SelectedIndex <= 0 ? null : (EvidenceStrength)(strength.SelectedIndex - 1),
            Search: search.Text ?? string.Empty,
            From: from.SelectedDate is { } fromDate ? DateTimeInput.Combine(fromDate, TimeSpan.Zero).ToUniversalTime() : null,
            To: to.SelectedDate is { } toDate ? DateTimeInput.Combine(toDate, new TimeSpan(23, 59, 59)).ToUniversalTime() : null));
    }

    private void Reset(object? sender, RoutedEventArgs args) => ResetRequested?.Invoke();

    private void IncidentSelected(object? sender, SelectionChangedEventArgs args)
    {
        if (!synchronizing && sender is ListBox { SelectedItem: IncidentRow row }) IncidentRequested?.Invoke(row);
    }
}
