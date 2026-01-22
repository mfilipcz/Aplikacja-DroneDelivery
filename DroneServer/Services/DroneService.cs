using Grpc.Core;
using DroneServer; // Namespace z wygenerowanego Proto

namespace DroneServer.Services;

public class DroneApiService : DroneService.DroneServiceBase
{
    // Baza danych w pamięci (na serwerze)
    private static readonly List<DroneOrderMsg> _orders = new();
    // Śledzi które paczki już zostały oznaczone jako dostarczone (żeby nie logować wielokrotnie)
    private static readonly HashSet<string> _deliveredIds = new();
    // Liczniki klientów per platforma
    private static readonly Dictionary<string, int> _platformCounters = new();
    // Zarejestrowani klienci
    private static readonly HashSet<string> _registeredClients = new();

    public override Task<RegisterResponse> RegisterClient(ClientInfo request, ServerCallContext context)
    {
        var platform = string.IsNullOrEmpty(request.Platform) ? "Unknown" : request.Platform;
        
        // Zwiększ licznik dla tej platformy
        if (!_platformCounters.ContainsKey(platform))
            _platformCounters[platform] = 0;
        
        _platformCounters[platform]++;
        var clientId = $"{platform}{_platformCounters[platform]}";
        
        _registeredClients.Add(clientId);
        
        Console.WriteLine($"[SERWER] 🖥️  Nowy klient: {clientId} połączył się");
        
        return Task.FromResult(new RegisterResponse 
        { 
            Success = true, 
            ClientId = clientId 
        });
    }

    public override Task<OrderList> GetOrders(ClientRequest request, ServerCallContext context)
    {
        var clientId = request.ClientId;
        
        // Filtruj paczki tylko dla tego klienta
        var clientOrders = _orders.Where(o => o.ClientId == clientId).ToList();
        
        Console.WriteLine($"[SERWER] 📋 {clientId} pobrał listę paczek ({clientOrders.Count} szt.)");
        
        var response = new OrderList();
        response.Orders.AddRange(clientOrders);
        return Task.FromResult(response);
    }

    public override Task<ServerResponse> AddOrder(DroneOrderMsg request, ServerCallContext context)
    {
        // Sprawdź czy już istnieje, żeby nie dublować
        var existing = _orders.FirstOrDefault(x => x.Id == request.Id);
        
        if (existing == null)
        {
            var clientId = string.IsNullOrEmpty(request.ClientId) ? "Unknown" : request.ClientId;
            Console.WriteLine($"[SERWER] 📦 {clientId} dodał paczkę: {request.OriginAddress} → {request.DestinationAddress}");
            _orders.Add(request);
        }

        return Task.FromResult(new ServerResponse { Success = true, Message = "OK" });
    }

    public override Task<ServerResponse> UpdateOrder(DroneOrderMsg request, ServerCallContext context)
    {
        var existing = _orders.FirstOrDefault(x => x.Id == request.Id);
        if (existing != null)
        {
            _orders.Remove(existing);
            _orders.Add(request);
            
            // Loguj dostarczenie tylko RAZ (pierwsze wystąpienie)
            if (request.Status.Contains("Dostarczono") && !_deliveredIds.Contains(request.Id))
            {
                _deliveredIds.Add(request.Id);
                var clientId = string.IsNullOrEmpty(request.ClientId) ? "Unknown" : request.ClientId;
                Console.WriteLine($"[SERWER] ✅ {clientId}: Dostarczono paczkę {request.OriginAddress} → {request.DestinationAddress}");
            }
        }
        return Task.FromResult(new ServerResponse { Success = true });
    }

    public override Task<ServerResponse> DeleteOrder(DeleteRequest request, ServerCallContext context)
    {
        var existing = _orders.FirstOrDefault(x => x.Id == request.Id);
        if (existing != null)
        {
            _orders.Remove(existing);
            _deliveredIds.Remove(request.Id);
            var clientId = string.IsNullOrEmpty(request.ClientId) ? "Unknown" : request.ClientId;
            Console.WriteLine($"[SERWER] 🗑️  {clientId} usunął paczkę: {existing.OriginAddress} → {existing.DestinationAddress}");
            return Task.FromResult(new ServerResponse { Success = true, Message = "Usunięto" });
        }
        
        Console.WriteLine($"[SERWER] ⚠️  Nie znaleziono paczki do usunięcia: {request.Id.Substring(0, 8)}...");
        return Task.FromResult(new ServerResponse { Success = false, Message = "Nie znaleziono" });
    }
}