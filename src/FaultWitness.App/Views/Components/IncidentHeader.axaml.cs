using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FaultWitness.App.Views.Components;

public sealed partial class IncidentHeader : UserControl
{
    public IncidentHeader() => AvaloniaXamlLoader.Load(this);
}
