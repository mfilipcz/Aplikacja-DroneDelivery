using Grpc.Net.Client;
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
    
    // Session data (in-memory)
    public string Username { get; private set; } = "";
    public string Role { get; private set; } = "";
    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);
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
                Username = response.ClientId;
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
            var response = await _client.RegisterUserAsync(new RegisterUserRequest { Username = username, Password = password });
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

    // User Management

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

    // Orders

    // Admin: Get all
    public async Task<List<DroneOrder>> GetAllOrdersAsync()
    {
        if (!IsLoggedIn || !IsAdmin) return new List<DroneOrder>();
        
        try
        {
            var response = await _client.GetAllOrdersAsync(new Empty());
            return MapToModelList(response.Orders);
        }
        catch
        {
            return new List<DroneOrder>();
        }
    }

    // Admin: Update status
    public async Task<bool> UpdateOrderStatusAsync(string orderId, string newStatus)
    {
        if (!IsAdmin) return false;
        try
        {
            var response = await _client.UpdateOrderStatusAsync(new StatusRequest 
            { 
                OrderId = orderId, 
                NewStatus = newStatus, 
                IsAdmin = true 
            });
            return response.Success;
        }
        catch
        {
            return false;
        }
    }

    // User: Get own orders
    public async Task<List<DroneOrder>> GetOrdersAsync()
    {
        if (!IsLoggedIn) return new List<DroneOrder>();

        try
        {
            // Send Username as clientId
            var response = await _client.GetOrdersAsync(new ClientRequest { ClientId = Username });
            return MapToModelList(response.Orders);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BŁĄD gRPC] {ex.Message}");
            return new List<DroneOrder>();
        }
    }

    private List<DroneOrder> MapToModelList(IEnumerable<DroneOrderMsg> msgs)
    {
        var list = new List<DroneOrder>();
        foreach (var msg in msgs)
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

    public async Task AddOrderAsync(DroneOrder order)
    {
        if (!IsLoggedIn) return;
        
        var msg = MapToMsg(order);
        msg.ClientId = Username; // Assign current user
        // Default status
        
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
