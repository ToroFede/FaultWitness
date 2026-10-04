using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;

#pragma warning disable CA2000 // Each window owns its synthetic view model and is closed in finally.

namespace FaultWitness.UI.Tests;

public sealed class Pass2EVisualValidationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    [AvaloniaFact]
    public async Task TwentyTwoNavigationScreensAssertActualGeometryAndSingleCaptureRoute()
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2E_VISUAL_OUTPUT");
        var evidence = new List<object>();
        var cases = Cases().ToArray();
        Assert.Equal(22, cases.Length);
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);
        for (var index = 0; index < cases.Length; index++)
        {
            var scenario = cases[index];
            var captureService = new CaptureAxamlTests.Service();
            var flow = new CaptureWorkflow(captureService, new CaptureAxamlTests.Journal());
            if (scenario.State is "preview" or "verified")
            {
                flow.TargetExecutable = "synthetic.exe";
                await flow.PreviewAsync();
                if (scenario.State == "verified") await flow.ConfigureAsync();
            }
            var services = new TestServices
            {
                Capture = flow,
                Settings = new UserSettings(Language: scenario.Locale, Theme: scenario.Theme),
                StructuredInventory = new SystemInventorySnapshot([
                    new("InventoryGroupOperatingSystem", [new("os", [new("InventoryOperatingSystem", "Synthetic Windows"), new("InventoryOsVersion", "Synthetic NT")])]),
                    new("InventoryGroupProcessorMemory", [new("cpu", [new("InventoryProcessorLogical", "8"), new("InventoryArchitecture", "X64")])]),
                    new("InventoryGroupGraphics", []), new("InventoryGroupStorage", [])]),
                StructuredReadiness = [new("system", "SourceSystem", DiagnosticCapabilityStatus.Ready, "ReadinessHelp"),
                    new("wer", "SourceWer", DiagnosticCapabilityStatus.Limited, "ReadinessHelp")]
            };
            var window = AnalyzeCaptureNavigationTests.Open(services);
            Task? running = null;
            try
            {
                window.Width = scenario.Width;
                window.Height = 1100;
                window.ViewModel.Navigate(scenario.Page);
                if (scenario.Page == AppPage.Readiness) await window.ViewModel.RefreshReadinessAsync();
                if (scenario.State == "running")
                {
                    services.WaitForCancellation = true;
                    running = window.ViewModel.AnalyzeAsync();
                }
                CaptureAxamlTests.Settle(window);
                if (scenario.Anchor is { } anchorName)
                {
                    var host = AnalyzeCaptureNavigationTests.Current<AnalyzeCaptureView>(window);
                    var root = host.FindControl<StackPanel>("AnalyzeCaptureContent")!;
                    var anchor = CaptureAxamlTests.View(window).FindControl<Control>(anchorName)!;
                    host.FindControl<ScrollViewer>("AnalyzeCaptureScroll")!.Offset = new Vector(0, anchor.TranslatePoint(default, root)!.Value.Y);
                    CaptureAxamlTests.Settle(window);
                }
                CaptureAxamlTests.AssertNoOverflow(window);
                Assert.Equal(scenario.Page == AppPage.Capture ? 1 : 0, window.GetVisualDescendants().OfType<CaptureView>().Count());
                Assert.Equal(scenario.State == "verified" ? 1 : 0, captureService.Executions);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No navigation frame.");
                VisualRenderGeometry.AssertFrameMatches(window, frame, scenario.Width, 1100, scenario.State);
                var file = $"{index:D2}-{scenario.Page}-{scenario.State}-{scenario.Locale}-{scenario.Theme}-{scenario.Width}x1100.png";
                if (!string.IsNullOrWhiteSpace(output)) frame.Save(Path.Combine(output, file), new PngBitmapEncoderOptions());
                evidence.Add(new { file, requestedWidth = scenario.Width, requestedHeight = 1100,
                    bitmapWidth = frame.PixelSize.Width, bitmapHeight = frame.PixelSize.Height, scenario.Locale,
                    theme = scenario.Theme.ToString(), page = scenario.Page.ToString(), scenario.State, geometryAsserted = true });
            }
            finally
            {
                window.ViewModel.Cancel();
                if (running is not null) await running;
                window.Close();
            }
        }
        if (!string.IsNullOrWhiteSpace(output))
            await File.WriteAllTextAsync(Path.Combine(output, "render-matrix.json"), JsonSerializer.Serialize(evidence, JsonOptions));
    }

    private sealed record Scenario(AppPage Page, string State, string Locale, AppTheme Theme, int Width, string? Anchor = null);
    private static IEnumerable<Scenario> Cases()
    {
        foreach (var (width, locale, theme) in new[] { (600, "de", AppTheme.Dark), (640, "ru", AppTheme.Dark), (641, "pl", AppTheme.Dark),
            (1008, "it", AppTheme.Light), (1280, "en", AppTheme.Light), (1920, "it", AppTheme.Light) })
        {
            yield return new(AppPage.Analyze, "normal", locale, theme, width);
            yield return new(AppPage.Capture, "normal", locale, theme, width);
        }
        yield return new(AppPage.Analyze, "normal", "en", AppTheme.Dark, 1280);
        yield return new(AppPage.Capture, "normal", "en", AppTheme.Dark, 1280);
        yield return new(AppPage.Analyze, "running", "en", AppTheme.Dark, 1280);
        yield return new(AppPage.Capture, "preview", "de", AppTheme.Dark, 600, "CapturePreviewRegion");
        yield return new(AppPage.Capture, "verified", "en", AppTheme.Light, 1280, "CapturePreviewRegion");
        yield return new(AppPage.Capture, "verified", "en", AppTheme.Dark, 1280, "CapturePreviewRegion");
        yield return new(AppPage.System, "inventory", "en", AppTheme.Light, 1280);
        yield return new(AppPage.Readiness, "mixed", "en", AppTheme.Dark, 1280);
        yield return new(AppPage.System, "inventory", "pl", AppTheme.Dark, 641);
        yield return new(AppPage.Readiness, "mixed", "de", AppTheme.Dark, 600);
    }
}
