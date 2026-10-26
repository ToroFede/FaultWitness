using Avalonia;
using Avalonia.Controls;

namespace FaultWitness.App.Views.Components;

/// <summary>Stacks each column independently without reordering or reparenting its logical children.</summary>
public sealed class IndependentColumnsPanel : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty = AvaloniaProperty.Register<IndependentColumnsPanel, int>(
        nameof(Columns), 1, validate: value => value > 0);
    public static readonly StyledProperty<double> ColumnSpacingProperty = AvaloniaProperty.Register<IndependentColumnsPanel, double>(
        nameof(ColumnSpacing), 0, validate: value => double.IsFinite(value) && value >= 0);
    public static readonly StyledProperty<double> RowSpacingProperty = AvaloniaProperty.Register<IndependentColumnsPanel, double>(
        nameof(RowSpacing), 0, validate: value => double.IsFinite(value) && value >= 0);

    static IndependentColumnsPanel() => AffectsMeasure<IndependentColumnsPanel>(ColumnsProperty, ColumnSpacingProperty, RowSpacingProperty);

    public int Columns { get => GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }
    public double RowSpacing { get => GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }
    private int ActiveColumns => Math.Min(Columns, Math.Max(1, Children.Count));

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0) return default;
        var columns = ActiveColumns;
        var spacing = EffectiveSpacing(availableSize.Width, columns);
        var width = double.IsPositiveInfinity(availableSize.Width) ? double.PositiveInfinity :
            Math.Max(0, (availableSize.Width - spacing * (columns - 1)) / columns);
        var heights = new double[columns];
        var counts = new int[columns];
        var desiredWidth = 0d;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(width, double.PositiveInfinity));
            if (!child.IsVisible) continue;
            var column = i % columns;
            heights[column] += (counts[column]++ > 0 ? RowSpacing : 0) + child.DesiredSize.Height;
            desiredWidth = Math.Max(desiredWidth, child.DesiredSize.Width);
        }
        return new Size(double.IsPositiveInfinity(availableSize.Width) ? desiredWidth * columns + spacing * (columns - 1) : availableSize.Width,
            heights.Max());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ActiveColumns;
        var spacing = EffectiveSpacing(finalSize.Width, columns);
        var width = Math.Max(0, (finalSize.Width - spacing * (columns - 1)) / columns);
        var offsets = new double[columns];
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (!child.IsVisible) continue;
            var column = i % columns;
            child.Arrange(new Rect(column * (width + spacing), offsets[column], width, child.DesiredSize.Height));
            offsets[column] += child.DesiredSize.Height + RowSpacing;
        }
        return finalSize;
    }

    private double EffectiveSpacing(double width, int columns) => columns == 1 ? 0 : Math.Min(ColumnSpacing, width / (columns - 1));
}
