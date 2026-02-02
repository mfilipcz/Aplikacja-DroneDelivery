using Avalonia;
using System;

namespace DroneDeliveryLinux;

sealed class Program
{
    // Application entry point.
    [STAThread]
    public static void Main(string[] args)
    {
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.Svg).Assembly);
        BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
