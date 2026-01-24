using Grpc.Core;
using DroneServer; // Namespace z wygenerowanego Proto

namespace DroneServer.Services;

public class DroneApiService : DroneService.DroneServiceBase
{
    // Baza danych w pamięci (na serwerze)
    private static readonly List<DroneOrderMsg> _orders = new();

    public override Task<OrderList> GetOrders(Empty request, ServerCallContext context)
    {
        var response = new OrderList();
        response.Orders.AddRange(_orders);
        return Task.FromResult(response);
    }

    public override Task<ServerResponse> AddOrder(DroneOrderMsg request, ServerCallContext context)
        {
            // FIX: Sprawdź czy już istnieje, żeby nie dublować
            var existing = _orders.FirstOrDefault(x => x.Id == request.Id);
            
            if (existing == null)
            {
                Console.WriteLine($"[SERWER] Dodano nowe ID: {request.Id}");
                _orders.Add(request);
            }
            else
            {
                Console.WriteLine($"[SERWER] Próba dublowania ID: {request.Id} - Ignoruję.");
            }

            return Task.FromResult(new ServerResponse { Success = true, Message = "OK" });
        }

    public override Task<ServerResponse> UpdateOrder(DroneOrderMsg request, ServerCallContext context)
    {
        var existing = _orders.FirstOrDefault(x => x.Id == request.Id);
        if (existing != null)
        {
            _orders.Remove(existing);
            _orders.Add(request); // Podmieniamy na nowszą wersję
            // Console.WriteLine($"[SERWER] Aktualizacja pozycji drona: {request.Progress:P0}");
        }
        return Task.FromResult(new ServerResponse { Success = true });
    }
}