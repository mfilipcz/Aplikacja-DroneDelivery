using Microsoft.Extensions.Logging;
using Grpc.Net.Client;
using DroneServer;
using System.Net.Http;

namespace DroneDelivery;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiMaps()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Konfiguracja gRPC
        builder.Services.AddSingleton(services =>
        {
            var httpHandler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                EnableMultipleHttp2Connections = true
            };

            var channel = GrpcChannel.ForAddress("http://localhost:5000", new GrpcChannelOptions
            {
                HttpHandler = httpHandler
            });

            return new DroneService.DroneServiceClient(channel);
        });

        // --- NOWOŚĆ: Rejestrujemy Symulator jako Singleton ---
        // Singleton oznacza, że działa jeden w tle przez całe życie aplikacji
        builder.Services.AddSingleton<DroneSimulatorService>();

        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<SendPackagePage>();
        builder.Services.AddTransient<TrackingPage>();

        return builder.Build();
    }
}