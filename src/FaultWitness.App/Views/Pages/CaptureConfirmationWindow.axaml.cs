using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FaultWitness.App.Presentation;
using FaultWitness.Localization;
namespace FaultWitness.App.Views.Pages;
public sealed partial class CaptureConfirmationWindow : Window
{
    public CaptureConfirmationWindow() => AvaloniaXamlLoader.Load(this);
    public void Prepare(LocalizationService text, bool restore)
    {
        Title = text.Get(restore ? "CaptureRestore" : "CaptureConfigure");
        Height = restore ? 260 : 300;
        DataContext = new CaptureConfirmationPresentation(new(text), text.Get(restore ? "CaptureRestoreWarning" : "CaptureConfigureWarning"), text.Get(restore ? "CaptureConfirmRestore" : "CaptureConfirmConfigure"));
        var button = this.FindControl<Button>("CaptureConfirmButton")!;
        button.Classes.Add(restore ? "secondary-action" : "primary-action");
        if (restore) button.Classes.Add("state-change-action");
    }
    private void Confirm(object? sender, RoutedEventArgs args) => Close(true);
    private void Cancel(object? sender, RoutedEventArgs args) => Close(false);
}
