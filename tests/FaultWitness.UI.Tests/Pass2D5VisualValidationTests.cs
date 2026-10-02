using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Views.Components;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

public sealed class Pass2D5VisualValidationTests
{
    [AvaloniaFact]
    public async Task FortyFourCaptureScreens_KeepWorkflowStatesAndCorrectRenderGeometry()
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2D5_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);
        var scenarios = Matrix().ToArray();
        Assert.Equal(44, scenarios.Length);
        for (var index = 0; index < scenarios.Length; index++)
        {
            var scenario = scenarios[index];
            var service = new CaptureAxamlTests.Service();
            var journal = new CaptureAxamlTests.Journal();
            var flow = scenario.State == "null-service" ? null : new CaptureWorkflow(service, journal) { TargetExecutable = "synthetic.exe" };
            var gate = new TaskCompletionSource<CaptureResult>();
            Task? running = null;
            if (flow is not null)
            {
                if (scenario.State is "restore-candidate" or "journal-expanded") service.State = new(true, 2, 5, @"%LOCALAPPDATA%\CrashDumps");
                if (scenario.State == "cancelled") service.Result = CaptureResultCode.CancelledByUser;
                if (scenario.State == "operation-failure") service.Result = CaptureResultCode.ApplyFailed;
                if (scenario.State == "verification-failure") service.SkipWrite = true;
                await flow.PreviewAsync();
                if (scenario.State is "verified" or "cancelled" or "operation-failure" or "verification-failure" or "restore-candidate" or "restore-success" or "drift-refusal" or "newer-refusal" or "journal-expanded")
                    await flow.ConfigureAsync();
                if (scenario.State == "restore-success") await flow.RestoreAsync(flow.Entries[0].ActionId);
                if (scenario.State == "drift-refusal")
                {
                    service.State = service.State with { DumpCount = 7 };
                    await flow.RestoreAsync(flow.Entries[0].ActionId);
                }
                if (scenario.State == "newer-refusal")
                {
                    var original = flow.Entries[0];
                    journal.Items.Add(original with { ActionId = Guid.NewGuid(), TimestampUtc = original.TimestampUtc.AddMinutes(1), Result = CaptureResultCode.Pending });
                    await flow.RestoreAsync(original.ActionId);
                }
                if (scenario.State == "pending-recovery")
                {
                    var before = new LocalDumpState(false);
                    journal.Items.Add(new(Guid.NewGuid(), CaptureOperation.ConfigureApplicationCrashDump, "synthetic.exe", DateTimeOffset.UtcNow.AddDays(-1), true, before, CrashCapturePolicy.Desired(before)));
                    service.State = CrashCapturePolicy.Desired(before);
                    await flow.RefreshAsync();
                }
                if (scenario.State == "busy-configure")
                {
                    service.ExecuteGate = gate.Task;
                    running = flow.ConfigureAsync();
                    Assert.Equal(CaptureResultCode.Pending, Assert.Single(journal.Items).Result);
                }
            }
            var window = CaptureAxamlTests.Open(flow, scenario.Language);
            try
            {
                window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Theme = scenario.Theme });
                window.Width = scenario.Width; window.Height = 1200;
                CaptureAxamlTests.Settle(window);
                var view = CaptureAxamlTests.View(window);
                if (scenario.State == "selection")
                {
                    // Opening System legitimately refreshes Capture. Edit afterward to retain the real invalidated state.
                    view.FindControl<TextBox>("CaptureExecutable")!.Text = "selected.exe";
                    Assert.Null(flow!.Preview);
                    Assert.Null(flow.LastResult);
                }
                if (scenario.State is "configure-ready" or "verified" or "technical-preview-expanded" or "busy-configure")
                    view.FindControl<CheckBox>("CaptureArchitectureConfirmation")!.IsChecked = true;
                if (scenario.State == "technical-preview-expanded") view.FindControl<Expander>("CapturePreviewDetails")!.IsExpanded = true;
                if (scenario.State == "journal-expanded")
                    view.GetVisualDescendants().OfType<CaptureRestoreCard>().Single().GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
                CaptureAxamlTests.Settle(window);
                var system = window.GetVisualDescendants().OfType<SystemView>().Single();
                var scroll = system.FindControl<ScrollViewer>("SystemScroll")!;
                var root = system.FindControl<StackPanel>("SystemContent")!;
                var anchor = view.FindControl<Control>(scenario.Anchor)!;
                scroll.Offset = new Vector(0, anchor.TranslatePoint(default, root)!.Value.Y);
                CaptureAxamlTests.Settle(window);
                CaptureAxamlTests.AssertNoOverflow(window);
                if (flow is not null)
                {
                    CaptureHierarchyTests.AssertOwner(window, flow.IsBusy ? "CaptureOperationStatus" : flow.LastResult is not null ? "CaptureLastResult" : "CapturePreviewNotice");
                    Assert.Equal(flow.CanConfigure && view.FindControl<CheckBox>("CaptureArchitectureConfirmation")!.IsChecked == true,
                        view.FindControl<Button>("CaptureConfigureButton")!.IsEnabled);
                }
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No Capture frame.");
                VisualRenderGeometry.AssertFrameMatches(window, frame, scenario.Width, 1200, scenario.State);
                if (!string.IsNullOrWhiteSpace(output))
                    frame.Save(Path.Combine(output, $"{index:D2}-{scenario.State}-{scenario.Language}-{scenario.Theme}-{scenario.Width}x1200.png"), new PngBitmapEncoderOptions());
            }
            finally
            {
                gate.TrySetResult(new(CaptureResultCode.CancelledByUser));
                if (running is not null) await running;
                window.Close();
            }
        }
    }

    private sealed record Scenario(string State, string Language, AppTheme Theme, int Width, string Anchor);
    private static IEnumerable<Scenario> Matrix()
    {
        var states = new[] { "not-configured", "selection", "preview-ready", "configure-ready", "busy-configure", "verified", "cancelled", "operation-failure", "verification-failure", "restore-candidate", "restore-success", "drift-refusal", "newer-refusal", "pending-recovery", "null-service", "journal-expanded", "technical-preview-expanded" };
        foreach (var state in states)
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            yield return new(state, "en", theme, 1280, Anchor(state));
        yield return new("configure-ready", "de", AppTheme.Dark, 600, "CaptureSafetyRegion");
        yield return new("verification-failure", "ru", AppTheme.Dark, 640, "CapturePreviewRegion");
        yield return new("pending-recovery", "pl", AppTheme.Dark, 641, "CaptureResultRegion");
        yield return new("preview-ready", "it", AppTheme.Light, 1920, "CapturePurpose");
        yield return new("restore-candidate", "de", AppTheme.Dark, 560, "CaptureResultRegion");
        yield return new("drift-refusal", "ru", AppTheme.Dark, 640, "CaptureResultRegion");
        yield return new("technical-preview-expanded", "pl", AppTheme.Light, 641, "CaptureTechnicalRegion");
        yield return new("verified", "it", AppTheme.Light, 1920, "CapturePreviewRegion");
        yield return new("journal-expanded", "de", AppTheme.Dark, 600, "CaptureResultRegion");
        yield return new("cancelled", "en", AppTheme.Dark, 1008, "CapturePreviewRegion");
    }

    private static string Anchor(string state) => state switch
    {
        "not-configured" or "selection" or "null-service" or "preview-ready" or "configure-ready" => "CapturePurpose",
        "verified" => "CapturePolicyRegion",
        "restore-candidate" or "restore-success" or "drift-refusal" or "newer-refusal" or "pending-recovery" or "journal-expanded" => "CaptureResultRegion",
        "technical-preview-expanded" => "CaptureTechnicalRegion",
        "busy-configure" => "CaptureApplyRegion",
        _ => "CapturePreviewRegion"
    };
}
