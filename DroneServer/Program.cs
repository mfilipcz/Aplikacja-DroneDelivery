using DroneServer.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore; // WAŻNE
using DroneServer;

var builder = WebApplication.CreateBuilder(args);

// 1. Konfiguracja Kestrel (Port 5011, HTTP/2) - TO ZOSTAJE BEZ ZMIAN
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5011, o => o.Protocols = HttpProtocols.Http2);
});

// 2. Rejestracja Bazy Danych SQLite
builder.Services.AddDbContext<DroneDbContext>(options =>
    options.UseSqlite("Data Source=drones.db"));

builder.Services.AddGrpc();

var app = builder.Build();

// 3. Automatyczna migracja (utworzenie pliku .db przy starcie)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DroneDbContext>();
    db.Database.EnsureCreated();
}

app.MapGrpcService<DroneApiService>();
app.MapGet("/", () => "Serwer Dronów z SQLite działa!");

app.Run();