using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "VisualRenderGeometry")]
public sealed class VisualRenderGeometryTests
{
    [AvaloniaFact]
    public async Task HomeAndReadiness_RenderAtRequestedPostRestoreGeometry()
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2D01_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);

        foreach (var width in new[] { 600, 640, 641, 1280, 1920 })
        foreach (var page in new[] { AppPage.Home, AppPage.Readiness })
        {
            var services = new TestServices
            {
                Settings = new UserSettings(Language: "en", Theme: AppTheme.Light),
                StructuredReadiness =
                [
                    new("system-event-log", "SourceSystem", DiagnosticCapabilityStatus.Ready, "ReadinessSourceReady"),
                    new("wer", "SourceTypeWer", DiagnosticCapabilityStatus.Limited, "ReadinessSourceLimited")
                ]
            };
#pragma warning disable CA2000 // MainWindow takes ownership of the view model and disposes it when closed.
            var viewModel = new MainViewModel(services);
            var window = new MainWindow(viewModel);
#pragma warning restore CA2000
            var scenario = $"geometry-{page.ToString().ToLowerInvariant()}-{width}x900";
            VisualRenderGeometry.ShowAtRequestedGeometry(window, width, 900);

            try
            {
                if (page == AppPage.Home)
                    viewModel.SetResult(SyntheticResults.Create(3));
                else
                {
                    viewModel.Navigate(AppPage.Readiness);
                    await viewModel.RefreshReadinessAsync();
                }

                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.All(window.FindControl<ContentControl>("PageHost")!.GetVisualDescendants().OfType<ScrollViewer>(),
                    viewer => Assert.True(viewer.Viewport.Width == 0 || viewer.Extent.Width <= viewer.Viewport.Width + 2,
                        $"{scenario}: horizontal overflow {viewer.Extent.Width} > {viewer.Viewport.Width}."));

                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{scenario}: window did not render.");
                VisualRenderGeometry.AssertFrameMatches(window, frame, width, 900, scenario);
                if (!string.IsNullOrWhiteSpace(output))
                    frame.Save(Path.Combine(output, scenario + ".png"), new PngBitmapEncoderOptions());
            }
            finally { window.Close(); }
        }
    }
}
