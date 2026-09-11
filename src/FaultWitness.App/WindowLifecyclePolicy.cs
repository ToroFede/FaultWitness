namespace FaultWitness.App;

/// <summary>Pure window sizing and placement rules shared by the desktop window and headless tests.</summary>
public static class WindowLifecyclePolicy
{
    public const double MinimumWidth = 560;
    public const double MinimumHeight = 600;
    // Comfortable for the large layout while leaving room for a typical laptop taskbar.
    public const double FirstLaunchWidth = 1120;
    public const double FirstLaunchHeight = 760;

    public static (double Width, double Height) ClampSize(double width, double height, double workWidth, double workHeight)
    {
        var maxWidth = Math.Max(MinimumWidth, workWidth);
        var maxHeight = Math.Max(MinimumHeight, workHeight);
        return (Math.Clamp(width, MinimumWidth, maxWidth), Math.Clamp(height, MinimumHeight, maxHeight));
    }

    public static (double X, double Y) SafePlacement(double x, double y, double width, double height,
        double workX, double workY, double workWidth, double workHeight)
    {
        // Keep a useful portion of the title bar on the working area. This also recovers
        // windows whose previous monitor has been disconnected.
        const double visibleTitleBar = 48;
        var minX = workX - width + visibleTitleBar;
        var maxX = workX + workWidth - visibleTitleBar;
        var minY = workY;
        var maxY = workY + workHeight - visibleTitleBar;
        return (Math.Clamp(x, minX, maxX), Math.Clamp(y, minY, maxY));
    }

    public static bool IsRestorableState(AppWindowState state) => state is AppWindowState.Normal or AppWindowState.Maximized;
}
