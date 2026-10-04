using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Core;
using FaultWitness.App.Views.Pages;

namespace FaultWitness.UI.Tests;

public sealed class CaptureAxamlVisualTests
{
    [AvaloniaFact]
    public async Task ThirtyRepresentativeScreens_UseSyntheticWorkflowOnly()
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2B3_VISUAL_OUTPUT");
        if (!string.IsNullOrEmpty(output)) Directory.CreateDirectory(output);
        var scenarios = new[] { "not-configured", "selection", "preview", "configured-verified", "uac-cancelled", "operation-error", "restore-available", "drift-blocked", "pending-recovery", "null-service" };
        var index = 0;
        for (var scenarioIndex = 0; scenarioIndex < scenarios.Length; scenarioIndex++)
        {
            var scenario = scenarios[scenarioIndex];
            var matrix = new[] { ("en", AppTheme.Light, 1280, 1000), ("en", AppTheme.Dark, 1280, 1000),
                scenarioIndex % 2 == 0 ? ("de", AppTheme.Dark, 600, 1200) : ("it", AppTheme.Light, 1920, 1080) };
            foreach (var (language, theme, width, height) in matrix)
            {
                var service = new CaptureAxamlTests.Service(); var journal = new CaptureAxamlTests.Journal();
                var flow = scenario == "null-service" ? null : new CaptureWorkflow(service, journal) { TargetExecutable = "synthetic.exe" };
                if (flow is not null)
                {
                    if (scenario is "uac-cancelled") service.Result = CaptureResultCode.CancelledByUser;
                    if (scenario is "operation-error") service.Result = CaptureResultCode.ApplyFailed;
                    await flow.PreviewAsync();
                    if (scenario is "configured-verified" or "uac-cancelled" or "operation-error" or "restore-available" or "drift-blocked") await flow.ConfigureAsync();
                    if (scenario == "drift-blocked")
                    {
                        service.State = service.State with { DumpCount = 7 }; await flow.RefreshAsync();
                        await flow.RestoreAsync(flow.Entries[0].ActionId);
                    }
                    if (scenario == "pending-recovery")
                    {
                        var before = new LocalDumpState(false);
                        journal.Items.Add(new(Guid.NewGuid(), CaptureOperation.ConfigureApplicationCrashDump, "recovery.exe", DateTimeOffset.UtcNow.AddDays(-1), true, before, CrashCapturePolicy.Desired(before)));
                        service.State = CrashCapturePolicy.Desired(before);
                    }
                }
                var window = CaptureAxamlTests.Open(flow, language);
                try
                {
                    window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Theme = theme });
                    window.Width = width; window.Height = height; CaptureAxamlTests.Settle(window);
                    var view = CaptureAxamlTests.View(window);
                    if (scenario == "selection") CaptureAxamlTests.Find<TextBox>(window, "CaptureExecutable").Text = "selected.exe";
                    if (scenario is "preview" or "configured-verified") CaptureAxamlTests.Find<CheckBox>(window, "CaptureArchitectureConfirmation").IsChecked = true;
                    var system = window.GetVisualDescendants().OfType<AnalyzeCaptureView>().Single();
                    var scroll = system.FindControl<ScrollViewer>("AnalyzeCaptureScroll")!;
                    var root = system.FindControl<StackPanel>("AnalyzeCaptureContent")!;
                    if (scenario == "preview" && language == "de") view.FindControl<Expander>("CapturePreviewDetails")!.IsExpanded = true;
                    if (scenario == "pending-recovery" && language == "de") view.GetVisualDescendants().OfType<Expander>().Single(item => item.Name != "CapturePreviewDetails").IsExpanded = true;
                    var focusRegion = scenario is "restore-available" or "drift-blocked" or "pending-recovery" ? "CaptureResultRegion" : "CapturePurpose";
                    var anchor = view.FindControl<Control>(focusRegion)!;
                    CaptureAxamlTests.Settle(window);
                    scroll.Offset = new Vector(0, anchor.TranslatePoint(default, root)!.Value.Y);
                    CaptureAxamlTests.Settle(window);
                    CaptureAxamlTests.AssertNoOverflow(window);
                    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No Capture frame.");
                    Assert.True(frame.PixelSize.Width > 0);
                    if (!string.IsNullOrEmpty(output)) frame.Save(Path.Combine(output, $"{index:D2}-{scenario}-{language}-{theme}-{width}x{height}.png"), new PngBitmapEncoderOptions());
                    index++;
                }
                finally { window.Close(); }
            }
        }
        Assert.Equal(30, index);
    }
}
