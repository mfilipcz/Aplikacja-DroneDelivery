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

        // 1. Rejestrujemy Sesję jako Singleton (wspólna dla całej aplikacji)
        builder.Services.AddSingleton<ClientSession>();

        // 2. Konfiguracja gRPC na port 5011
        builder.Services.AddSingleton(services =>
        {
            var httpHandler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                EnableMultipleHttp2Connections = true
            };

            // ZMIANA PORTU NA 5011 (zgodnie z nowym serwerem) 100.81.235.110
            var channel = GrpcChannel.ForAddress("http://127.0.0.1:5011", new GrpcChannelOptions
            {
                HttpHandler = httpHandler
            });

            return new DroneService.DroneServiceClient(channel);
        });

        builder.Services.AddSingleton<DroneSimulatorService>();
        
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<SendPackagePage>();
        builder.Services.AddTransient<TrackingPage>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<AdminPage>();

        return builder.Build();
    }
}