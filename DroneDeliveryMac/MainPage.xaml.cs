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
    
    // Mapa
    private Microsoft.Maui.Controls.Maps.Map? _droneMap;
    
    // Timer do renderowania grafiki
    private IDispatcherTimer? _uiTimer;
    
    // ZMIANA: Słownik przechowujący ŻYWE obiekty symulacji (ID -> Obiekt)
    private static readonly Dictionary<string, DroneOrder> ActiveMissions = new();

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
        
        SetupUiTimer();

        if (Children.Count > initialTab)
        {
            CurrentPage = Children[initialTab];
        }
    }

    private void SetupUiTimer()
    {
        _uiTimer = Dispatcher.CreateTimer();
        _uiTimer.Interval = TimeSpan.FromMilliseconds(50);
        _uiTimer.Tick += (s, e) => UpdateMapVisuals();
    }

    // --- CYKL ŻYCIA ---

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Task.Delay(300); 
        await InitializeMap(); 
        
        _uiTimer?.Start();

        try 
        { 
            RecalculateCost(); 
            await LoadOrdersAndResume(); 
        } 
        catch { }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _uiTimer?.Stop();

        try 
        {
            if (_droneMap != null)
            {
                MapContainer.Children.Clear();
                _droneMap = null;
            }
        }
        catch { }
    }

    // --- LOGIKA MAPY I DANYCH ---

    private async Task InitializeMap()
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                if (_droneMap != null && MapContainer.Children.Contains(_droneMap)) return;

                MapContainer.Children.Clear();
                await Task.Delay(50);

                _droneMap = new Microsoft.Maui.Controls.Maps.Map 
                { 
                    MapType = MapType.Street, 
                    IsShowingUser = true 
                };

                MapContainer.Children.Add(_droneMap);
                _droneMap.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(52.23, 21.01), Distance.FromKilometers(10)));
                MapOverlayInfo.IsVisible = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MAP INIT ERROR] {ex.Message}");
                MapOverlayInfo.Text = "Mapa niedostępna.";
                MapOverlayInfo.IsVisible = true;
            }
        });
    }

    private async Task LoadOrdersAndResume()
    {
        try 
        {
            var serverOrders = await _grpcService.GetOrdersAsync();
            
            OutgoingOrders.Clear(); 
            IncomingOrders.Clear();

            foreach (var serverOrder in serverOrders)
            {
                // Domyślnie używamy tego, co przyszło z serwera
                DroneOrder orderForUi = serverOrder;

                // --- KLUCZOWA POPRAWKA ---
                // Sprawdzamy, czy ten dron już leci w tle.
                lock (ActiveMissions)
                {
                    if (ActiveMissions.ContainsKey(serverOrder.Id))
                    {
                        // Jeśli tak -> PODMIENIAMY go na wersję "żywą" (tę, która się rusza)
                        // Dzięki temu UI wyświetli obiekt, który faktycznie zmienia współrzędne
                        orderForUi = ActiveMissions[serverOrder.Id];
                    }
                }

                if (orderForUi.IsIncoming) IncomingOrders.Add(orderForUi); 
                else OutgoingOrders.Add(orderForUi);
                
                // Jeśli misja nie jest aktywna (nie ma jej w słowniku), a powinna być -> uruchom
                if (orderForUi.Status != "✅ Dostarczono") 
                {
                    lock (ActiveMissions)
                    {
                        if (!ActiveMissions.ContainsKey(orderForUi.Id))
                        {
                            _ = Task.Run(() => StartDroneMission(orderForUi));
                        }
                    }
                }
            }
        } catch { }
    }

    private void UpdateMapVisuals()
    {
        if (_droneMap == null || MapContainer.Children.Count == 0) return;

        // Szukamy paczki, która jest w trakcie lotu
        foreach (var order in OutgoingOrders.Concat(IncomingOrders))
        {
            // Interesują nas tylko te, które są w słowniku ActiveMissions (czyli te, które faktycznie lecą)
            bool isSimulating;
            lock(ActiveMissions) { isSimulating = ActiveMissions.ContainsKey(order.Id); }

            if (isSimulating && order.Status != "✅ Dostarczono")
            {
                try 
                {
                    var pin = _droneMap.Pins.FirstOrDefault(p => p.Label == "DRON");
                    
                    if (pin != null)
                    {
                        // Przesuwamy pina na pozycję z "żywego" obiektu
                        pin.Location = new Location(order.CurrentLat, order.CurrentLng);
                    }
                    else
                    {
                        // Jeśli pina nie ma (np. po przeładowaniu mapy) -> dodajemy go
                        _droneMap.Pins.Add(new Pin 
                        { 
                            Label = "DRON", 
                            Type = PinType.Generic, 
                            Location = new Location(order.CurrentLat, order.CurrentLng) 
                        });
                    }
                }
                catch { }
                break; // Obsługujemy jednego drona naraz w tym widoku
            }
        }
    }

    private async Task StartDroneMission(DroneOrder o)
    {
        // Rejestracja w słowniku - TO JEST TEN "ŻYWY" OBIEKT
        lock (ActiveMissions) {
            if (ActiveMissions.ContainsKey(o.Id)) return;
            ActiveMissions.Add(o.Id, o);
        }

        try
        {
            Location start = new Location(o.OriginLat, o.OriginLng);
            Location end = new Location(o.DestLat, o.DestLng);
            double distanceKm = Location.CalculateDistance(start, end, DistanceUnits.Kilometers);
            
            int totalSteps = 600; 
            int delayMs = 100;
            int startStep = (int)(o.Progress * totalSteps);

            double phaseStart = 0.05;
            double phaseTakeoff = 0.10;
            double phaseLanding = 0.95;

            for (int i = startStep; i <= totalSteps; i++) 
            {
                if (o.Status == "✅ Dostarczono") break;

                await Task.Delay(delayMs);
                double pct = (double)i / totalSteps;
                
                string newStatus;
                if (pct < phaseStart) newStatus = "📦 Pakowanie...";
                else if (pct < phaseTakeoff) newStatus = "🚁 Startowanie...";
                else if (pct < phaseLanding) newStatus = $"✈️ W locie ({100} km/h) - {distanceKm * (1-pct):F1}km";
                else if (pct < 1.0) newStatus = "🛬 Lądowanie...";
                else newStatus = "✅ Dostarczono";

                // Aktualizujemy obiekt w pamięci. 
                // Ponieważ UI (lista i timer) korzystają z TEGO SAMEGO obiektu (dzięki podmianie w LoadOrders),
                // wszystko zaktualizuje się automatycznie.
                o.Status = newStatus;
                o.CurrentLat = o.OriginLat + (o.DestLat - o.OriginLat) * pct;
                o.CurrentLng = o.OriginLng + (o.DestLng - o.OriginLng) * pct;
                o.Progress = pct;

                if (i % 20 == 0) await _grpcService.UpdateOrderAsync(o);
            }
            
            o.Status = "✅ Dostarczono"; 
            o.Progress = 1.0;
            await _grpcService.UpdateOrderAsync(o);
            
            // Po zakończeniu, usuwamy drona z mapy (opcjonalnie)
            MainThread.BeginInvokeOnMainThread(() => {
                if (_droneMap != null) {
                    var p = _droneMap.Pins.FirstOrDefault(x => x.Label == "DRON");
                    if (p != null) _droneMap.Pins.Remove(p);
                    DrawRouteSafe(o); // Przerysuj samą trasę
                }
            });
        }
        catch (Exception ex)
        {
             Console.WriteLine($"Mission Error: {ex.Message}");
        }
        finally
        {
            lock (ActiveMissions) {
                ActiveMissions.Remove(o.Id);
            }
        }
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
                Status = "Inicjalizacja...", 
                Progress = 0.0
            };
            
            await _grpcService.AddOrderAsync(order);
            OutgoingOrders.Add(order);
            
            CurrentPage = Children[0]; // Przełącz na mapę
            
            await Task.Delay(1000);
            DrawRouteSafe(order);
            
            // Startujemy misję
            _ = Task.Run(() => StartDroneMission(order));

            EntryOrigin.Text = ""; EntryDest.Text = "";
        } 
        catch (Exception ex) { await DisplayAlert("Error", ex.Message, "OK"); }
    }

    private async void OnOrderSelected(object sender, SelectionChangedEventArgs e)
    {
        var selectedOrder = e.CurrentSelection.FirstOrDefault() as DroneOrder;
        if (selectedOrder == null) return;

        if (_droneMap == null) await InitializeMap();
        
        DrawRouteSafe(selectedOrder);

        if (sender is CollectionView cv) cv.SelectedItem = null;
    }

    private void DrawRouteSafe(DroneOrder o)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_droneMap == null || MapContainer.Children.Count == 0) return;

            try 
            {
                _droneMap.Pins.Clear(); 
                _droneMap.MapElements.Clear();
                
                _droneMap.Pins.Add(new Pin { Label = "Start", Type = PinType.SavedPin, Location = new Location(o.OriginLat, o.OriginLng) });
                _droneMap.Pins.Add(new Pin { Label = "Cel", Type = PinType.Place, Location = new Location(o.DestLat, o.DestLng) });
                
                // Nie musimy dodawać drona ręcznie tutaj - Timer go doda
                
                var polyline = new Polyline
                {
                    StrokeColor = Colors.Blue,
                    StrokeWidth = 5
                };
                polyline.Geopath.Add(new Location(o.OriginLat, o.OriginLng));
                polyline.Geopath.Add(new Location(o.DestLat, o.DestLng));
                
                _droneMap.MapElements.Add(polyline);
                _droneMap.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(o.CurrentLat, o.CurrentLng), Distance.FromKilometers(5)));
            } 
            catch { }
        });
    }
}