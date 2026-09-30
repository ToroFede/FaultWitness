using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FaultWitness.App.Views.Components;

public sealed partial class CoverageSummary : UserControl
{
    public CoverageSummary() => AvaloniaXamlLoader.Load(this);
}
