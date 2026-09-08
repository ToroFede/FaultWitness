using Avalonia;

namespace FaultWitness.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>().UsePlatformDetect().LogToTrace();
}
