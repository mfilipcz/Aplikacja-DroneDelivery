using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Maps; // <--- Upewnij się, że to jest odkomentowane

namespace DroneDeliveryMac;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiMaps() // <--- TO MUSI BYĆ ODKOMENTOWANE! INACZEJ APKA UMIERA PO CICHU.
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}