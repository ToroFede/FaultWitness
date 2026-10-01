using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Pass2D3VisualValidation")]
public sealed class Pass2D3VisualValidationTests
{
    [AvaloniaFact]
    public async Task RenderCommandFeedbackMatrixWithVerifiedGeometryAndAnnouncementOwner()
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2D3_VISUAL_OUTPUT");
        foreach (var scenario in Cases()) await Render(scenario, output);
    }

    private static async Task Render(VisualCase scenario, string? output)
    {
        var services = new TestServices { Settings = new UserSettings(Language: scenario.Locale, Theme: scenario.Theme) };
        if (scenario.Page == "history")
        {
            services.FailHistoryLoad = scenario.State == "error";
            if (scenario.State == "populated") services.History = [CommandFeedbackTests.StoredHistory()];
        }
        var window = CommandFeedbackTests.Open(services, scenario.Width);
        var vm = window.ViewModel;
        Task? running = null;
        try
        {
            switch (scenario.Page)
            {
                case "analyze":
                    vm.OpenAnalyze(scenario.State == "invalid-around" ? AnalysisMode.Around : AnalysisMode.Recent);
                    var page = CommandFeedbackTests.Current<AnalyzeView>(window);
                    if (scenario.State == "invalid-range")
                    {
                        page.FindControl<ComboBox>("PeriodSelector")!.SelectedIndex = (int)AnalysisPeriod.Custom;
                        page.FindControl<DatePicker>("FromDate")!.SelectedDate = DateTimeOffset.Now.AddDays(2);
                        CommandFeedbackTests.Click(page.FindControl<Button>("RunAnalysis")!);
                    }
                    else if (scenario.State == "invalid-around")
                    {
                        page.FindControl<DatePicker>("AroundDate")!.SelectedDate = DateTimeOffset.Now.AddDays(2);
                        CommandFeedbackTests.Click(page.FindControl<Button>("RunAroundAnalysis")!);
                    }
                    else if (scenario.State is "running" or "cancelled")
                    {
                        services.WaitForCancellation = true;
                        running = vm.AnalyzeAsync();
                        if (scenario.State == "cancelled") { vm.Cancel(); await running; }
                    }
                    else if (scenario.State == "completed") await vm.AnalyzeAsync();
                    break;
                case "export":
                    vm.SetResult(SyntheticResults.Create(2));
                    vm.Navigate(AppPage.Export);
                    if (scenario.State == "error") vm.Fail("OperationError", new IOException("synthetic export failure"));
                    break;
                case "history":
                    vm.Navigate(AppPage.History);
                    await vm.RefreshHistoryAsync();
                    break;
                case "shell":
                    services.WaitForCancellation = true;
                    vm.OpenAnalyze(AnalysisMode.Recent);
                    running = vm.AnalyzeAsync();
                    vm.Navigate(AppPage.Settings);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(scenario));
            }

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            if (vm.HasVisibleStatus)
                CommandFeedbackTests.AssertSingleLiveOwner(window, vm.HasLocalFeedback ? (scenario.Page == "analyze" ? "Analyze" : scenario.Page == "history" ? "History" : "Export") + "Feedback" : "StatusText");
            if (scenario.Page == "history")
                Assert.Equal(scenario.State == "empty", CommandFeedbackTests.Current<HistoryView>(window).FindControl<Border>("HistoryEmpty")!.IsVisible);
            foreach (var viewer in window.GetVisualDescendants().OfType<ScrollViewer>().Where(item => item.Name != "PART_ScrollViewer"))
                Assert.True(viewer.Viewport.Width == 0 || viewer.Extent.Width <= viewer.Viewport.Width + 2, $"{scenario.Name}: {viewer.Name} overflows horizontally.");
            if (scenario.Page == "shell")
            {
                Assert.Equal(window.FindControl<ContentControl>("PageHost")!.Bounds.X, window.FindControl<Grid>("StatusArea")!.Bounds.X, 2);
                Assert.Equal(window.FindControl<ContentControl>("PageHost")!.Bounds.Width, window.FindControl<Grid>("StatusArea")!.Bounds.Width, 2);
            }
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No visual frame.");
            VisualRenderGeometry.AssertFrameMatches(window, frame, scenario.Width, 900, scenario.Name);
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                frame.Save(Path.Combine(output, scenario.Name + ".png"), new PngBitmapEncoderOptions());
            }
        }
        finally
        {
            vm.Cancel();
            if (running is not null) await running.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
        }
    }

    private static IEnumerable<VisualCase> Cases()
    {
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            foreach (var state in new[] { "normal", "invalid-range", "invalid-around", "running", "cancelled", "completed" })
                yield return new("analyze", state, "en", theme, 1280);
            foreach (var state in new[] { "normal", "error" }) yield return new("export", state, "en", theme, 1280);
            foreach (var state in new[] { "empty", "error", "populated" }) yield return new("history", state, "en", theme, 1280);
            yield return new("shell", "busy-away", "en", theme, 1920);
        }
        foreach (var state in new[] { "invalid-range", "invalid-around", "running" }) yield return new("analyze", state, "de", AppTheme.Dark, 600);
        yield return new("analyze", "cancelled", "ru", AppTheme.Dark, 640);
        yield return new("analyze", "invalid-around", "pl", AppTheme.Dark, 641);
        yield return new("export", "error", "it", AppTheme.Light, 1008);
        yield return new("history", "error", "ru", AppTheme.Dark, 640);
        yield return new("history", "populated", "pl", AppTheme.Light, 641);
    }

    private sealed record VisualCase(string Page, string State, string Locale, AppTheme Theme, int Width)
    {
        public string Name => $"{Page}-{State}-{Locale}-{Theme.ToString().ToLowerInvariant()}-{Width}";
    }
}
