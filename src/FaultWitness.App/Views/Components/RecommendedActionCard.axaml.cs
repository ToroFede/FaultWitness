using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FaultWitness.App.Views.Components;

public sealed partial class RecommendedActionCard : UserControl
{
    public RecommendedActionCard() => AvaloniaXamlLoader.Load(this);
}
