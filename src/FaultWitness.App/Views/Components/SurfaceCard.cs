using Avalonia.Controls;

namespace FaultWitness.App.Views.Components;

/// <summary>Neutral, reusable content surface. It carries no diagnostic meaning or interaction.</summary>
public sealed class SurfaceCard : ContentControl { }

/// <summary>Keyboard-accessible metric action with a theme-owned surface across cached page/theme changes.</summary>
public sealed class MetricCard : Button { }
