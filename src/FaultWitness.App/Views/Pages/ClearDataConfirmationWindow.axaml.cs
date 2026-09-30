using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FaultWitness.App.Presentation;
using FaultWitness.Localization;

namespace FaultWitness.App.Views.Pages;

public sealed partial class ClearDataConfirmationWindow : Window
{
    public ClearDataConfirmationWindow() => AvaloniaXamlLoader.Load(this);

    public void Prepare(LocalizationService text)
    {
        Title = text.Get("ClearData");
        DataContext = new ClearDataConfirmationPresentation(text);
    }

    private void Confirm(object? sender, RoutedEventArgs args) => Close(true);
    private void Cancel(object? sender, RoutedEventArgs args) => Close(false);
}
