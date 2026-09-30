using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FaultWitness.App.Views.Components;

public sealed partial class EventRecordDetails : UserControl
{
    public EventRecordDetails() => AvaloniaXamlLoader.Load(this);
}
