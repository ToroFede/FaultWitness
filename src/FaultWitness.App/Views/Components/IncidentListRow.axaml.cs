using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FaultWitness.App.Views.Components;

public sealed partial class IncidentListRow : UserControl
{
    public IncidentListRow() => AvaloniaXamlLoader.Load(this);
}
