using DroneServer.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core; // Konieczne dla konfiguracji portu

var builder = WebApplication.CreateBuilder(args);

// --- NAPRAWA: Wymuszamy Port 5000 i HTTP/2 ---
// To jest ten fragment, którego brakowało i przez to miałeś błędy.
// Dzięki temu omijamy "zombie" na porcie 5011 i pasujemy do klienta.
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5000, o => o.Protocols = HttpProtocols.Http2);
});
// ---------------------------------------------

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<DroneApiService>();
app.MapGet("/", () => "Serwer Dronów działa (Pamięć RAM, Port 5000, HTTP/2)!");

app.Run();