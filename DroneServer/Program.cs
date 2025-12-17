using DroneServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Dodaj usługi gRPC
builder.Services.AddGrpc();

var app = builder.Build();

// Skonfiguruj serwis
app.MapGrpcService<DroneApiService>();
app.MapGet("/", () => "Serwer Dronów działa! Użyj klienta gRPC.");

app.Run();