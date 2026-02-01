using Grpc.Core;
using DroneServer;

namespace DroneDelivery;

public class DroneSimulatorService
{
    private readonly DroneService.DroneServiceClient _client;
    private readonly ClientSession _session;
    private readonly System.Timers.Timer _timer;
    
    // Blokada, żeby nie uruchamiać aktualizacji, jeśli poprzednia jeszcze trwa
    private bool _isUpdating = false;

    public DroneSimulatorService(DroneService.DroneServiceClient client, ClientSession session)
    {
        _client = client;
        _session = session;
        
        // Timer co 500ms (pół sekundy) - płynny ruch
        _timer = new System.Timers.Timer(500);
        _timer.Elapsed += async (s, e) => await Tick();
        _timer.Start();
    }

    private async Task Tick()
    {
        // Jeśli aktualizacja trwa lub użytkownik nie jest zalogowany -> nic nie rób
        if (_isUpdating || !_session.IsRegistered) return;
        
        _isUpdating = true;

        try
        {
            // 1. Pobieramy stan paczek z serwera dla zalogowanego użytkownika
            var request = new ClientRequest { ClientId = _session.ClientId };
            var response = await _client.GetOrdersAsync(request);
            var orders = response.Orders.ToList();

            foreach (var order in orders)
            {
                // --- KLUCZOWA ZMIANA ---
                // Symulujemy ruch TYLKO dla paczek zatwierdzonych ("W drodze").
                // Jeśli paczka ma status "Oczekuje na zatwierdzenie", dron stoi w miejscu.
                if (order.Status == "W drodze")
                {
                    double dLat = order.DestLat - order.CurrentLat;
                    double dLng = order.DestLng - order.CurrentLng;
                    
                    // Obliczamy dystans
                    double distance = Math.Sqrt(dLat * dLat + dLng * dLng);
                    double speed = 0.0015; // Prędkość drona na cykl

                    // Jeśli jesteśmy bardzo blisko celu -> Dostarczono
                    if (distance < speed)
                    {
                        order.Status = "Dostarczono";
                        order.CurrentLat = order.DestLat;
                        order.CurrentLng = order.DestLng;
                        order.Progress = 1.0;
                    }
                    else
                    {
                        // Przesuwamy drona w stronę celu
                        double moveLat = (dLat / distance) * speed;
                        double moveLng = (dLng / distance) * speed;

                        order.CurrentLat += moveLat;
                        order.CurrentLng += moveLng;
                        
                        // Aktualizujemy pasek postępu (matematyczny)
                        order.Progress += 0.02;
                        if (order.Progress > 1) order.Progress = 0.99;
                    }

                    // Przypisujemy ClientId (wymagane przez Entity Framework do identyfikacji)
                    order.ClientId = _session.ClientId; 
                    
                    // Wysyłamy nową pozycję do bazy danych
                    await _client.UpdateOrderAsync(order);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Simulator] Błąd pętli: {ex.Message}");
        }
        finally
        {
            _isUpdating = false;
        }
    }
}