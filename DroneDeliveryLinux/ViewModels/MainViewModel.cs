using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DroneDeliveryLinux.Models;
using DroneDeliveryLinux.Services;
using Avalonia.Threading;

namespace DroneDeliveryLinux.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly GrpcDataService _grpcService;
    private readonly DatabaseService _dbService = new();
    private static readonly HttpClient _httpClient = new();
    
    private static readonly Dictionary<string, DroneOrder> ActiveMissions = new();
    private readonly Action _onLogout;

    // Zdarzenie wywoływane po dodaniu nowego zamówienia
    public event Action<DroneOrder>? OrderAdded;
    public event Action<DroneOrder>? OrderDeleted;
    public event Action<string>? ErrorOccurred;

    // ID klienta (Username)
    public string ClientId => _grpcService.Username;

    [ObservableProperty]
    private ObservableCollection<DroneOrder> _allOrders = new();

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
    private string _labelCost = "Koszt: 57.00 PLN";

    [ObservableProperty]
    private DateTime _sendDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _deliverDate = DateTime.Today.AddDays(2);

    partial void OnSliderWeightChanged(double value) => RecalculateCost();
    partial void OnSendDateChanged(DateTime value)
    {
        if (value.Date < DateTime.Today) { SendDate = DateTime.Today; return; }
        RecalculateCost();
    }
    partial void OnDeliverDateChanged(DateTime value)
    {
        if (value.Date < DateTime.Today) { DeliverDate = DateTime.Today; return; }
        RecalculateCost();
    }

    private void RecalculateCost()
    {
        double weight = SliderWeight;
        var sendDateOnly = DateOnly.FromDateTime(SendDate);
        var deliverDateOnly = DateOnly.FromDateTime(DeliverDate);
        int days = deliverDateOnly.DayNumber - sendDateOnly.DayNumber;
        
        if (days < 0) { LabelCost = "Błąd daty"; return; }
        
        // Algorytm zgodny z Mac
        decimal weightCost = (decimal)weight * 10.0m;
        decimal speedCost = 50.0m / (days + 1);
        decimal basePrice = 20.0m;
        decimal totalPrice = basePrice + weightCost + speedCost;
        
        LabelCost = $"Koszt: {totalPrice:F2} PLN";
    }

    public MainViewModel(GrpcDataService grpcService, Action onLogout)
    {
        _grpcService = grpcService;
        _onLogout = onLogout;
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "DroneDeliveryLinux/1.0");
        RecalculateCost();
        
        // Startujemy ładowanie paczek
        _ = LoadOrdersLoop();
    }

    private async Task LoadOrdersLoop()
    {
        while (_grpcService.IsLoggedIn)
        {
            await LoadOrdersAsync();
            await Task.Delay(1000); // Polling co 1s
        }
    }

    public async Task LoadOrdersAsync()
    {
        var orders = await _grpcService.GetOrdersAsync();
        
        Dispatcher.UIThread.Post(() => {
            // Sync lists logic could be better, but sticking to clear/add for simplicity with "ActiveMissions" preservation
            // Actually, clearing breaks "ActiveMissions" if we rely on object reference.
            // Let's implement smart sync to support polling.
            
            // 1. Add/Update
            foreach (var fetched in orders)
            {
                var existing = AllOrders.FirstOrDefault(x => x.Id == fetched.Id);
                if (existing != null)
                {
                    // Update fields
                    existing.Status = fetched.Status;
                    existing.CurrentLat = fetched.CurrentLat;
                    existing.CurrentLng = fetched.CurrentLng;
                    existing.Progress = fetched.Progress;
                }
                else
                {
                    AllOrders.Add(fetched);
                    if (fetched.IsIncoming) IncomingOrders.Add(fetched);
                    else OutgoingOrders.Add(fetched);
                }

                // Check for simulation start
                var liveOrder = existing ?? fetched;
                if (liveOrder.Status != "✅ Dostarczono" && liveOrder.Status != "Oczekuje na zatwierdzenie")
                {
                    lock (ActiveMissions)
                    {
                        if (!ActiveMissions.ContainsKey(liveOrder.Id))
                        {
                            _ = Task.Run(() => StartDroneMission(liveOrder));
                        }
                    }
                }
            }

            // 2. Remove deleted
            var toRemove = AllOrders.Where(local => !orders.Any(remote => remote.Id == local.Id)).ToList();
            foreach (var item in toRemove)
            {
                AllOrders.Remove(item);
                OutgoingOrders.Remove(item);
                IncomingOrders.Remove(item);
                
                lock (ActiveMissions) { ActiveMissions.Remove(item.Id); }
                OrderDeleted?.Invoke(item);
            }
        });
    }

    [RelayCommand]
    private void Logout()
    {
        _grpcService.Logout();
        _onLogout();
    }

    public async Task DeleteOrderAsync(DroneOrder order)
    {
        var success = await _grpcService.DeleteOrderAsync(order.Id);
        // Polling zaktualizuje listę
    }

    private async Task<(double lat, double lng)?> GeocodeAddressAsync(string address)
    {
        // ... (skrócone dla czytelności, logika bez zmian)
        try {
            var full = address.Contains("Warszawa") ? address : $"{address}, Warszawa, Polska";
            var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(full)}&format=json&limit=1";
            var json = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetArrayLength() > 0) {
                var el = doc.RootElement[0];
                return (double.Parse(el.GetProperty("lat").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                        double.Parse(el.GetProperty("lon").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
            }
        } catch {}
        return null;
    }

    [RelayCommand]
    private async Task SendPackage()
    {
        if (string.IsNullOrWhiteSpace(EntryOrigin) || string.IsNullOrWhiteSpace(EntryDest)) return;

        var origin = await GeocodeAddressAsync(EntryOrigin);
        var dest = await GeocodeAddressAsync(EntryDest);

        if (origin == null || dest == null)
        {
            ErrorOccurred?.Invoke("Nie znaleziono adresu.");
            return;
        }

        var order = new DroneOrder
        {
            OriginAddress = EntryOrigin, OriginLat = origin.Value.lat, OriginLng = origin.Value.lng,
            DestinationAddress = EntryDest, DestLat = dest.Value.lat, DestLng = dest.Value.lng,
            CurrentLat = origin.Value.lat, CurrentLng = origin.Value.lng,
            PackageWeightKg = SliderWeight,
            SendDate = SendDate, DeliveryDate = DeliverDate,
            Status = "Oczekuje na zatwierdzenie", // Domyślny status
            Progress = 0.0,
            IsIncoming = false
        };

        await _grpcService.AddOrderAsync(order);
        
        // Dodaj lokalnie dla UI (polling to potwierdzi)
        AllOrders.Add(order);
        OutgoingOrders.Add(order);
        OrderAdded?.Invoke(order); // Przełącz na mapę
        
        // NIE startujemy misji - czekamy na admina
        
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
            // Jeśli status nie jest jeszcze "W drodze" (np. Admin zmienił), ustawiamy.
            // Ale uwaga: Jeśli user sam to uruchomił, to znaczy że status JUŻ JEST "W drodze" (z loadera)
            
            while (true)
            {
                // Jeśli status zmienił się na "Dostarczono" (np. przez innego klienta/admina), przerwij
                if (o.Status.Contains("Dostarczono")) break;

                double dLat = o.DestLat - o.CurrentLat;
                double dLng = o.DestLng - o.CurrentLng;
                double distance = Math.Sqrt(dLat * dLat + dLng * dLng);
                double speed = 0.0015;

                if (distance < speed)
                {
                    o.Status = "✅ Dostarczono";
                    o.CurrentLat = o.DestLat;
                    o.CurrentLng = o.DestLng;
                    o.Progress = 1.0;
                    await _grpcService.UpdateOrderAsync(o);
                    break;
                }
                else
                {
                    // Aktualizuj status na "W drodze" jeśli był inny
                    if (o.Status != "✈️ W drodze") o.Status = "✈️ W drodze";

                    double moveLat = (dLat / distance) * speed;
                    double moveLng = (dLng / distance) * speed;
                    o.CurrentLat += moveLat;
                    o.CurrentLng += moveLng;
                    o.Progress += 0.02;
                    if (o.Progress > 1) o.Progress = 0.99;
                    
                    await _grpcService.UpdateOrderAsync(o);
                }
                
                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Mission Error: {ex.Message}");
        }
        finally
        {
            lock (ActiveMissions) { ActiveMissions.Remove(o.Id); }
        }
    }
}