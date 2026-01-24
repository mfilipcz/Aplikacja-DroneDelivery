using Grpc.Core;
using DroneServer;

namespace DroneDelivery;

public class DroneSimulatorService
{
    private readonly DroneService.DroneServiceClient _client;
    private readonly System.Timers.Timer _timer;
    private bool _isUpdating = false;

    public DroneSimulatorService(DroneService.DroneServiceClient client)
    {
        _client = client;
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
            var response = await _client.GetOrdersAsync(new Empty());
            var orders = response.Orders.ToList();

            foreach (var order in orders)
            {
                if (order.Status == "W drodze" || order.Status == "W locie")
                {
                    // FIX: Ruch po linii prostej (Wektory)
                    double dLat = order.DestLat - order.CurrentLat;
                    double dLng = order.DestLng - order.CurrentLng;
                    
                    // Obliczamy dystans do celu (Pitagoras)
                    double distance = Math.Sqrt(dLat * dLat + dLng * dLng);
                    double speed = 0.0015; // Prędkość przesuwania

                    if (distance < speed)
                    {
                        // Dotarł do celu
                        order.Status = "Dostarczono";
                        order.CurrentLat = order.DestLat;
                        order.CurrentLng = order.DestLng;
                        order.Progress = 1.0;
                    }
                    else
                    {
                        // Normalizacja wektora (żeby leciał prosto)
                        // Dzielimy różnicę przez dystans i mnożymy przez prędkość
                        double moveLat = (dLat / distance) * speed;
                        double moveLng = (dLng / distance) * speed;

                        order.CurrentLat += moveLat;
                        order.CurrentLng += moveLng;
                        
                        order.Progress += 0.01; 
                        if (order.Progress > 1) order.Progress = 0.99;
                    }

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