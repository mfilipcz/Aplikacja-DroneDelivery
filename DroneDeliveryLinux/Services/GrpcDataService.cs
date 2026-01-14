using Grpc.Net.Client;
using DroneServer;
using DroneDeliveryLinux.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DroneDeliveryLinux.Services;

public class GrpcDataService
{
    private readonly DroneService.DroneServiceClient _client;

    public GrpcDataService()
    {
        // Adres lokalny serwera.
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        
        var channel = GrpcChannel.ForAddress("http://localhost:5011"); 
        _client = new DroneService.DroneServiceClient(channel);
    }

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
        try { await _client.AddOrderAsync(msg); } catch { }
    }

    public async Task UpdateOrderAsync(DroneOrder order)
    {
        var msg = MapToMsg(order);
        try { await _client.UpdateOrderAsync(msg); } catch { }
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
            DeliveryDate = order.DeliveryDate.ToString("o")
        };
    }
}