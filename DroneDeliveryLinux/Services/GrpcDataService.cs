using Grpc.Net.Client;
using DroneServer;
using DroneDeliveryLinux.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Threading;
using System.IO;

namespace DroneDeliveryLinux.Services;

public class GrpcDataService
{
    private readonly DroneService.DroneServiceClient _client;
    private string _clientId = "";
    private const string ClientIdFile = "client_id.txt";
    
    public string ClientId => _clientId;

    public GrpcDataService()
    {
        // Adres lokalny serwera.
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        
        var httpHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            // To jest kluczowe dla HTTP/2 bez TLS na localhost
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30)
        };

        var channel = GrpcChannel.ForAddress("http://localhost:5011", new GrpcChannelOptions
        {
            HttpHandler = httpHandler
        });
        _client = new DroneService.DroneServiceClient(channel);
    }

    /// <summary>
    /// Rejestruje klienta na serwerze i otrzymuje unikalny identyfikator
    /// </summary>
    public async Task<bool> RegisterAsync()
    {
        try
        {
            // 1. Sprawdź, czy mamy zapisane ID
            if (File.Exists(ClientIdFile))
            {
                var savedId = await File.ReadAllTextAsync(ClientIdFile);
                if (!string.IsNullOrWhiteSpace(savedId))
                {
                    _clientId = savedId.Trim();
                    Console.WriteLine($"[KLIENT] Przywrócono ID: {_clientId}");
                    return true;
                }
            }

            // 2. Jeśli nie, zarejestruj nowe
            var response = await _client.RegisterClientAsync(new ClientInfo { Platform = "Linux" });
            if (response.Success)
            {
                _clientId = response.ClientId;
                Console.WriteLine($"[KLIENT] Zarejestrowano jako: {_clientId}");
                
                // 3. Zapisz ID do pliku
                await File.WriteAllTextAsync(ClientIdFile, _clientId);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BŁĄD] Nie można połączyć z serwerem: {ex.Message}");
            return false;
        }
    }

    public async Task<List<DroneOrder>> GetOrdersAsync()
    {
        try
        {
            var response = await _client.GetOrdersAsync(new ClientRequest { ClientId = _clientId });
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
                    SendDate = DateTime.Parse(msg.SendDate),
                    DeliveryDate = DateTime.Parse(msg.DeliveryDate)
                });
            }
            return list;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BŁĄD gRPC] {ex.Message}");
            return new List<DroneOrder>();
        }
    }

    public async Task AddOrderAsync(DroneOrder order)
    {
        var msg = MapToMsg(order);
        msg.ClientId = _clientId;  // Przypisz klienta do paczki
        try { await _client.AddOrderAsync(msg); } catch { }
    }

    public async Task UpdateOrderAsync(DroneOrder order)
    {
        var msg = MapToMsg(order);
        msg.ClientId = _clientId;
        try { await _client.UpdateOrderAsync(msg); } catch { }
    }

    public async Task<bool> DeleteOrderAsync(string orderId)
    {
        try 
        { 
            var response = await _client.DeleteOrderAsync(new DeleteRequest 
            { 
                Id = orderId,
                ClientId = _clientId
            });
            return response.Success;
        } 
        catch 
        { 
            return false; 
        }
    }

    private DroneOrderMsg MapToMsg(DroneOrder order)
    {
        return new DroneOrderMsg
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
            SendDate = order.SendDate.ToString("o"),
            DeliveryDate = order.DeliveryDate.ToString("o"),
            ClientId = _clientId
        };
    }
}
