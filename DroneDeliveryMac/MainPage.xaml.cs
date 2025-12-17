using System.Collections.ObjectModel;
using DroneDeliveryMac.Models;
using DroneDeliveryMac.Services;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Microsoft.Maui.Devices.Sensors;

namespace DroneDeliveryMac;

public partial class MainPage : TabbedPage
{
    private readonly GrpcService _grpcService = new();
    private Microsoft.Maui.Controls.Maps.Map? DroneMap;

    public ObservableCollection<DroneOrder> OutgoingOrders { get; set; } = new();
    public ObservableCollection<DroneOrder> IncomingOrders { get; set; } = new();

    public MainPage(int initialTab = 0)
    {
        InitializeComponent();

        ListOutgoing.ItemsSource = OutgoingOrders;
        ListIncoming.ItemsSource = IncomingOrders;

        PickerSendDate.Date = DateTime.Now;
        PickerDeliverDate.Date = DateTime.Now.AddDays(1);

        SliderWeight.ValueChanged += (s, e) => { LabelWeight.Text = $"{e.NewValue:F1} kg"; RecalculateCost(); };
        PickerSendDate.DateSelected += (s, e) => RecalculateCost();
        PickerDeliverDate.DateSelected += (s, e) => RecalculateCost();

        if (Children.Count > initialTab)
        {
            CurrentPage = Children[initialTab];
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Task.Delay(1000); 

        // Inicjalizacja Mapy (jeśli jeszcze nie istnieje)
        if (DroneMap == null)
        {
            try
            {
                DroneMap = new Microsoft.Maui.Controls.Maps.Map { MapType = MapType.Street, IsShowingUser = true };
                MapContainer.Children.Add(DroneMap);
                DroneMap.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(52.23, 21.01), Distance.FromKilometers(10)));
                MapOverlayInfo.IsVisible = false;
            }
            catch (Exception ex) { MapOverlayInfo.Text = "Błąd mapy: " + ex.Message; }
        }

        try { RecalculateCost(); await LoadOrders(); } catch { }
    }

    private async Task LoadOrders()
    {
        try {
            var all = await _grpcService.GetOrdersAsync();
            
            OutgoingOrders.Clear(); IncomingOrders.Clear();
            foreach (var o in all)
            {
                if (o.IsIncoming) IncomingOrders.Add(o); else OutgoingOrders.Add(o);
                
                // Jeśli paczka nie jest dostarczona, wznów symulację
                if (o.Progress < 1.0 && o.Status != "Dostarczono") 
                {
                    _ = Task.Run(() => StartDroneMission(o));
                }
            }
        } catch { }
    }

    private void RecalculateCost()
    {
        double weight = SliderWeight.Value;
        int days = (PickerDeliverDate.Date - PickerSendDate.Date).Days;
        decimal basePrice = 15.0m + (decimal)weight * 2.0m;
        if (days == 0) basePrice += 100.0m;
        else if (days == 1) basePrice += 40.0m;
        else if (days > 2) basePrice -= 5.0m;
        if (basePrice < 0) basePrice = 10;
        LabelCost.Text = $"Koszt: {basePrice:F2} PLN";
    }

    private async void OnSendPackageClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EntryOrigin.Text) || string.IsNullOrWhiteSpace(EntryDest.Text)) return;

        try {
            var o = (await Geocoding.GetLocationsAsync(EntryOrigin.Text))?.FirstOrDefault();
            var d = (await Geocoding.GetLocationsAsync(EntryDest.Text))?.FirstOrDefault();
            
            if (o == null || d == null) { await DisplayAlert("Błąd", "Nie znaleziono adresu", "OK"); return; }

            var order = new DroneOrder
            {
                OriginAddress = EntryOrigin.Text, OriginLat = o.Latitude, OriginLng = o.Longitude, CurrentLat = o.Latitude, CurrentLng = o.Longitude,
                DestinationAddress = EntryDest.Text, DestLat = d.Latitude, DestLng = d.Longitude,
                PackageWeightKg = SliderWeight.Value, SendDate = PickerSendDate.Date, DeliveryDate = PickerDeliverDate.Date,
                Status = "Pakowanie...", // Startowy status
                Progress = 0.0
            };
            
            await _grpcService.AddOrderAsync(order);
            OutgoingOrders.Add(order);
            
            CurrentPage = Children[0];
            await Task.Delay(500);
            
            ShowRouteOnMap(order);
            _ = Task.Run(() => StartDroneMission(order));
            
            EntryOrigin.Text = ""; EntryDest.Text = "";
        } 
        catch (Exception ex) { await DisplayAlert("Error", ex.Message, "OK"); }
    }

    // --- KLUCZOWA POPRAWKA: Klikanie w listę ---
    private void OnOrderSelected(object sender, SelectionChangedEventArgs e)
    {
        var selectedOrder = e.CurrentSelection.FirstOrDefault() as DroneOrder;
        if (selectedOrder == null) return;

        ShowRouteOnMap(selectedOrder);

        // FIX: Odznacz element, aby można było kliknąć go ponownie
        if (sender is CollectionView cv)
        {
            cv.SelectedItem = null;
        }
    }

    private void ShowRouteOnMap(DroneOrder o)
    {
        if (DroneMap == null) return;
        
        // Musimy to robić w głównym wątku UI
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try {
                DroneMap.Pins.Clear(); 
                DroneMap.MapElements.Clear();
                
                // Piny
                DroneMap.Pins.Add(new Pin { Label = "Start", Type = PinType.SavedPin, Location = new Location(o.OriginLat, o.OriginLng) });
                DroneMap.Pins.Add(new Pin { Label = "Cel", Type = PinType.Place, Location = new Location(o.DestLat, o.DestLng) });
                
                // Jeśli dron jest w trasie, pokaż go
                if (o.Status != "Dostarczono") 
                {
                    DroneMap.Pins.Add(new Pin { Label = "DRON (100km/h)", Type = PinType.Generic, Location = new Location(o.CurrentLat, o.CurrentLng) });
                }

                // Linia trasy
                DroneMap.MapElements.Add(new Polyline 
                { 
                    StrokeColor = Colors.Blue, 
                    StrokeWidth = 5, 
                    Geopath = { new Location(o.OriginLat, o.OriginLng), new Location(o.DestLat, o.DestLng) } 
                });
                
                // Zoom na drona
                DroneMap.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(o.CurrentLat, o.CurrentLng), Distance.FromKilometers(5)));
            } catch { }
        });
    }

    private async Task StartDroneMission(DroneOrder o)
    {
        // 1. Oblicz dystans w km
        Location start = new Location(o.OriginLat, o.OriginLng);
        Location end = new Location(o.DestLat, o.DestLng);
        double distanceKm = Location.CalculateDistance(start, end, DistanceUnits.Kilometers);
        
        // 2. Prędkość drona: 100 km/h
        // W symulacji nie będziemy czekać godziny. Przyjmijmy "czas symulacji":
        // Niech każde 100ms programu to np. 1 minuta w świecie drona.
        // Albo prościej: ustalamy stałą liczbę kroków dla płynności, ale status wyświetla dane.
        
        int steps = 200; // Więcej kroków = płynniejszy ruch
        int stepDelay = 100; // ms

        // Fazy (progi procentowe)
        double phaseStart = 0.10; // 10%
        double phaseFly = 0.20;   // 20%
        double phaseLand = 0.90;  // 90%

        for (int i = 0; i <= steps; i++) {
            if (o.Status == "Dostarczono") break;

            await Task.Delay(stepDelay);
            double pct = (double)i / steps;
            
            // Logika Faz
            if (pct < phaseStart) o.Status = "📦 Pakowanie...";
            else if (pct < phaseFly) o.Status = "🚁 Startowanie silników...";
            else if (pct < phaseLand) o.Status = $"✈️ Lot (100 km/h) - Dystans: {distanceKm * (1-pct):F1}km";
            else if (pct < 1.0) o.Status = "🛬 Lądowanie...";
            else o.Status = "✅ Dostarczono";

            // Aktualizacja pozycji
            o.CurrentLat = o.OriginLat + (o.DestLat - o.OriginLat) * pct;
            o.CurrentLng = o.OriginLng + (o.DestLng - o.OriginLng) * pct;
            o.Progress = pct;

            // Wyślij status do serwera (rzadziej, żeby nie zapchać sieci, np. co 5%)
            if (i % 10 == 0) await _grpcService.UpdateOrderAsync(o);

            // Aktualizacja Mapy (Wizualizacja)
            MainThread.BeginInvokeOnMainThread(() => {
                if (DroneMap != null && DroneMap.Pins.Count > 0) {
                     var p = DroneMap.Pins.FirstOrDefault(x => x.Label.StartsWith("DRON"));
                     if (p != null) p.Location = new Location(o.CurrentLat, o.CurrentLng);
                }
            });
        }
        
        o.Status = "Dostarczono"; 
        o.Progress = 1.0;
        await _grpcService.UpdateOrderAsync(o);
        
        // Odśwież UI finalnie
        MainThread.BeginInvokeOnMainThread(() => {
            ShowRouteOnMap(o);
        });
    }
}