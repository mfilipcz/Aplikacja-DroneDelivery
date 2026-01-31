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
    private DateTime _sendDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _deliverDate = DateTime.Today.AddDays(2);

    // Handlery zmian właściwości dla przeliczania kosztu
    partial void OnSliderWeightChanged(double value) => RecalculateCost();
    partial void OnSendDateChanged(DateTime value)
    {
        if (value.Date < DateTime.Today)
        {
            SendDate = DateTime.Today;
            return;
        }
        RecalculateCost();
    }
    
    partial void OnDeliverDateChanged(DateTime value)
    {
        // Walidacja - data dostawy nie może być mniejsza niż dziś
        if (value.Date < DateTime.Today)
        {
            DeliverDate = DateTime.Today;
            return;
        }
        RecalculateCost();
    }

    private void RecalculateCost()
    {
        double weight = SliderWeight;
        
        // Oblicz różnicę dni między datą dostawy a datą nadania
        // Używamy DateOnly dla precyzyjnego porównania tylko dat (bez czasu)
        var sendDateOnly = DateOnly.FromDateTime(SendDate);
        var deliverDateOnly = DateOnly.FromDateTime(DeliverDate);
        int days = deliverDateOnly.DayNumber - sendDateOnly.DayNumber;
        
        // Walidacja - data dostawy nie może być wcześniejsza niż data nadania
        if (days < 0)
        {
            LabelCost = "Błąd: data dostawy przed datą nadania";
            return;
        }
        
        // Algorytm zgodny z wersją Mac:
        // Waga * 10 + Szybkość (50 / (dni + 1)) + Baza 20
        
        decimal weightCost = (decimal)weight * 10.0m;
        decimal speedCost = 50.0m / (days + 1);
        decimal basePrice = 20.0m;
        
        decimal totalPrice = basePrice + weightCost + speedCost;
        
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
            if (string.IsNullOrWhiteSpace(EntryOrigin) && string.IsNullOrWhiteSpace(EntryDest))
            {
                ErrorOccurred?.Invoke("Wprowadź adres nadania i dostawy.");
            }
            else if (string.IsNullOrWhiteSpace(EntryOrigin))
            {
                ErrorOccurred?.Invoke("Wprowadź adres nadania.");
            }
            else
            {
                ErrorOccurred?.Invoke("Wprowadź adres dostawy.");
            }
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
            if (originCoords == null && destCoords == null)
            {
                ErrorOccurred?.Invoke("Nie znaleziono adresów nadania i dostawy. Sprawdź poprawność.");
            }
            else if (originCoords == null)
            {
                ErrorOccurred?.Invoke($"Nie znaleziono adresu nadania: '{EntryOrigin}'. Sprawdź poprawność.");
            }
            else
            {
                ErrorOccurred?.Invoke($"Nie znaleziono adresu dostawy: '{EntryDest}'. Sprawdź poprawność.");
            }
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
            SendDate = SendDate,
            DeliveryDate = DeliverDate,
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
            // Ustaw status początkowy zgodny z Mac, ale z emoji
            o.Status = "✈️ W drodze";
            await _grpcService.UpdateOrderAsync(o);

            while (true)
            {
                // Sprawdź czy nie anulowano (np. usunięto paczkę)
                // W tej implementacji MainViewModel zarządzanie anulowaniem jest trudniejsze,
                // ale zakładamy, że pętla przerwie się przy błędzie update lub można dodać flagę.
                // Mac po prostu robi to w pętli Tick. Tutaj mamy Task per Order.
                
                double dLat = o.DestLat - o.CurrentLat;
                double dLng = o.DestLng - o.CurrentLng;
                
                // Obliczamy dystans (prosta euklidesowa)
                double distance = Math.Sqrt(dLat * dLat + dLng * dLng);
                double speed = 0.0015; // Prędkość drona na cykl (zgodna z Mac)

                // Jeśli jesteśmy bardzo blisko celu -> Dostarczono
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
                    // Przesuwamy drona w stronę celu
                    double moveLat = (dLat / distance) * speed;
                    double moveLng = (dLng / distance) * speed;

                    o.CurrentLat += moveLat;
                    o.CurrentLng += moveLng;
                    
                    o.Progress += 0.02;
                    if (o.Progress > 1) o.Progress = 0.99;
                    
                    await _grpcService.UpdateOrderAsync(o);
                }
                
                // Opóźnienie zgodne z Mac (500ms)
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