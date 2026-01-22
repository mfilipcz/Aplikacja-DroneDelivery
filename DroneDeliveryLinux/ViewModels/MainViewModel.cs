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
    private readonly GrpcDataService _grpcService = new();
    private readonly DatabaseService _dbService = new();
    private static readonly HttpClient _httpClient = new();
    
    private static readonly Dictionary<string, DroneOrder> ActiveMissions = new();

    // Zdarzenie wywoływane po dodaniu nowego zamówienia
    public event Action<DroneOrder>? OrderAdded;
    
    // Zdarzenie wywoływane po usunięciu zamówienia
    public event Action<DroneOrder>? OrderDeleted;

    // Zdarzenie dla błędów
    public event Action<string>? ErrorOccurred;

    // ID klienta przydzielone przez serwer
    public string ClientId => _grpcService.ClientId;

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
    private DateTimeOffset _sendDate = new DateTimeOffset(DateTime.Today);

    [ObservableProperty]
    private DateTimeOffset _deliverDate = new DateTimeOffset(DateTime.Today.AddDays(2));

    // Handlery zmian właściwości dla przeliczania kosztu
    partial void OnSliderWeightChanged(double value) => RecalculateCost();
    partial void OnSendDateChanged(DateTimeOffset value) => RecalculateCost();
    partial void OnDeliverDateChanged(DateTimeOffset value)
    {
        // Walidacja - data dostawy nie może być mniejsza niż dziś
        if (value.Date < DateTimeOffset.Now.Date)
        {
            DeliverDate = DateTimeOffset.Now;
            return;
        }
        RecalculateCost();
    }

    private void RecalculateCost()
    {
        double weight = SliderWeight;
        
        // Oblicz różnicę dni między datą dostawy a datą nadania
        // Używamy DateOnly dla precyzyjnego porównania tylko dat (bez czasu)
        var sendDateOnly = DateOnly.FromDateTime(SendDate.Date);
        var deliverDateOnly = DateOnly.FromDateTime(DeliverDate.Date);
        int days = deliverDateOnly.DayNumber - sendDateOnly.DayNumber;
        
        // Walidacja - data dostawy nie może być wcześniejsza niż data nadania
        if (days < 0)
        {
            LabelCost = "Błąd: data dostawy przed datą nadania";
            return;
        }
        
        // Cena bazowa
        decimal basePrice = 10.0m;
        
        // Skalowanie wagi: im cięższa paczka, tym drożej
        // 0.5kg = +1 PLN, 10kg = +20 PLN, 20kg = +40 PLN
        decimal weightCost = (decimal)weight * 2.0m;
        
        // Skalowanie terminu: im bliższy termin, tym drożej
        // Dzień 0 (ten sam dzień) = +100 PLN
        // Dzień 1 = +90 PLN
        // Dzień 2 = +80 PLN
        // ...
        // Dzień 10+ = +0 PLN (najtaniej)
        decimal timeCost = 0.0m;
        if (days < 10)
        {
            timeCost = (10 - days) * 10.0m;
        }
        
        decimal totalPrice = basePrice + weightCost + timeCost;
        
        LabelCost = $"Koszt: {totalPrice:F2} PLN";
    }

    public MainViewModel()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "DroneDeliveryLinux/1.0");
        RecalculateCost();
    }

    /// <summary>
    /// Rejestruje klienta na serwerze gRPC - musi być wywołane przed innymi operacjami
    /// </summary>
    public async Task<bool> RegisterAsync()
    {
        var success = await _grpcService.RegisterAsync();
        if (success)
        {
            // Po rejestracji pobierz listę paczek tego klienta
            await LoadOrdersAsync();
        }
        return success;
    }

    public async Task LoadOrdersAsync()
    {
        var orders = await _grpcService.GetOrdersAsync();
        
        Dispatcher.UIThread.Post(() => {
            AllOrders.Clear();
            OutgoingOrders.Clear();
            IncomingOrders.Clear();
            
            foreach (var order in orders)
            {
                DroneOrder liveOrder = order;
                lock (ActiveMissions)
                {
                    if (ActiveMissions.ContainsKey(order.Id)) liveOrder = ActiveMissions[order.Id];
                }

                AllOrders.Add(liveOrder);

                if (liveOrder.IsIncoming) IncomingOrders.Add(liveOrder);
                else OutgoingOrders.Add(liveOrder);

                if (liveOrder.Status != "✅ Dostarczono")
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
        });
    }

    public async Task DeleteOrderAsync(DroneOrder order)
    {
        // Usuń z serwera
        var success = await _grpcService.DeleteOrderAsync(order.Id);
        
        if (success)
        {
            // Usuń z aktywnych misji
            lock (ActiveMissions)
            {
                ActiveMissions.Remove(order.Id);
            }
            
            // Usuń z kolekcji lokalnych
            Dispatcher.UIThread.Post(() => {
                AllOrders.Remove(order);
                OutgoingOrders.Remove(order);
                IncomingOrders.Remove(order);
                
                OrderDeleted?.Invoke(order);
            });
        }
    }

    // Geokodowanie używając Nominatim (OpenStreetMap)
    private async Task<(double lat, double lng)?> GeocodeAddressAsync(string address)
    {
        try
        {
            // Dodaj "Warszawa, Polska" jeśli adres nie zawiera miasta
            var fullAddress = address;
            if (!address.ToLower().Contains("warszawa") && !address.ToLower().Contains("poland") && !address.ToLower().Contains("polska"))
            {
                fullAddress = $"{address}, Warszawa, Polska";
            }
            
            var encodedAddress = Uri.EscapeDataString(fullAddress);
            var url = $"https://nominatim.openstreetmap.org/search?q={encodedAddress}&format=json&limit=1";
            
            var response = await _httpClient.GetStringAsync(url);
            var results = JsonSerializer.Deserialize<JsonElement[]>(response);
            
            if (results != null && results.Length > 0)
            {
                var first = results[0];
                var lat = double.Parse(first.GetProperty("lat").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var lng = double.Parse(first.GetProperty("lon").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                return (lat, lng);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Geocoding Error] {ex.Message}");
        }
        return null;
    }

    // Obliczanie dystansu (formuła Haversine)
    public static double CalculateDistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371; // Promień Ziemi w km
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double deg) => deg * Math.PI / 180;

    [RelayCommand]
    private async Task SendPackage()
    {
        if (string.IsNullOrWhiteSpace(EntryOrigin) || string.IsNullOrWhiteSpace(EntryDest))
        {
            ErrorOccurred?.Invoke("Wprowadź adres nadania i dostawy.");
            return;
        }
        
        if (DeliverDate.Date < SendDate.Date)
        {
            ErrorOccurred?.Invoke("Data dostawy nie może być wcześniejsza niż data nadania.");
            return;
        }

        // Geokodowanie adresów
        var originCoords = await GeocodeAddressAsync(EntryOrigin);
        var destCoords = await GeocodeAddressAsync(EntryDest);

        if (originCoords == null || destCoords == null)
        {
            ErrorOccurred?.Invoke("Nie znaleziono adresu. Sprawdź poprawność.");
            return;
        }

        var order = new DroneOrder
        {
            OriginAddress = EntryOrigin,
            OriginLat = originCoords.Value.lat,
            OriginLng = originCoords.Value.lng,
            CurrentLat = originCoords.Value.lat,
            CurrentLng = originCoords.Value.lng,
            DestinationAddress = EntryDest,
            DestLat = destCoords.Value.lat,
            DestLng = destCoords.Value.lng,
            PackageWeightKg = SliderWeight,
            SendDate = SendDate.DateTime,
            DeliveryDate = DeliverDate.DateTime,
            Status = "Inicjalizacja...",
            Progress = 0.0
        };

        await _grpcService.AddOrderAsync(order);
        OutgoingOrders.Add(order);
        AllOrders.Add(order);
        
        // Powiadom widok o nowym zamówieniu (dla rysowania trasy)
        OrderAdded?.Invoke(order);
        
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
            double distanceKm = CalculateDistanceKm(o.OriginLat, o.OriginLng, o.DestLat, o.DestLng);
            
            // Stała prędkość drona: 100 km/h
            // W symulacji: 1 km = 0.5 sekundy (przyspieszenie x7200)
            // Minimalna długość lotu: 5 sekund, maksymalna: 60 sekund
            const double droneSpeedKmh = 100.0;
            const double simulationSpeedMultiplier = 0.5; // sekund na km w symulacji
            const int delayMs = 50; // opóźnienie między krokami dla płynnej animacji
            
            // Oblicz czas lotu w sekundach symulacji
            double flightTimeSeconds = distanceKm * simulationSpeedMultiplier;
            flightTimeSeconds = Math.Max(5, Math.Min(60, flightTimeSeconds)); // Limit 5-60 sekund
            
            // Oblicz liczbę kroków
            int totalSteps = (int)(flightTimeSeconds * 1000 / delayMs);
            int startStep = (int)(o.Progress * totalSteps);

            // Fazy lotu (w procentach całego lotu)
            double phasePackaging = 0.03;   // Pakowanie: 0-3%
            double phaseTakeoff = 0.08;     // Startowanie: 3-8%
            double phaseLanding = 0.95;     // Lądowanie: 95-100%

            for (int i = startStep; i <= totalSteps; i++)
            {
                if (o.Status == "✅ Dostarczono") break;

                await Task.Delay(delayMs);
                double pct = (double)i / totalSteps;

                // Oblicz pozostały dystans
                double remainingKm = distanceKm * (1 - pct);

                string newStatus;
                if (pct < phasePackaging) 
                    newStatus = "📦 Pakowanie...";
                else if (pct < phaseTakeoff) 
                    newStatus = "🚁 Startowanie...";
                else if (pct < phaseLanding) 
                    newStatus = $"✈️ W locie ({droneSpeedKmh:F0} km/h) - {remainingKm:F1}km";
                else if (pct < 1.0) 
                    newStatus = "🛬 Lądowanie...";
                else 
                    newStatus = "✅ Dostarczono";

                o.Status = newStatus;
                o.CurrentLat = o.OriginLat + (o.DestLat - o.OriginLat) * pct;
                o.CurrentLng = o.OriginLng + (o.DestLng - o.OriginLng) * pct;
                o.Progress = pct;

                if (i % 20 == 0) await _grpcService.UpdateOrderAsync(o);
            }
            
            o.Status = "✅ Dostarczono";
            o.Progress = 1.0;
            await _grpcService.UpdateOrderAsync(o);
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