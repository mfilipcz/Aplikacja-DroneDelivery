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

    // Klucz, pod którym zapisujemy ID w pamięci telefonu
    private const string PREF_CLIENT_ID = "moje_id_klienta_v2";

    public DroneSimulatorService(DroneService.DroneServiceClient client, ClientSession session)
    {
        _client = client;
        _session = session;
        
        // Timer co 500ms (pół sekundy)
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
            // --- 1. LOGIKA TOŻSAMOŚCI (Zapamiętywanie ID) ---
            if (!_session.IsRegistered)
            {
                // A. Sprawdzamy, czy mamy zapisane ID w pamięci telefonu
                string savedId = Preferences.Get(PREF_CLIENT_ID, null);

                if (!string.IsNullOrEmpty(savedId))
                {
                    // MAMY ID! Przywracamy je bez pytania serwera
                    _session.ClientId = savedId;
                    // Console.WriteLine($"[Klient] Witaj ponownie! Przywrócono ID: {savedId}");
                }
                else
                {
                    // B. Nie mamy ID (pierwsze uruchomienie). Rejestrujemy się na serwerze.
                    try 
                    {
                        var regResponse = await _client.RegisterClientAsync(new ClientInfo { Platform = "Mac" });
                        if (regResponse.Success)
                        {
                            _session.ClientId = regResponse.ClientId;
                            
                            // WAŻNE: Zapisujemy nowe ID w pamięci telefonu na stałe
                            Preferences.Set(PREF_CLIENT_ID, _session.ClientId);
                            
                            Console.WriteLine($"[Klient] Zarejestrowano nowe ID: {_session.ClientId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Klient] Błąd rejestracji: {ex.Message}");
                        return; // Bez ID nie możemy nic zrobić, kończymy cykl
                    }
                }
            }

            // --- 2. LOGIKA SYMULACJI (Ruch Dronów) ---
            
            // Pobieramy stan paczek z serwera (Database) dla naszego ID
            var request = new ClientRequest { ClientId = _session.ClientId };
            var response = await _client.GetOrdersAsync(request);
            var orders = response.Orders.ToList();

            foreach (var order in orders)
            {
                // Symulujemy tylko paczki, które są w ruchu
                if (order.Status == "W drodze" || order.Status == "W locie")
                {
                    double dLat = order.DestLat - order.CurrentLat;
                    double dLng = order.DestLng - order.CurrentLng;
                    
                    // Obliczamy dystans (prosta euklidesowa dla małych odległości wystarczy do symulacji)
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
                        
                        // Aktualizujemy pasek postępu (chociaż UI i tak liczy go z GPS)
                        order.Progress += 0.02;
                        if (order.Progress > 1) order.Progress = 0.99;
                    }

                    // Ważne: Przypisujemy ClientId przed wysłaniem aktualizacji, 
                    // żeby serwer wiedział, czyja to paczka (Entity Framework tego wymaga przy update)
                    order.ClientId = _session.ClientId; 
                    
                    // Wysyłamy nową pozycję do bazy danych
                    await _client.UpdateOrderAsync(order);
                }
            }
        }
        catch (Exception ex)
        {
            // Ignorujemy błędy połączenia w pętli (żeby aplikacja nie crashowała przy braku neta)
            Console.WriteLine($"[Simulator] Błąd pętli: {ex.Message}");
        }
        finally
        {
            _isUpdating = false;
        }
    }
}