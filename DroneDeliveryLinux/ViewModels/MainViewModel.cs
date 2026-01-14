using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DroneDeliveryLinux.Models;
using DroneDeliveryLinux.Services;
using Avalonia.Threading;

namespace DroneDeliveryLinux.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly GrpcDataService _grpcService = new();
    private readonly DatabaseService _dbService = new();
    
    private static readonly Dictionary<string, DroneOrder> ActiveMissions = new();

    [ObservableProperty]
    private ObservableCollection<DroneOrder> _outgoingOrders = new();

    [ObservableProperty]
    private ObservableCollection<DroneOrder> _incomingOrders = new();

    [ObservableProperty]
    private string _entryOrigin = "";

    [ObservableProperty]
    private string _entryDest = "";

    [ObservableProperty]
    private double _sliderWeight = 1.0;

    [ObservableProperty]
    private string _labelCost = "Koszt: 17.00 PLN";

    [ObservableProperty]
    private DateTimeOffset _sendDate = DateTimeOffset.Now;

    [ObservableProperty]
    private DateTimeOffset _deliverDate = DateTimeOffset.Now.AddDays(1);

    public MainViewModel()
    {
        _ = LoadOrdersAsync();
        
        // Timer do odświeżania (odpowiednik _uiTimer w MAUI)
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        timer.Tick += (s, e) => { /* Tutaj można dodać logikę odświeżania mapy jeśli będzie potrzebna */ };
        timer.Start();
    }

    private async Task LoadOrdersAsync()
    {
        var orders = await _grpcService.GetOrdersAsync();
        
        Dispatcher.UIThread.Post(() => {
            OutgoingOrders.Clear();
            IncomingOrders.Clear();
            
            foreach (var order in orders)
            {
                DroneOrder liveOrder = order;
                lock (ActiveMissions)
                {
                    if (ActiveMissions.ContainsKey(order.Id)) liveOrder = ActiveMissions[order.Id];
                }

                if (liveOrder.IsIncoming) IncomingOrders.Add(liveOrder);
                else OutgoingOrders.Add(liveOrder);

                if (liveOrder.Status != "✅ Dostarczono")
                {
                    _ = Task.Run(() => StartDroneMission(liveOrder));
                }
            }
        });
    }

    [RelayCommand]
    private async Task SendPackage()
    {
        if (string.IsNullOrWhiteSpace(EntryOrigin) || string.IsNullOrWhiteSpace(EntryDest)) return;

        // Uproszczone współrzędne dla Linuxa (bez Geocodingu na razie)
        // W przyszłości można dodać HttpClient do Nominatim
        var order = new DroneOrder
        {
            OriginAddress = EntryOrigin,
            OriginLat = 52.23, OriginLng = 21.01, // Warszawa
            DestinationAddress = EntryDest,
            DestLat = 50.06, DestLng = 19.94, // Kraków
            PackageWeightKg = SliderWeight,
            SendDate = SendDate.DateTime,
            DeliveryDate = DeliverDate.DateTime,
            Status = "Inicjalizacja...",
            Progress = 0.0
        };

        await _grpcService.AddOrderAsync(order);
        OutgoingOrders.Add(order);
        
        _ = Task.Run(() => StartDroneMission(order));
        
        EntryOrigin = "";
        EntryDest = "";
    }

    private async Task StartDroneMission(DroneOrder o)
    {
        lock (ActiveMissions)
        {
            if (ActiveMissions.ContainsKey(o.Id)) return;
            ActiveMissions.Add(o.Id, o);
        }

        try
        {
            int totalSteps = 200; // Mniej kroków dla testów
            for (int i = (int)(o.Progress * totalSteps); i <= totalSteps; i++)
            {
                await Task.Delay(100);
                double pct = (double)i / totalSteps;
                
                o.Progress = pct;
                o.CurrentLat = o.OriginLat + (o.DestLat - o.OriginLat) * pct;
                o.CurrentLng = o.OriginLng + (o.DestLng - o.OriginLng) * pct;
                
                if (pct < 1.0) o.Status = $"W locie... {pct:P0}";
                else o.Status = "✅ Dostarczono";

                if (i % 20 == 0) await _grpcService.UpdateOrderAsync(o);
            }
            await _grpcService.UpdateOrderAsync(o);
        }
        catch { }
        finally
        {
            lock (ActiveMissions) { ActiveMissions.Remove(o.Id); }
        }
    }
}