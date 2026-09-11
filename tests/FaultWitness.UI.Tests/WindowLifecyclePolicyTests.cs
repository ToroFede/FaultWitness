using FaultWitness.App;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Headless")]
public sealed class WindowLifecyclePolicyTests
{
    [AvaloniaFact]
    public void MaximizedState_IsAppliedAfterOpening()
    {
        var services = new TestServices { Settings = new UserSettings(WindowState: AppWindowState.Maximized) };
        using var viewModel = new MainViewModel(services);
        var window = new MainWindow(viewModel);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Maximized, window.WindowState);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(400, 500, 1920, 1080, 560, 600)]
    [InlineData(1600, 1200, 1000, 700, 1000, 700)]
    [InlineData(900, 650, 1920, 1080, 900, 650)]
    public void ClampSize_RespectsMinimumAndWorkingArea(double width, double height, double workWidth, double workHeight, double expectedWidth, double expectedHeight)
    {
        var result = WindowLifecyclePolicy.ClampSize(width, height, workWidth, workHeight);
        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
    }

    [Fact]
    public void SafePlacement_RecoversOffScreenWindow()
    {
        var result = WindowLifecyclePolicy.SafePlacement(-4000, 2000, 900, 700, 0, 0, 1920, 1080);
        Assert.InRange(result.X, -852, 1872);
        Assert.InRange(result.Y, 0, 1032);
    }

    [Fact]
    public void MinimizedState_IsNeverRestored() => Assert.False(WindowLifecyclePolicy.IsRestorableState((AppWindowState)99));

    [Fact]
    public void Settings_RejectInvalidWindowStateAndDimensions()
    {
        var directory = Directory.CreateTempSubdirectory("faultwitness-window-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "settings.json"), JsonSerializer.Serialize(new
            {
                WindowWidth = 400d, WindowHeight = 500d, WindowState = 99,
                Language = "en", Theme = AppTheme.System, Period = AnalysisPeriod.Week, RetentionDays = 30
            }));
            var settings = new DesktopServices(directory.FullName).LoadSettings();
            Assert.Equal(WindowLifecyclePolicy.MinimumWidth, settings.WindowWidth);
            Assert.Equal(WindowLifecyclePolicy.MinimumHeight, settings.WindowHeight);
            Assert.Equal(AppWindowState.Normal, settings.WindowState);
        }
        finally { directory.Delete(true); }
    }
}
