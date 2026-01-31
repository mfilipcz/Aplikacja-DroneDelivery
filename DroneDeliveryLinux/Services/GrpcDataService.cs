using Grpc.Net.Client;
using DroneServer;
using DroneDeliveryLinux.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Threading;

namespace DroneDeliveryLinux.Services;

public class GrpcDataService
{
    private readonly DroneService.DroneServiceClient _client;
    
    // Dane sesji (w pamięci)
    public string Username { get; private set; } = "";
    public string Role { get; private set; } = "";
    public bool IsAdmin => Role == "Admin";
    public bool IsLoggedIn => !string.IsNullOrEmpty(Username);

    public GrpcDataService()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        
        var httpHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
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

    // --- AUTH ---

    public async Task<(bool success, string message)> LoginAsync(string username, string password)
    {
        try
        {
            var response = await _client.LoginAsync(new LoginRequest { Username = username, Password = password });
            if (response.Success)
            {
                Username = response.Username;
                Role = response.Role;
                return (true, "Zalogowano pomyślnie");
            }
            return (false, response.Message);
        }
        catch (Exception ex)
        {
            return (false, $"Błąd połączenia: {ex.Message}");
        }
    }

    public async Task<(bool success, string message)> RegisterAsync(string username, string password)
    {
        try
        {
            var response = await _client.RegisterUserAsync(new RegisterRequest { Username = username, Password = password });
            return (response.Success, response.Message);
        }
        catch (Exception ex)
        {
            return (false, $"Błąd rejestracji: {ex.Message}");
        }
    }

    public void Logout()
    {
        Username = "";
        Role = "";
    }

    // --- USER MANAGEMENT (Admin) ---

    public async Task<List<UserMsg>> GetAllUsersAsync()
    {
        try
        {
            var response = await _client.GetAllUsersAsync(new Empty());
            return new List<UserMsg>(response.Users);
        }
        catch
        {
            return new List<UserMsg>();
        }
    }

    public async Task<bool> DeleteUserAsync(string username)
    {
        try
        {
            var response = await _client.DeleteUserAsync(new UserRequest { Username = username });
            return response.Success;
        }
        catch
        {
            return false;
        }
    }

    // --- ORDERS ---

    // Metoda RegisterAsync (stara) została zastąpiona przez logowanie.
    // Usuwamy starą metodę RegisterAsync() która używała ClientInfo.

    public async Task<List<DroneOrder>> GetOrdersAsync()
    {
        if (!IsLoggedIn) return new List<DroneOrder>();

        try
        {
            // Wysyłamy Username jako clientId oraz Rolę
            var response = await _client.GetOrdersAsync(new ClientRequest { ClientId = Username, Role = Role });
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
                    DeliveryDate = DateTime.Parse(msg.DeliveryDate),
                    Username = msg.ClientId
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
        if (!IsLoggedIn) return;
        
        var msg = MapToMsg(order);
        msg.ClientId = Username; // Przypisz aktualnego usera
        // Status ustawiamy na "Oczekuje na zatwierdzenie" (lub inny, ale serwer i tak to nadpisze dla Usera)
        
        try { await _client.AddOrderAsync(msg); } catch { }
    }

    public async Task UpdateOrderAsync(DroneOrder order)
    {
        var msg = MapToMsg(order);
        msg.ClientId = Username;
        try { await _client.UpdateOrderAsync(msg); } catch { }
    }

    public async Task<bool> DeleteOrderAsync(string orderId)
    {
        try 
        { 
            var response = await _client.DeleteOrderAsync(new DeleteRequest 
            { 
                Id = orderId,
                ClientId = Username
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
            ClientId = Username
        };
    }
}
