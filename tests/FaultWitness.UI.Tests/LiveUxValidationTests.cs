using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using FaultWitness.App;
using Microsoft.Data.Sqlite;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "RealMachineUx")]
public sealed class LiveUxValidationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    [AvaloniaFact]
    public async Task RealMachine_SevenDayAndAroundTimeFlowsRemainResponsive()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FAULTWITNESS_LIVE_UX"), "1", StringComparison.Ordinal)) return;
        var root = FindRoot(); var output = Path.Combine(root, "artifacts", "ux-architecture", "live-validation"); Directory.CreateDirectory(output);
        var data = Directory.CreateTempSubdirectory("FaultWitness-live-ux-");
        using var viewModel = new MainViewModel(new DesktopServices(data.FullName));
        var window = new MainWindow(viewModel); window.Show(); window.Width = 1280; window.Height = 800; window.UpdateLayout();
        try
        {
            using var process = Process.GetCurrentProcess(); long peak = process.WorkingSet64;
            async Task Observe(Task task) { while (!task.IsCompleted) { peak = Math.Max(peak, process.WorkingSet64); await Task.WhenAny(task, Task.Delay(50, TestContext.Current.CancellationToken)); } await task; peak = Math.Max(peak, process.WorkingSet64); }
            var ticks = window.ResponsiveTicks; await Observe(viewModel.AnalyzeAsync());
            var seven = new { viewModel.AttentionCount, viewModel.KnowingCount, viewModel.BackgroundCount, viewModel.LastDurationSeconds, ResponsiveTicks = window.ResponsiveTicks - ticks };
            window.UpdateLayout(); (window.CaptureRenderedFrame() ?? throw new InvalidOperationException()).Save(Path.Combine(output, "real-7-day-home.png"), new PngBitmapEncoderOptions());
            viewModel.AroundTime = DateTimeOffset.Now.AddMinutes(-10); viewModel.WindowMinutes = 5; ticks = window.ResponsiveTicks; await Observe(viewModel.AnalyzeAsync(true));
            var around = new { viewModel.AttentionCount, viewModel.KnowingCount, viewModel.BackgroundCount, viewModel.LastDurationSeconds, ResponsiveTicks = window.ResponsiveTicks - ticks };
            window.UpdateLayout(); (window.CaptureRenderedFrame() ?? throw new InvalidOperationException()).Save(Path.Combine(output, "real-around-time-home.png"), new PngBitmapEncoderOptions());
            var importPath = Path.Combine(root, "artifacts", "ux-validation", "synthetic-gui.zip");
            viewModel.OpenAnalyze(AnalysisMode.Files); viewModel.AddImports([importPath]); await Observe(viewModel.AnalyzeImportsAsync());
            var imported = viewModel.IsImported && viewModel.Imports.Any(item => item.StatusKey == "ImportAccepted");
            await File.WriteAllTextAsync(Path.Combine(output, "aggregate.json"), JsonSerializer.Serialize(new { seven, around, imported, PeakWorkingSetBytes = peak, window.ResponsiveTicks }, JsonOptions), TestContext.Current.CancellationToken);
            Assert.True(seven.ResponsiveTicks > 0); Assert.True(around.ResponsiveTicks > 0); Assert.True(imported);
        }
        finally { window.Close(); SqliteConnection.ClearAllPools(); data.Delete(true); }
    }
    private static string FindRoot() { var path = AppContext.BaseDirectory; while (!File.Exists(Path.Combine(path, "FaultWitness.slnx"))) path = Directory.GetParent(path)?.FullName ?? throw new DirectoryNotFoundException(); return path; }
}
