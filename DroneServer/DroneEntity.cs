using System.ComponentModel.DataAnnotations;

namespace DroneServer;

public class DroneEntity
{
    [Key]
    public string Id { get; set; } = "";
    public string OriginAddress { get; set; } = "";
    public double OriginLat { get; set; }
    public double OriginLng { get; set; }
    public string DestinationAddress { get; set; } = "";
    public double DestLat { get; set; }
    public double DestLng { get; set; }
    public double PackageWeightKg { get; set; }
    public string Status { get; set; } = "";
    public double Progress { get; set; }
    public bool IsIncoming { get; set; }
    public string SendDate { get; set; } = "";
    public string DeliveryDate { get; set; } = "";
    public double CurrentLat { get; set; }
    public double CurrentLng { get; set; }
    public string ClientId { get; set; } = ""; // Dodane pole ClientId
}