using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Controls.Shapes; // Potrzebne dla Border (RoundRectangle)
using DroneServer;

namespace DroneDelivery;

public partial class TrackingPage : ContentPage
{
    private readonly DroneService.DroneServiceClient _client;
    private System.Timers.Timer? _timer;
    private string? _selectedOrderId = null;
    
    private List<DroneOrderMsg> _cachedOrders = new();

    public TrackingPage(DroneService.DroneServiceClient client)
    {
        InitializeComponent();
        _client = client;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CheckLocationPermission();
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
        _timer = new System.Timers.Timer(500); 
        _timer.Elapsed += async (s, e) => await RefreshMapData();
        _timer.Start();
    }

    private async Task RefreshMapData()
    {
        try
        {
            var response = await _client.GetOrdersAsync(new Empty());
            _cachedOrders = response.Orders.ToList();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdateMap(_cachedOrders);
                UpdateSideBar(_cachedOrders);
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd: {ex.Message}");
        }
    }

    private void UpdateMap(IEnumerable<DroneOrderMsg> orders)
    {
        DroneMap.Pins.Clear();
        DroneMap.MapElements.Clear();

        foreach (var order in orders)
        {
            var dronePin = new Pin
            {
                Label = "Dron",
                Address = $"Status: {order.Status}",
                Type = PinType.Generic,
                Location = new Location(order.CurrentLat, order.CurrentLng)
            };
            DroneMap.Pins.Add(dronePin);

            if (order.Id == _selectedOrderId)
            {
                DroneMap.Pins.Add(new Pin { Label = "Start", Address = order.OriginAddress, Type = PinType.SavedPin, Location = new Location(order.OriginLat, order.OriginLng) });
                DroneMap.Pins.Add(new Pin { Label = "Cel", Address = order.DestinationAddress, Type = PinType.SavedPin, Location = new Location(order.DestLat, order.DestLng) });

                // POPRAWKA: Używamy pełnej nazwy, żeby uniknąć konfliktu z Shapes
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

    private Border CreateOrderCard(DroneOrderMsg order)
    {
        bool isSelected = order.Id == _selectedOrderId;
        var borderColor = isSelected ? Colors.Blue : Colors.LightGray;
        var bgColor = isSelected ? Color.FromRgba(240, 240, 255, 255) : Colors.White;

        // Tutaj używamy Shapes (RoundRectangle)
        var border = new Border
        {
            Stroke = borderColor,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = bgColor,
            Padding = 10,
            Margin = new Thickness(0, 0, 0, 5)
        };

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += (s, e) => 
        {
            _selectedOrderId = order.Id;
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
            double distanceKm = Location.CalculateDistance(
                order.CurrentLat, order.CurrentLng,
                order.DestLat, order.DestLng,
                DistanceUnits.Kilometers);

            stack.Children.Add(new Label 
            { 
                Text = $"{distanceKm:F1} km do celu", 
                TextColor = Colors.Gray, 
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 5) 
            });

            stack.Children.Add(new ProgressBar { Progress = order.Progress, ProgressColor = Colors.Purple, HeightRequest = 4 });
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
        double centerLat = (order.OriginLat + order.DestLat) / 2;
        double centerLng = (order.OriginLng + order.DestLng) / 2;
        var center = new Location(centerLat, centerLng);

        double distanceKm = Location.CalculateDistance(
            order.OriginLat, order.OriginLng,
            order.DestLat, order.DestLng,
            DistanceUnits.Kilometers);

        DroneMap.MoveToRegion(MapSpan.FromCenterAndRadius(center, Distance.FromKilometers(distanceKm * 0.6)));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
    }
}