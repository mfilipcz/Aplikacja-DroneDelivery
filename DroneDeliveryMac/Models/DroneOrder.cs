using System.ComponentModel;
using System.Runtime.CompilerServices;
using SQLite; // Jeśli nadal używasz atrybutów SQLite, zostaw. Jeśli nie, można usunąć.

namespace DroneDeliveryMac.Models;

// Dodajemy INotifyPropertyChanged, żeby UI widziało zmiany w czasie rzeczywistym
public class DroneOrder : INotifyPropertyChanged
{
    private string _status = "Oczekiwanie";
    private double _progress = 0.0;
    private double _currentLat;
    private double _currentLng;

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

    // --- WŁAŚCIWOŚCI POWIADAMIAJĄCE WIDOK ---

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (_progress != value)
            {
                _progress = value;
                OnPropertyChanged();
            }
        }
    }

    public double CurrentLat
    {
        get => _currentLat;
        set { _currentLat = value; OnPropertyChanged(); }
    }

    public double CurrentLng
    {
        get => _currentLng;
        set { _currentLng = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}