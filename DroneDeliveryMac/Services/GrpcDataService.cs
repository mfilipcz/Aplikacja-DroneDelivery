using Grpc.Net.Client;
using DroneServer; // Namespace z Proto (może się różnić, sprawdź w drones.proto 'option csharp_namespace')
using DroneDeliveryMac.Models;

namespace DroneDeliveryMac.Services;

public class GrpcService
{
    private readonly DroneService.DroneServiceClient _client;

    public GrpcService()
    {
        // Adres lokalny serwera.
        // Jeśli serwer ruszy na HTTP (nie HTTPS), trzeba odblokować switch:
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        
        // Porty sprawdź po uruchomieniu serwera (zazwyczaj 5000 lub 5001 dla HTTP)
        var channel = GrpcChannel.ForAddress("http://localhost:5011"); 
        _client = new DroneService.DroneServiceClient(channel);
    }

    // Pobieranie
    public async Task<List<DroneOrder>> GetOrdersAsync()
    {
        try
        {
            var response = await _client.GetOrdersAsync(new Empty());
            var list = new List<DroneOrder>();

            foreach (var msg in response.Orders)
            {
                list.Add(new DroneOrder
                {
                    Id = msg.Id,
                    OriginAddress = msg.OriginAddress,
                    OriginLat = msg.OriginLat,
                    OriginLng = msg.OriginLng,
                    DestinationAddress = msg.DestinationAddress,
                    DestLat = msg.DestLat,
                    DestLng = msg.DestLng,
                    PackageWeightKg = msg.PackageWeightKg,
                    Status = msg.Status,
                    Progress = msg.Progress,
                    IsIncoming = msg.IsIncoming,
                    CurrentLat = msg.CurrentLat,
                    CurrentLng = msg.CurrentLng,
                    // Konwersja daty string -> DateTime
                    SendDate = DateTime.Parse(msg.SendDate),
                    DeliveryDate = DateTime.Parse(msg.DeliveryDate)
                });
            }
            return list;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BŁĄD gRPC] Czy serwer jest włączony? {ex.Message}");
            return new List<DroneOrder>(); // Zwróć pustą listę jak serwer leży
        }
    }

    // Dodawanie
    public async Task AddOrderAsync(DroneOrder order)
    {
        var msg = new DroneOrderMsg
        {
            Id = order.Id,
            OriginAddress = order.OriginAddress,
            OriginLat = order.OriginLat,
            OriginLng = order.OriginLng,
            DestinationAddress = order.DestinationAddress,
            DestLat = order.DestLat,
            DestLng = order.DestLng,
            PackageWeightKg = order.PackageWeightKg,
            Status = order.Status,
            Progress = order.Progress,
            IsIncoming = order.IsIncoming,
            CurrentLat = order.CurrentLat,
            CurrentLng = order.CurrentLng,
            // Konwersja DateTime -> string
            SendDate = order.SendDate.ToString("o"),
            DeliveryDate = order.DeliveryDate.ToString("o")
        };

        try { await _client.AddOrderAsync(msg); } catch { }
    }

    // Aktualizacja (Ruch drona)
    public async Task UpdateOrderAsync(DroneOrder order)
    {
        // Budujemy wiadomość
        var msg = new DroneOrderMsg
        {
            Id = order.Id,
            OriginAddress = order.OriginAddress,
            OriginLat = order.OriginLat,
            OriginLng = order.OriginLng,
            DestinationAddress = order.DestinationAddress,
            DestLat = order.DestLat,
            DestLng = order.DestLng,
            PackageWeightKg = order.PackageWeightKg,
            Status = order.Status,
            Progress = order.Progress,
            IsIncoming = order.IsIncoming,
            CurrentLat = order.CurrentLat,
            CurrentLng = order.CurrentLng,
            // Konwersja dat
            SendDate = order.SendDate.ToString("o"),
            DeliveryDate = order.DeliveryDate.ToString("o")
        };

        try 
        { 
            // FIX: Musimy wołać UpdateOrderAsync, a NIE AddOrderAsync!
            await _client.UpdateOrderAsync(msg); 
        } 
        catch (Exception ex)
        {
            Console.WriteLine("Błąd aktualizacji: " + ex.Message);
        }
    }
}