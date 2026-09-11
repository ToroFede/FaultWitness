using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "BetaUxPolish")]
public sealed class BetaUxPolishTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisMode.Recent, "RecentAnalysisGuide")]
    [InlineData(AnalysisMode.Around, "AroundAnalysisGuide")]
    [InlineData(AnalysisMode.Files, "ImportHelp")]
    public void AnalysisMode_ShowsModeSpecificGuidance(AnalysisMode mode, string guidanceKey)
    {
        using var viewModel = new MainViewModel(new TestServices());
        var window = new MainWindow(viewModel); window.Show();
        try
        {
            viewModel.OpenAnalyze(mode); window.UpdateLayout();
            Assert.Contains(viewModel.Text.Get(guidanceKey), VisibleText(window), StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReadinessEmpty_ShowsExplicitHelp()
    {
        using var viewModel = new MainViewModel(new TestServices { StructuredReadiness = [] });
        var window = new MainWindow(viewModel); window.Show();
        try
        {
            viewModel.Navigate(AppPage.Readiness); await viewModel.RefreshReadinessAsync(); window.UpdateLayout();
            Assert.Empty(viewModel.Readiness);
            Assert.Contains(viewModel.Text.Get("ReadinessEmptyHelp"), VisibleText(window), StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CapturePreview_IsCollapsedAndConfigureIsGuarded()
    {
        var capture = new CaptureWorkflow(new FakeCaptureService(), new MemoryCaptureJournal()) { TargetExecutable = "synthetic.exe" };
        using var viewModel = new MainViewModel(new TestServices { Capture = capture });
        var window = new MainWindow(viewModel); window.Show();
        try
        {
            await capture.PreviewAsync(); viewModel.Navigate(AppPage.System); window.UpdateLayout();
            var preview = window.GetLogicalDescendants().OfType<TextBlock>().Single(block => block.Name == "CapturePreviewState");
            var details = preview.GetLogicalAncestors().OfType<Expander>().Single();
            Assert.False(details.IsExpanded);
            Assert.Equal(viewModel.Text.Format("CapturePreviewValue", viewModel.Text.Get("NotAvailable"), viewModel.Text.Get("NotAvailable"), viewModel.Text.Get("NotAvailable")), preview.Text);

            var configure = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CaptureConfigureButton");
            Assert.False(configure.IsEnabled);
            Assert.Contains("state-change-action", configure.Classes);

            var checkbox = window.GetVisualDescendants().OfType<CheckBox>().Single(control => control.Name == "CaptureArchitectureConfirmation");
            Assert.Equal(viewModel.Text.Get("CaptureArchitectureConfirmation"), AutomationProperties.GetName(checkbox));
            var checkboxText = checkbox.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(TextWrapping.Wrap, checkboxText.TextWrapping);
            checkbox.IsChecked = true;
            Assert.True(configure.IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PrimaryAction_WrapsTextAndRemainsFocusable()
    {
        using var viewModel = new MainViewModel(new TestServices());
        var window = new MainWindow(viewModel); window.Show();
        try
        {
            viewModel.OpenAnalyze(AnalysisMode.Recent); window.Width = 600; window.Height = 900; window.UpdateLayout();
            var primary = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "RunAnalysis");
            var label = primary.Content as TextBlock;
            Assert.NotNull(label);
            Assert.Equal(TextWrapping.Wrap, label!.TextWrapping);
            Assert.True(primary.Focusable);
            Assert.True(primary.IsTabStop);
        }
        finally { window.Close(); }
    }

    private static string VisibleText(Control root) => string.Join(" ", root.GetVisualDescendants().OfType<TextBlock>().Where(item => item.IsVisible).Select(item => item.Text));

    private sealed class MemoryCaptureJournal : ICaptureJournal
    {
        public Task SaveAsync(CaptureJournalEntry entry, CancellationToken token) => Task.CompletedTask;
        public Task<IReadOnlyList<CaptureJournalEntry>> LoadAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CaptureJournalEntry>>([]);
    }

    private sealed class FakeCaptureService : ICrashCaptureService
    {
        public Task<CaptureReadResult> ReadAsync(string executable, CancellationToken token) => Task.FromResult(new CaptureReadResult(CaptureResultCode.Success, new LocalDumpState(false)));
        public Task<CaptureResult> ExecuteAsync(CaptureRequest request, CancellationToken token) => Task.FromResult(new CaptureResult(CaptureResultCode.Success, request.DesiredState));
    }
}
