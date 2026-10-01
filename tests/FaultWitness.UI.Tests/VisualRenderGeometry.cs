using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace FaultWitness.UI.Tests;

internal static class VisualRenderGeometry
{
    public static void ShowAtRequestedGeometry(Window window, int width, int height)
    {
        window.Show();

        // MainWindow applies restored geometry from Opened. Let that lifecycle pass finish
        // before applying the dimensions requested by a synthetic visual scenario.
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        window.Width = width;
        window.Height = height;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        AssertRequestedGeometry(window, width, height, "after open and geometry restoration");
    }

    public static void AssertFrameMatches(Window window, Bitmap frame, int width, int height, string scenario)
    {
        AssertRequestedGeometry(window, width, height, scenario);
        Assert.True(frame.PixelSize.Width == width && frame.PixelSize.Height == height,
            $"{scenario}: requested {width}x{height}, window bounds are {window.Bounds.Width:0.##}x{window.Bounds.Height:0.##}, " +
            $"but rendered bitmap is {frame.PixelSize.Width}x{frame.PixelSize.Height}.");
    }

    private static void AssertRequestedGeometry(Window window, int width, int height, string phase)
    {
        Assert.True(Math.Abs(window.Bounds.Width - width) < 0.01 && Math.Abs(window.Bounds.Height - height) < 0.01,
            $"Requested {width}x{height}, but window bounds after {phase} are " +
            $"{window.Bounds.Width:0.##}x{window.Bounds.Height:0.##}.");
    }
}
