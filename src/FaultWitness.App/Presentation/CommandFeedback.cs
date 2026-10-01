using Avalonia.Automation;
using Avalonia.Controls;

namespace FaultWitness.App.Presentation;

// Stateless presentation of MainViewModel's status in existing command regions.
internal static class CommandFeedback
{
    public static void Refresh(UserControl view, string prefix, MainViewModel source)
    {
        var visible = source.HasLocalFeedback;
        var text = view.FindControl<TextBlock>(prefix + "Feedback")!;
        AutomationProperties.SetLiveSetting(text, visible ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off);
        text.Text = source.StatusText;
        AutomationProperties.SetName(text, source.StatusText);
        view.FindControl<StackPanel>(prefix + "FeedbackRegion")!.IsVisible = visible;
        var details = view.FindControl<Expander>(prefix + "FeedbackDetails")!;
        details.Header = source.Text.Get("TechnicalDetails");
        AutomationProperties.SetName(details, source.Text.Get("TechnicalDetails"));
        details.IsVisible = visible && source.TechnicalError.Length > 0;
        ((TextBlock)details.Content!).Text = source.TechnicalError;
        if (view.FindControl<Button>("CancelAnalyzeLocal") is { } cancel)
        {
            var validation = visible && source.StatusKey == "InvalidTimeRange" ? source.StatusText : string.Empty;
            foreach (var name in new[] { "PeriodSelector", "FromDate", "ToDate", "AroundDate", "AroundTime", "AroundWindow" })
                AutomationProperties.SetHelpText(view.FindControl<Control>(name)!, validation);
            cancel.IsVisible = visible && source.IsBusy;
            AutomationProperties.SetName(cancel, source.Text.Get("Cancel"));
            ((TextBlock)cancel.Content!).Text = source.Text.Get("Cancel");
            var progress = view.FindControl<ProgressBar>("AnalyzeLocalProgress")!;
            progress.IsVisible = visible && source.IsBusy;
            AutomationProperties.SetName(progress, source.Text.Get("AnalysisInProgress"));
        }
    }
}
