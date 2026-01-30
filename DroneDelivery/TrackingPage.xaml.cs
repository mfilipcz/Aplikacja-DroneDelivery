using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Controls.Shapes; // Potrzebne dla Border
using DroneServer;

namespace DroneDelivery;

public partial class TrackingPage : ContentPage
{
    private readonly DroneService.DroneServiceClient _client;
    private readonly ClientSession _session; // Sesja z ID klienta
    private System.Timers.Timer? _timer;
    private string? _selectedOrderId = null;
    
    // CACHE: Przechowujemy dane lokalnie, żeby interfejs nie migał
    private List<DroneOrderMsg> _cachedOrders = new();

    // Konstruktor przyjmuje klienta gRPC oraz Sesję
    public TrackingPage(DroneService.DroneServiceClient client, ClientSession session)
    {
        InitializeComponent();
        _client = client;
        _session = session;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CheckLocationPermission();
        
        // Ustawiamy widok domyślny na Warszawę (lub inną lokalizację startową)
        DroneMap.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(52.2297, 21.0122), Distance.FromKilometers(10)));
        
        StartWatching();
    }

    private async Task CheckLocationPermission()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        }
    }

    private void StartWatching()
    {
        _timer = new System.Timers.Timer(500); // Odświeżanie co 0.5 sekundy
        _timer.Elapsed += async (s, e) => await RefreshMapData();
        _timer.Start();
    }

    private async Task RefreshMapData()
    {
        // Jeśli nie mamy ID (aplikacja się nie zarejestrowała), nie pytamy serwera
        if (!_session.IsRegistered) return;

        try
        {
            // Pobieramy paczki przypisane do naszego ClientId
            var request = new ClientRequest { ClientId = _session.ClientId };
            var response = await _client.GetOrdersAsync(request);
            
            _cachedOrders = response.Orders.ToList();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdateMap(_cachedOrders);
                UpdateSideBar(_cachedOrders);
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd pobierania danych: {ex.Message}");
        }
    }

    private void UpdateMap(IEnumerable<DroneOrderMsg> orders)
    {
        DroneMap.Pins.Clear();
        DroneMap.MapElements.Clear();

        foreach (var order in orders)
        {
            // 1. Rysujemy drona (zawsze)
            var dronePin = new Pin
            {
                Label = "Dron",
                Address = $"Status: {order.Status}",
                Type = PinType.Generic,
                Location = new Location(order.CurrentLat, order.CurrentLng)
            };
            DroneMap.Pins.Add(dronePin);

            // 2. Rysujemy trasę (tylko dla wybranej paczki)
            if (order.Id == _selectedOrderId)
            {
                DroneMap.Pins.Add(new Pin { Label = "Start", Address = order.OriginAddress, Type = PinType.SavedPin, Location = new Location(order.OriginLat, order.OriginLng) });
                DroneMap.Pins.Add(new Pin { Label = "Cel", Address = order.DestinationAddress, Type = PinType.SavedPin, Location = new Location(order.DestLat, order.DestLng) });

                // Używamy pełnej nazwy, żeby uniknąć konfliktu z Shapes.Polyline
                var polyline = new Microsoft.Maui.Controls.Maps.Polyline
                {
                    StrokeColor = Colors.Blue,
                    StrokeWidth = 5
                };
                polyline.Geopath.Add(new Location(order.OriginLat, order.OriginLng));
                polyline.Geopath.Add(new Location(order.DestLat, order.DestLng));
                DroneMap.MapElements.Add(polyline);
            }
        }
    }

    private void UpdateSideBar(IEnumerable<DroneOrderMsg> orders)
    {
        if (OrdersStack == null) return;
        OrdersStack.Children.Clear();

        foreach (var order in orders)
        {
            OrdersStack.Children.Add(CreateOrderCard(order));
        }
    }

    // --- TO JEST METODA Z POPRAWIONYM PASKIEM POSTĘPU ---
    private Border CreateOrderCard(DroneOrderMsg order)
    {
        bool isSelected = order.Id == _selectedOrderId;
        var borderColor = isSelected ? Colors.Blue : Colors.LightGray;
        var bgColor = isSelected ? Color.FromRgba(240, 240, 255, 255) : Colors.White;

        var border = new Border
        {
            Stroke = borderColor,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = bgColor,
            Padding = 10,
            Margin = new Thickness(0, 0, 0, 5)
        };

        // Obsługa kliknięcia w kartę
        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += (s, e) => 
        {
            _selectedOrderId = order.Id;
            // Natychmiastowe odświeżenie UI
            UpdateSideBar(_cachedOrders); 
            UpdateMap(_cachedOrders);     
            FocusOnRoute(order);
        };
        border.GestureRecognizers.Add(tapGesture);

        var stack = new VerticalStackLayout();
        stack.Children.Add(new Label { Text = $"Do: {order.DestinationAddress}", FontAttributes = FontAttributes.Bold, FontSize = 13, TextColor = Colors.Black });
        
        var statusColor = order.Status == "Dostarczono" ? Colors.Green : Colors.Purple;
        stack.Children.Add(new Label { Text = order.Status, TextColor = statusColor, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });

        if (order.Status != "Dostarczono")
        {
            // --- MATEMATYKA POSTĘPU ---
            
            // 1. Dystans całkowity (Start -> Cel)
            double totalDistance = Location.CalculateDistance(
                order.OriginLat, order.OriginLng,
                order.DestLat, order.DestLng,
                DistanceUnits.Kilometers);

            // 2. Dystans pozostały (Dron -> Cel)
            double remainingDistance = Location.CalculateDistance(
                order.CurrentLat, order.CurrentLng,
                order.DestLat, order.DestLng,
                DistanceUnits.Kilometers);

            // 3. Obliczamy % wykonania trasy
            double realProgress = 0;
            if (totalDistance > 0)
            {
                // Wzór: 100% minus (to co zostało / całość)
                realProgress = 1.0 - (remainingDistance / totalDistance);
            }

            // Zabezpieczenie przed wartościami spoza zakresu 0-1
            realProgress = Math.Clamp(realProgress, 0.0, 1.0);

            stack.Children.Add(new Label 
            { 
                Text = $"{remainingDistance:F1} km do celu", 
                TextColor = Colors.Gray, 
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 5) 
            });

            // Wyświetlamy obliczony postęp
            stack.Children.Add(new ProgressBar { Progress = realProgress, ProgressColor = Colors.Purple, HeightRequest = 4 });
        }
        else
        {
             stack.Children.Add(new Label { Text = "✓ Dostarczono", TextColor = Colors.Green, FontSize = 10, Margin = new Thickness(0, 5, 0, 0) });
        }

        border.Content = stack;
        return border;
    }

    private void FocusOnRoute(DroneOrderMsg order)
    {
        // Wyśrodkowanie kamery na trasie
        double centerLat = (order.OriginLat + order.DestLat) / 2;
        double centerLng = (order.OriginLng + order.DestLng) / 2;
        var center = new Location(centerLat, centerLng);

        // Obliczenie zoomu
        double distanceKm = Location.CalculateDistance(
            order.OriginLat, order.OriginLng,
            order.DestLat, order.DestLng,
            DistanceUnits.Kilometers);

        // Ustawienie widoku z marginesem (mnożnik 0.6)
        DroneMap.MoveToRegion(MapSpan.FromCenterAndRadius(center, Distance.FromKilometers(distanceKm * 0.6)));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
    }
}