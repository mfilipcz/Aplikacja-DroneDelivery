using Grpc.Core;
using DroneServer;
using Microsoft.EntityFrameworkCore; // Potrzebne do obsługi bazy

namespace DroneServer.Services;

public class DroneApiService : DroneService.DroneServiceBase
{
    private readonly DroneDbContext _dbContext;

    // Wstrzykujemy bazę danych przez konstruktor
    public DroneApiService(DroneDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Liczniki tymczasowe w pamięci (dla prostoty rejestracji mogą zostać w RAM)
    private static readonly Dictionary<string, int> _platformCounters = new();

    public override Task<RegisterResponse> RegisterClient(ClientInfo request, ServerCallContext context)
    {
        var platform = string.IsNullOrEmpty(request.Platform) ? "Unknown" : request.Platform;
        
        lock (_platformCounters)
        {
            if (!_platformCounters.ContainsKey(platform))
                _platformCounters[platform] = 0;
            _platformCounters[platform]++;
        }
        
        var clientId = $"{platform}{_platformCounters[platform]}";
        Console.WriteLine($"[SERWER] 🖥️  Nowy klient: {clientId} połączył się");
        
        return Task.FromResult(new RegisterResponse { Success = true, ClientId = clientId });
    }

    public override async Task<OrderList> GetOrders(ClientRequest request, ServerCallContext context)
    {
        // Pobierz z Bazy Danych
        var entities = await _dbContext.Orders
            .Where(o => o.ClientId == request.ClientId)
            .ToListAsync();

        var response = new OrderList();
        
        // Mapowanie (Przepisanie z Bazy na gRPC)
        foreach (var e in entities)
        {
            response.Orders.Add(new DroneOrderMsg
            {
                Id = e.Id,
                OriginAddress = e.OriginAddress,
                OriginLat = e.OriginLat,
                OriginLng = e.OriginLng,
                DestinationAddress = e.DestinationAddress,
                DestLat = e.DestLat,
                DestLng = e.DestLng,
                PackageWeightKg = e.PackageWeightKg,
                Status = e.Status,
                Progress = e.Progress,
                IsIncoming = e.IsIncoming,
                SendDate = e.SendDate,
                DeliveryDate = e.DeliveryDate,
                CurrentLat = e.CurrentLat,
                CurrentLng = e.CurrentLng,
                ClientId = e.ClientId
            });
        }

        return response;
    }

    public override async Task<ServerResponse> AddOrder(DroneOrderMsg request, ServerCallContext context)
    {
        // Sprawdź w bazie
        var exists = await _dbContext.Orders.AnyAsync(x => x.Id == request.Id);
        
        if (!exists)
        {
            // Mapowanie z gRPC na Bazę
            var entity = new DroneEntity
            {
                Id = request.Id,
                OriginAddress = request.OriginAddress,
                OriginLat = request.OriginLat,
                OriginLng = request.OriginLng,
                DestinationAddress = request.DestinationAddress,
                DestLat = request.DestLat,
                DestLng = request.DestLng,
                PackageWeightKg = request.PackageWeightKg,
                Status = request.Status,
                Progress = request.Progress,
                IsIncoming = request.IsIncoming,
                SendDate = request.SendDate,
                DeliveryDate = request.DeliveryDate,
                CurrentLat = request.CurrentLat,
                CurrentLng = request.CurrentLng,
                ClientId = request.ClientId
            };

            _dbContext.Orders.Add(entity);
            await _dbContext.SaveChangesAsync(); // Zapis na dysk
            
            Console.WriteLine($"[SERWER] 📦 Dodano paczkę do bazy: {request.Id}");
        }

        return new ServerResponse { Success = true, Message = "OK" };
    }

    public override async Task<ServerResponse> UpdateOrder(DroneOrderMsg request, ServerCallContext context)
    {
        // Pobierz rekord do edycji
        var entity = await _dbContext.Orders.FirstOrDefaultAsync(x => x.Id == request.Id);
        
        if (entity != null)
        {
            // Aktualizuj pola
            entity.CurrentLat = request.CurrentLat;
            entity.CurrentLng = request.CurrentLng;
            entity.Status = request.Status;
            entity.Progress = request.Progress;

            await _dbContext.SaveChangesAsync(); // Zapisz zmiany w pliku
            
            if (entity.Status.Contains("Dostarczono"))
            {
                 // Tu można dodać logikę jednorazowego logowania, jeśli potrzebna
            }
        }
        return new ServerResponse { Success = true };
    }

    public override async Task<ServerResponse> DeleteOrder(DeleteRequest request, ServerCallContext context)
    {
        var entity = await _dbContext.Orders.FirstOrDefaultAsync(x => x.Id == request.Id);
        if (entity != null)
        {
            _dbContext.Orders.Remove(entity);
            await _dbContext.SaveChangesAsync();
            return new ServerResponse { Success = true, Message = "Usunięto" };
        }
        return new ServerResponse { Success = false, Message = "Nie znaleziono" };
    }
}