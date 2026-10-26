using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

public sealed class FinalRedesignTests
{
    [Fact]
    public void FormattedPreviewRetainsEverySourceLineWithoutChangingExportText()
    {
        const string raw = "# FaultWitness\r\n\r\n## Incident\r\n- Fact: C:\\dump\\file & <record>\r\n### Limit\r\nExact ‘quotation’.\r\n";
        var lines = ReportPreviewLine.Format(raw);
        Assert.Equal(raw.Replace("\r\n", "\n", StringComparison.Ordinal), string.Join('\n', lines.Select(line => line.SourceText)));
        Assert.Equal("FaultWitness", lines[0].Text);
        Assert.True(lines[0].IsTitle);
        Assert.True(lines[3].IsBullet);
        Assert.Contains(lines, line => line.Text == "Exact ‘quotation’.");
    }

    [AvaloniaTheory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")]
    [InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public async Task FinalSurfacesAndDialogsFitAllLocalesAt560AndDesktop(string language)
    {
        var capture = new CaptureWorkflow(new CaptureAxamlTests.Service(), new CaptureAxamlTests.Journal()) { TargetExecutable = "synthetic.exe" };
        await capture.PreviewAsync();
        var services = new TestServices
        {
            Capture = capture,
            Settings = new UserSettings(Language: language, ReduceMotion: true),
            History = [CommandFeedbackTests.StoredHistory()]
        };
        using var vm = new MainViewModel(services);
        var window = new MainWindow(vm);
        VisualRenderGeometry.ShowAtRequestedGeometry(window, 560, 900);
        window.SetRenderScaling(2);
        try
        {
            vm.Navigate(AppPage.Export); window.UpdateLayout();
            var empty = window.GetVisualDescendants().OfType<ExportSupportView>().Single().FindControl<Border>("ExportUnavailable")!;
            Assert.Contains(empty.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == vm.Text.Get("ExportEmptyHelp"));
            vm.SetResult(SyntheticResults.Create(3));
            await vm.RefreshHistoryAsync();
            vm.SelectHistory(vm.History[0]);
            foreach (var width in new[] { 560, 1280 })
            foreach (var page in new[] { AppPage.Capture, AppPage.History, AppPage.Export, AppPage.System, AppPage.Readiness, AppPage.Settings, AppPage.Incidents })
            {
                window.Width = width;
                vm.ChangeSettings(vm.Settings with { Theme = width == 560 ? AppTheme.Dark : AppTheme.Light });
                vm.Navigate(page);
                if (page == AppPage.System) await vm.RefreshInventoryAsync();
                if (page == AppPage.Readiness) await vm.RefreshReadinessAsync();
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var content = (Control)window.FindControl<ContentControl>("PageHost")!.Content!;
                AssertFits(content, $"{language}/{page}/{width}");
                if (page == AppPage.History)
                {
                    var list = content.FindControl<ListBox>("HistoryList")!;
                    Assert.True(list.Bounds.Height < 250, "A single saved analysis must not create a tall empty list.");
                    Assert.NotNull(list.SelectedItem);
                    Assert.All(list.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible && block.Bounds.Height > 0), block =>
                        Assert.True(block.TranslatePoint(default, list)!.Value.Y + block.Bounds.Height <= list.Bounds.Height - 1,
                            "The bottom of a short history list must leave room for the last line."));
                }
                if (page == AppPage.Export)
                {
                    Assert.True(content.FindControl<ScrollViewer>("FormattedExportScroll")!.IsVisible);
                    Assert.False(content.FindControl<TextBox>("ExportPreview")!.IsVisible);
                    var projection = (ExportSupportPresentation)content.DataContext!;
                    Assert.Equal(window.ExportPreview.Replace("\r\n", "\n", StringComparison.Ordinal),
                        string.Join('\n', projection.PreviewLines.Select(line => line.SourceText)));
                    Assert.All(content.GetVisualDescendants().OfType<SelectableTextBlock>(), block =>
                    {
                        Assert.Equal(window.FindResource("ContentControlThemeFontFamily"), block.FontFamily);
                        Assert.True(block.FontSize >= 14);
                    });
                }
                SaveHighDpi(window, language, page, width);
            }
            window.Width = 560; window.UpdateLayout();
            foreach (var restore in new[] { false, true })
            {
                var dialog = new CaptureConfirmationWindow();
                dialog.Prepare(vm.Text, window, restore);
                dialog.Show(window);
                try { dialog.UpdateLayout(); AssertFits(dialog, $"{language}/capture-dialog/{restore}"); }
                finally { dialog.Close(); }
            }
            var clear = new ClearDataConfirmationWindow(); clear.Prepare(vm.Text, window); clear.Show(window);
            try { clear.UpdateLayout(); AssertFits(clear, $"{language}/clear-dialog"); }
            finally { clear.Close(); }
        }
        finally { window.Close(); }
    }

    private static void AssertFits(Control root, string context)
    {
        foreach (var scroll in root.GetVisualDescendants().OfType<ScrollViewer>()
                     .Where(control => control.IsEffectivelyVisible && !control.GetVisualAncestors().OfType<TextBox>().Any()))
            Assert.True(scroll.Viewport.Width == 0 || scroll.Extent.Width <= scroll.Viewport.Width + 2,
                $"{context}: {scroll.Name} horizontal overflow {scroll.Extent.Width} > {scroll.Viewport.Width}.");
        foreach (var button in root.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible && control.Bounds.Width > 0))
        {
            var point = button.TranslatePoint(default, root);
            Assert.True(point is null || point.Value.X >= -1 && point.Value.X + button.Bounds.Width <= root.Bounds.Width + 2,
                $"{context}: {button.Name} extends beyond its page ({point?.X} + {button.Bounds.Width} > {root.Bounds.Width}).");
        }
        foreach (var block in root.GetVisualDescendants().OfType<TextBlock>().Where(control => control.IsEffectivelyVisible && control.Bounds.Width > 0 &&
                     control.TextWrapping == Avalonia.Media.TextWrapping.Wrap && control.TextTrimming == Avalonia.Media.TextTrimming.None))
            Assert.True(block.TextLayout.Width <= block.Bounds.Width + 2,
                $"{context}: text exceeds its wrapping width ({block.TextLayout.Width} > {block.Bounds.Width}): {block.Text}");
    }

    private static void SaveHighDpi(Window window, string language, AppPage page, int width)
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_FINAL_VISUAL_OUTPUT");
        if (string.IsNullOrWhiteSpace(output) || width != 560) return;
        Directory.CreateDirectory(output);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
        using var bitmap = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Headless frame was not rendered.");
        Assert.Equal(1120, bitmap.PixelSize.Width);
        Assert.Equal(1800, bitmap.PixelSize.Height);
        bitmap.Save(Path.Combine(output, $"{language}-{page}-560-200pct.png"), new PngBitmapEncoderOptions());
    }
}
