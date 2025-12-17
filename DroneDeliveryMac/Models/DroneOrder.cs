using SQLite;

namespace DroneDeliveryMac.Models;

public class DroneOrder
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    public string OriginAddress { get; set; } = string.Empty;
    public double OriginLat { get; set; }
    public double OriginLng { get; set; }

    public string DestinationAddress { get; set; } = string.Empty;
    public double DestLat { get; set; }
    public double DestLng { get; set; }

    public double PackageWeightKg { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime SendDate { get; set; }
    public DateTime DeliveryDate { get; set; }
    public bool IsIncoming { get; set; } = false;

    public string Status { get; set; } = "Oczekiwanie";
    public double Progress { get; set; } = 0.0;
    public double BatteryLevel { get; set; } = 100.0;

    public double CurrentLat { get; set; }
    public double CurrentLng { get; set; }
}