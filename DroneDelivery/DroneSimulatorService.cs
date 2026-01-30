using Grpc.Core;
using DroneServer;

namespace DroneDelivery;

public class DroneSimulatorService
{
    private readonly DroneService.DroneServiceClient _client;
    private readonly ClientSession _session; // Dodajemy sesję
    private readonly System.Timers.Timer _timer;
    private bool _isUpdating = false;

    // Wstrzykujemy ClientSession
    public DroneSimulatorService(DroneService.DroneServiceClient client, ClientSession session)
    {
        _client = client;
        _session = session;
        
        _timer = new System.Timers.Timer(500);
        _timer.Elapsed += async (s, e) => await Tick();
        _timer.Start();
    }

    private async Task Tick()
    {
        if (_isUpdating) return;
        _isUpdating = true;

        try
        {
            // 1. AUTOMATYCZNA REJESTRACJA (Jeśli nie mamy ID)
            if (!_session.IsRegistered)
            {
                try 
                {
                    var regResponse = await _client.RegisterClientAsync(new ClientInfo { Platform = "Mac" });
                    if (regResponse.Success)
                    {
                        _session.ClientId = regResponse.ClientId;
                        Console.WriteLine($"[Klient] Zarejestrowano jako: {_session.ClientId}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Klient] Błąd rejestracji: {ex.Message}");
                    return; // Bez ID nie możemy pobrać paczek
                }
            }

            // 2. Pobieramy stan (używając ClientId)
            var request = new ClientRequest { ClientId = _session.ClientId };
            var response = await _client.GetOrdersAsync(request);
            var orders = response.Orders.ToList();

            // 3. Obliczamy ruch
            foreach (var order in orders)
            {
                if (order.Status == "W drodze" || order.Status == "W locie")
                {
                    double dLat = order.DestLat - order.CurrentLat;
                    double dLng = order.DestLng - order.CurrentLng;
                    double distance = Math.Sqrt(dLat * dLat + dLng * dLng);
                    double speed = 0.0015;

                    if (distance < speed)
                    {
                        order.Status = "Dostarczono";
                        order.CurrentLat = order.DestLat;
                        order.CurrentLng = order.DestLng;
                        order.Progress = 1.0;
                    }
                    else
                    {
                        double moveLat = (dLat / distance) * speed;
                        double moveLng = (dLng / distance) * speed;

                        order.CurrentLat += moveLat;
                        order.CurrentLng += moveLng;
                        
                        order.Progress += 0.02;
                        if (order.Progress > 1) order.Progress = 0.99;
                    }

                    // Przy aktualizacji też wysyłamy ClientId (serwer tego nie wymagał w Update, ale warto mieć w obiekcie)
                    order.ClientId = _session.ClientId; 
                    await _client.UpdateOrderAsync(order);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Simulator] Błąd: {ex.Message}");
        }
        finally
        {
            _isUpdating = false;
        }
    }
}