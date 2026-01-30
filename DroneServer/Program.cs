using DroneServer.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Wymuś HTTP/2 na porcie 5011 dla gRPC
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5011, o => o.Protocols = HttpProtocols.Http2);
});

// Dodaj usługi gRPC
builder.Services.AddGrpc();

var app = builder.Build();

// Skonfiguruj serwis
app.MapGrpcService<DroneApiService>();
app.MapGet("/", () => "Serwer Dronów działa! Użyj klienta gRPC.");

app.Run();