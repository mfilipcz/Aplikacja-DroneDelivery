using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DroneDeliveryLinux.Models;
using DroneDeliveryLinux.ViewModels;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Avalonia;
using Mapsui.Nts;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// Aliasy dla uniknięcia konfliktów nazw
using AvColor = Avalonia.Media.Color;
using AvBrush = Avalonia.Media.IBrush;
using AvBrushes = Avalonia.Media.Brushes;
using MColor = Mapsui.Styles.Color;
using MBrush = Mapsui.Styles.Brush;
using MPen = Mapsui.Styles.Pen;
using MPoint = Mapsui.MPoint;

namespace DroneDeliveryLinux.Views;

public partial class MainWindow : Window
{
    private MapControl _mapControl = null!;
    private MemoryLayer _droneLayer = null!;
    private MemoryLayer _routeLayer = null!;  // Dodana warstwa tras
    private MainViewModel _viewModel;
    private DispatcherTimer _timer;
    private DroneOrder? _selectedOrder;  // Aktualnie wybrana paczka
    private Border? _loadingOverlay;     // Overlay dla animacji loading

    // --- DESIGN SYSTEM ---
    private static readonly AvColor PrimaryColor = AvColor.Parse("#512BD4"); // .NET Purple
    private static readonly AvColor AccentColor = AvColor.Parse("#FFCC00");  // Gold/Yellow
    private static readonly AvBrush PrimaryBrush = new SolidColorBrush(PrimaryColor);
    private static readonly AvBrush TextDarkBrush = new SolidColorBrush(AvColor.Parse("#1a1a1a"));
    private static readonly AvBrush TextLightBrush = new SolidColorBrush(AvColor.Parse("#757575"));
    private static readonly AvBrush BackgroundBrush = new SolidColorBrush(AvColor.Parse("#F3F4F6")); // Light Gray

    // --- SKALE IKON NA MAPIE ---
    private const double PinScale = 1.6;
    private const double PinOffsetY = 0.5; // Czubek pinezki na punkcie
    private const double DroneBackdropScale = 1.3; // even thinner backdrop to reduce blue outline
    private const double DroneIconScale = 0.7;


    // Geometrie Ikon (Material Design paths)
    private const string IconBoxPath = "M21,16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V7.5C3,7.12 3.21,6.79 3.53,6.62L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.79,6.79 21,7.12 21,7.5V16.5Z M12,4.15L6.04,7.5L12,10.85L17.96,7.5L12,4.15Z M5,15.91L11,19.29V12.58L5,9.21V15.91Z M19,15.91V9.21L13,12.58V19.29L19,15.91Z";
    private const string IconSendPath = "M2,21L23,12L2,3V10L17,12L2,14V21Z";
    private const string IconBackPath = "M20,11V13H8L13.5,18.5L12.08,19.92L4.16,12L12.08,4.08L13.5,5.5L8,11H20Z";
    private const string IconMapPath = "M20.5,3L20.34,3.03L15,5.1L9,3L3.36,4.9C3.15,4.97 3,5.15 3,5.38V20.5A0.5,0.5 0 0,0 3.5,21L3.66,20.97L9,18.9L15,21L20.64,19.1C20.85,19.03 21,18.85 21,18.62V3.5A0.5,0.5 0 0,0 20.5,3M15,19L9,16.89V5L15,7.11V19Z";
    private const string IconDeletePath = "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z";

    public MainWindow()
    {
        Title = "Drone Delivery System";
        Width = 1100;
        Height = 800;
        Background = AvBrushes.White;
        Icon = null; // Można dodać ikonę okna jeśli dostępna

        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        
        // Subskrybuj zdarzenia z ViewModel
        _viewModel.OrderAdded += OnOrderAdded;
        _viewModel.ErrorOccurred += OnErrorOccurred;

        InitializeMap();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += UpdateMap;
        _timer.Start();

        // Rejestracja na serwerze i pokazanie menu
        _ = InitializeAndShowMenuAsync();
    }

    private async Task InitializeAndShowMenuAsync()
    {
        var success = await _viewModel.RegisterAsync();
        if (success)
        {
            Title = $"Drone Delivery System - {_viewModel.ClientId}";
        }
        else
        {
            Console.WriteLine("[UWAGA] Nie połączono z serwerem - tryb offline");
            Title = "Drone Delivery System - Offline";
        }
        ShowMenu();
    }

    private void OnOrderAdded(DroneOrder order)
    {
        _selectedOrder = order;
        Dispatcher.UIThread.Post(() => {
            HideLoading();  // Ukryj loading przed przejściem
            ShowMapPage();
            // Rysuj trasę po załadowaniu strony mapy
            DrawRoute(order);
        });
    }

    private async void OnErrorOccurred(string message)
    {
        // Prosta implementacja alertu
        var dialog = new Window
        {
            Title = "Błąd",
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        
        var stack = new StackPanel { Margin = new Thickness(20), Spacing = 20 };
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var okBtn = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Center };
        okBtn.Click += (s, e) => dialog.Close();
        stack.Children.Add(okBtn);
        dialog.Content = stack;
        
        await dialog.ShowDialog(this);
    }

    private void InitializeMap()
    {
        _mapControl = new MapControl();
        _mapControl.Map.Layers.Add(OpenStreetMap.CreateTileLayer());
        
        // Warstwa tras (pod dronami)
        _routeLayer = new MemoryLayer { Name = "Routes", Style = null };
        _mapControl.Map.Layers.Add(_routeLayer);
        
        // Warstwa dronów (na wierzchu)
        _droneLayer = new MemoryLayer { Name = "Drones", Style = null };
        _mapControl.Map.Layers.Add(_droneLayer);

        var center = SphericalMercator.FromLonLat(21.0122, 52.2297);
        _mapControl.Map.Navigator.CenterOnAndZoomTo(new MPoint(center.x, center.y), 15);
    }
    
    // Helper do pobierania ścieżki do Assets
    private string? GetAssetPath(string fileName)
    {
        // Znajdź katalog Assets względem exe
        var exePath = Assembly.GetExecutingAssembly().Location;
        var exeDir = System.IO.Path.GetDirectoryName(exePath);
        
        if (exeDir == null) return null;
        
        // Sprawdź różne możliwe lokalizacje
        var paths = new[]
        {
            System.IO.Path.Combine(exeDir, "Assets", fileName),
            System.IO.Path.Combine(exeDir, "..", "..", "..", "Assets", fileName),
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName),
            // Ścieżka developerska
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Assets", fileName),
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "DroneDeliveryLinux", "Assets", fileName),
        };
        
        foreach (var path in paths)
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }
        
        return null;
    }

    // Cache dla URI SVG (renderer Skia poprawnie je obsługuje)
    private string? _droneSvgUri;
    private string? _pinSvgUri;

    // Helper do pobrania URI (file://) pliku SVG
    private string? GetSvgUri(string fileName)
    {
        var path = GetAssetPath(fileName);
        if (path == null || !File.Exists(path)) return null;
        return new Uri(path).AbsoluteUri;
    }

    // --- HELPERY UI ---

    private PathIcon CreateIcon(string data, AvBrush brush, double size = 24)
    {
        return new PathIcon
        {
            Data = Avalonia.Media.Geometry.Parse(data),
            Foreground = brush,
            Width = size,
            Height = size
        };
    }

    private DispatcherTimer? _spinnerTimer;
    
    /// <summary>
    /// Pokazuje overlay z animacją loading na całej aplikacji
    /// </summary>
    private void ShowLoading(string message = "Przetwarzanie...")
    {
        if (_loadingOverlay != null) return;

        _loadingOverlay = new Border
        {
            Background = new SolidColorBrush(AvColor.Parse("#AAFFFFFF")), // Półprzezroczyste białe tło
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ZIndex = 1000
        };

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 20
        };

        // Spinner - łuk który się obraca
        var arc = new Arc
        {
            Width = 50,
            Height = 50,
            Stroke = PrimaryBrush,
            StrokeThickness = 5,
            StartAngle = 0,
            SweepAngle = 270
        };
        
        // Animacja obrotu przez timer
        var rotateTransform = new RotateTransform();
        arc.RenderTransform = rotateTransform;
        arc.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        
        _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double angle = 0;
        _spinnerTimer.Tick += (s, e) =>
        {
            angle = (angle + 8) % 360;
            rotateTransform.Angle = angle;
        };
        _spinnerTimer.Start();
        
        content.Children.Add(arc);

        // Tekst
        content.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = TextDarkBrush,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        _loadingOverlay.Child = content;

        // Zawsze zawijamy content w nowy Grid aby overlay był na całej aplikacji
        var currentContent = Content as Control;
        var rootGrid = new Grid();
        Content = null;
        if (currentContent != null)
        {
            rootGrid.Children.Add(currentContent);
        }
        rootGrid.Children.Add(_loadingOverlay);
        Content = rootGrid;
    }

    /// <summary>
    /// Ukrywa overlay loading
    /// </summary>
    private void HideLoading()
    {
        if (_loadingOverlay == null) return;

        // Zatrzymaj timer spinnera
        _spinnerTimer?.Stop();
        _spinnerTimer = null;

        if (Content is Grid grid && grid.Children.Contains(_loadingOverlay))
        {
            grid.Children.Remove(_loadingOverlay);
            // Przywróć oryginalny content jeśli został tylko jeden element
            if (grid.Children.Count == 1 && grid.Children[0] is Control originalContent)
            {
                grid.Children.Clear();
                Content = originalContent;
            }
        }
        _loadingOverlay = null;
    }

    private Border CreateModernButton(string text, string iconPath, AvBrush bg, AvBrush fg, Func<Task>? asyncOnClick = null, Action? onClick = null)
    {
        var border = new Border
        {
            Background = bg,
            CornerRadius = new CornerRadius(12),
            Height = 70,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 4, Blur = 10, Color = AvColor.Parse("#20000000") })
        };
        
        // Użyj PointerReleased zamiast PointerPressed dla lepszej responsywności
        border.PointerReleased += async (s, e) => 
        {
            e.Handled = true;
            try
            {
                if (asyncOnClick != null)
                    await asyncOnClick();
                else
                    onClick?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Button error: {ex.Message}");
            }
        };

        var stack = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 15,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false  // Pozwól zdarzeniom przejść do Border
        };
        
        stack.Children.Add(CreateIcon(iconPath, fg, 28));
        stack.Children.Add(new TextBlock 
        { 
            Text = text, 
            Foreground = fg,
            FontSize = 18, 
            FontWeight = FontWeight.Bold, 
            VerticalAlignment = VerticalAlignment.Center 
        });

        border.Child = stack;
        return border;
    }

    // --- EKRANY ---

    private void ShowMenu()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto, *"),
            Background = BackgroundBrush
        };

        // Header Section
        var header = new StackPanel 
        { 
            Margin = new Thickness(0, 80, 0, 50), 
            Spacing = 15,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        
        // Logo (Simulated)
        var logoIcon = CreateIcon(IconBoxPath, PrimaryBrush, 64);
        header.Children.Add(logoIcon);

        header.Children.Add(new TextBlock 
        { 
            Text = "Drone Delivery System", 
            FontSize = 36, 
            FontWeight = FontWeight.Black, 
            Foreground = TextDarkBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        
        header.Children.Add(new TextBlock 
        { 
            Text = "Wybierz operację, aby rozpocząć", 
            FontSize = 16, 
            Foreground = TextLightBrush, 
            HorizontalAlignment = HorizontalAlignment.Center 
        });

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // Buttons Section
        var btnStack = new StackPanel 
        { 
            Spacing = 25, 
            Width = 400,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Button 1: Moje Paczki (Outline Style)
        // Białe tło, Fioletowy tekst/ikona, Fioletowa ramka
        var btnMap = CreateModernButton("Moje Paczki i Mapa", IconMapPath, AvBrushes.White, PrimaryBrush, onClick: ShowMapPage);
        btnMap.BorderBrush = PrimaryBrush;
        btnMap.BorderThickness = new Thickness(2);
        
        // Button 2: Nadaj Paczkę (Filled Style)
        // Fioletowe tło, Biały tekst/ikona
        var btnSend = CreateModernButton("Nadaj Paczkę", IconSendPath, PrimaryBrush, AvBrushes.White, onClick: ShowSendPage);

        btnStack.Children.Add(btnMap);
        btnStack.Children.Add(btnSend);

        Grid.SetRow(btnStack, 1);
        root.Children.Add(btnStack);

        Content = root;
    }

    private void ShowMapPage()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto, *"),
            Background = AvBrushes.White
        };

        grid.Children.Add(CreateNavbar("Mapa Przesyłek"));

        var contentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("280, *")
        };

        // Panel boczny z jedną listą paczek
        var leftPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto, *"),
            Background = AvBrushes.White
        };

        // Header
        leftPanel.Children.Add(CreateListHeader("Moje Paczki", 0));
        
        // Lista wszystkich paczek
        var listAll = new ListBox 
        { 
            Background = AvBrushes.Transparent, 
            BorderThickness = new Thickness(0),
            Padding = new Thickness(5)
        };
        listAll.ItemTemplate = CreateOrderTemplate(false);
        listAll.ItemsSource = _viewModel.AllOrders;
        listAll.SelectionChanged += OnOrderSelected;
        Grid.SetRow(listAll, 1);
        leftPanel.Children.Add(listAll);

        Grid.SetColumn(leftPanel, 0);
        contentGrid.Children.Add(leftPanel);

        // Mapa - odłącz od poprzedniego rodzica
        if (_mapControl.Parent is Panel p) p.Children.Remove(_mapControl);
        else if (_mapControl.Parent is Decorator d) d.Child = null;
        else if (_mapControl.Parent is ContentControl c) c.Content = null;
        
        var mapContainer = new Border
        {
            BorderBrush = AvBrushes.LightGray,
            BorderThickness = new Thickness(1,0,0,0),
            Child = _mapControl
        };
        
        Grid.SetColumn(mapContainer, 1);
        contentGrid.Children.Add(mapContainer);

        Grid.SetRow(contentGrid, 1);
        grid.Children.Add(contentGrid);

        Content = grid;
        
        // Jeśli jest wybrana paczka, narysuj jej trasę
        if (_selectedOrder != null)
        {
            DrawRoute(_selectedOrder);
        }
    }

    private void OnOrderSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is DroneOrder order)
        {
            _selectedOrder = order;
            DrawRoute(order);
            lb.SelectedItem = null; // Odznacz element
        }
    }

    private void DrawRoute(DroneOrder order)
    {
        var features = new List<IFeature>();

        // Styl linii trasy - ciągła niebieska linia
        var lineStyle = new VectorStyle
        {
            Line = new MPen { Color = MColor.FromString("#3B82F6"), Width = 5, PenStyle = PenStyle.Solid }
        };

        var start = SphericalMercator.FromLonLat(order.OriginLng, order.OriginLat);
        var end = SphericalMercator.FromLonLat(order.DestLng, order.DestLat);

        // Linia trasy
        var lineString = new LineString(new[] 
        { 
            new Coordinate(start.x, start.y), 
            new Coordinate(end.x, end.y) 
        });
        var lineEntity = new GeometryFeature(lineString);
        lineEntity.Styles.Add(lineStyle);
        features.Add(lineEntity);

        // Pobierz ścieżkę SVG dla ikony pinezki (cachowane)
        _pinSvgUri ??= GetSvgUri("pin.svg");
        var pinUri = _pinSvgUri;
        
        // Pin Start - renderuj wyłącznie z pin.svg (zwróćmy się do oryginalnego obrazka)
        var startPin = new PointFeature(new MPoint(start.x, start.y));
        if (pinUri != null)
        {
            startPin.Styles.Add(new ImageStyle
            {
                Image = pinUri,
                SymbolScale = PinScale,
                RelativeOffset = new RelativeOffset(0.0, PinOffsetY)  // Czubek pinezki na punkcie
            });
        }
        features.Add(startPin);

        // Pin Cel - renderuj wyłącznie z pin.svg
        var endPin = new PointFeature(new MPoint(end.x, end.y));
        if (pinUri != null)
        {
            endPin.Styles.Add(new ImageStyle
            {
                Image = pinUri,
                SymbolScale = PinScale,
                RelativeOffset = new RelativeOffset(0.0, PinOffsetY)  // Czubek pinezki na punkcie
            });
        }
        features.Add(endPin);

        _routeLayer.Features = features;
        _routeLayer.DataHasChanged();

        // Wyśrodkuj mapę i dopasuj zoom do trasy
        var minLat = Math.Min(order.OriginLat, order.DestLat);
        var maxLat = Math.Max(order.OriginLat, order.DestLat);
        var minLng = Math.Min(order.OriginLng, order.DestLng);
        var maxLng = Math.Max(order.OriginLng, order.DestLng);
        
        // Dodaj margines 20%
        var latMargin = (maxLat - minLat) * 0.2;
        var lngMargin = (maxLng - minLng) * 0.2;
        
        var min = SphericalMercator.FromLonLat(minLng - lngMargin, minLat - latMargin);
        var max = SphericalMercator.FromLonLat(maxLng + lngMargin, maxLat + latMargin);
        
        // Użyj NavigateTo z bounding box dla idealnego dopasowania
        var extent = new MRect(min.x, min.y, max.x, max.y);
        _mapControl.Map.Navigator.ZoomToBox(extent);
        _mapControl.RefreshGraphics();
    }

    private void ShowSendPage()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto, *"),
            Background = new SolidColorBrush(AvColor.Parse("#FAFAFA"))
        };

        root.Children.Add(CreateNavbar("Formularz Nadania"));

        var scroll = new ScrollViewer();
        var centerPanel = new StackPanel 
        { 
            Spacing = 25, 
            Margin = new Thickness(0, 40, 0, 40), 
            Width = 500, 
            HorizontalAlignment = HorizontalAlignment.Center 
        };

        // Sekcja Adresowa
        var addrCard = new Border 
        { 
            Background = AvBrushes.White,
            BorderBrush = AvBrushes.LightGray, 
            BorderThickness = new Thickness(1), 
            CornerRadius = new CornerRadius(8), 
            Padding = new Thickness(25),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 2, Blur = 5, Color = AvColor.Parse("#10000000") })
        };
        var addrStack = new StackPanel { Spacing = 15 };
        
        addrStack.Children.Add(CreateStyledTextBox("Adres Nadania", "EntryOrigin"));
        addrStack.Children.Add(new Separator { Height = 1, Background = AvBrushes.WhiteSmoke });
        addrStack.Children.Add(CreateStyledTextBox("Adres Dostawy", "EntryDest"));
        
        addrCard.Child = addrStack;
        centerPanel.Children.Add(addrCard);

        // Sekcja Dat
        var datesGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, 20, *") };
        
        var d1 = CreateDatePicker("Data Nadania", "SendDate");
        Grid.SetColumn(d1, 0);
        datesGrid.Children.Add(d1);

        var d2 = CreateDatePicker("Oczekiwana Dostawa", "DeliverDate");
        Grid.SetColumn(d2, 2);
        datesGrid.Children.Add(d2);

        // Walidacja dat nadania i dostawy (przywracanie poprzedniej wartości i komunikat)
        var sendDatePicker = d1.Children.OfType<CalendarDatePicker>().FirstOrDefault();
        var deliverDatePicker = d2.Children.OfType<CalendarDatePicker>().FirstOrDefault();

        DateTime? lastSendDate = sendDatePicker?.SelectedDate;
        DateTime? lastDeliverDate = deliverDatePicker?.SelectedDate;
        bool suppressSendHandler = false;
        bool suppressDeliverHandler = false;

        if (sendDatePicker != null)
        {
            // Validate only after user interaction (click) or when focus is lost — avoid validating on hover
            void ValidateSendDate()
            {
                if (suppressSendHandler) return;
                var newDate = sendDatePicker.SelectedDate;
                if (newDate.HasValue && newDate.Value.Date < DateTime.Today)
                {
                    suppressSendHandler = true;
                    sendDatePicker.SelectedDate = lastSendDate ?? DateTime.Today;
                    suppressSendHandler = false;
                    _ = ShowValidationErrorAsync("Data nadania nie może być wcześniejsza niż dzisiaj.");
                }
                else
                {
                    lastSendDate = newDate;
                    if (deliverDatePicker != null && deliverDatePicker.SelectedDate.HasValue && lastSendDate.HasValue && deliverDatePicker.SelectedDate.Value.Date < lastSendDate.Value.Date)
                    {
                        suppressDeliverHandler = true;
                        deliverDatePicker.SelectedDate = lastSendDate;
                        suppressDeliverHandler = false;
                        _ = ShowValidationErrorAsync("Data dostawy nie może być wcześniejsza niż data nadania.");
                    }
                }
            }

            sendDatePicker.PointerReleased += (s, e) => ValidateSendDate();
            sendDatePicker.LostFocus += (s, e) => ValidateSendDate();
        }

        if (deliverDatePicker != null)
        {
            void ValidateDeliverDate()
            {
                if (suppressDeliverHandler) return;

                var newDate = deliverDatePicker.SelectedDate;
                var minAllowed = lastSendDate ?? DateTime.Today;
                if (newDate.HasValue && newDate.Value.Date < minAllowed.Date)
                {
                    suppressDeliverHandler = true;
                    deliverDatePicker.SelectedDate = lastDeliverDate ?? minAllowed;
                    suppressDeliverHandler = false;
                    _ = ShowValidationErrorAsync("Data dostawy nie może być wcześniejsza niż data nadania.");
                }
                else
                {
                    lastDeliverDate = newDate;
                }
            }

            deliverDatePicker.PointerReleased += (s, e) => ValidateDeliverDate();
            deliverDatePicker.LostFocus += (s, e) => ValidateDeliverDate();
        }

        // Lokalna asynchroniczna metoda dialogowa (lokalna funkcja, bez modyfikatora)
        async Task ShowValidationErrorAsync(string message)
        {
            var dialog = new Window
            {
                Title = "Błąd walidacji",
                Width = 360,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var stack = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            var okBtn = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Center, Width = 80 };
            okBtn.Click += (ss, ee) => dialog.Close();
            stack.Children.Add(okBtn);
            dialog.Content = stack;

            await dialog.ShowDialog(this);
        }
        centerPanel.Children.Add(datesGrid);

        // Sekcja Wagi
        var weightPanel = new StackPanel { Spacing = 10 };
        weightPanel.Children.Add(new TextBlock { Text = "Waga Paczki", FontSize = 14, Foreground = TextLightBrush });
        
        var wGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto") };
        var slider = new Slider { Minimum = 0.5, Maximum = 5, Value = 1.0, Foreground = PrimaryBrush };
        slider.Bind(Slider.ValueProperty, new Binding("SliderWeight"));
        Grid.SetColumn(slider, 0);
        wGrid.Children.Add(slider);
        
        var lblW = new TextBlock 
        { 
            FontWeight = FontWeight.Bold, 
            FontSize=16, 
            Margin = new Thickness(15,0,0,0),
            Foreground = TextDarkBrush // Fix: Dark text for weight value
        };
        lblW.Bind(TextBlock.TextProperty, new Binding("SliderWeight") { StringFormat = "{0:F1} kg" });
        Grid.SetColumn(lblW, 1);
        wGrid.Children.Add(lblW);
        
        weightPanel.Children.Add(wGrid);
        centerPanel.Children.Add(weightPanel);

        // Koszt
        var costBox = new Border 
        { 
            Background = new SolidColorBrush(AvColor.Parse("#F3F4F6")), // Lighter Gray 
            CornerRadius = new CornerRadius(6), 
            Padding = new Thickness(15), 
            Margin = new Thickness(0,10,0,10),
            BorderBrush = new SolidColorBrush(AvColor.Parse("#E5E7EB")),
            BorderThickness = new Thickness(1)
        };
        var costTxt = new TextBlock 
        { 
            HorizontalAlignment = HorizontalAlignment.Center, 
            FontSize = 20, 
            FontWeight = FontWeight.Bold,
            Foreground = TextDarkBrush // Fix: Dark text
        };
        costTxt.Bind(TextBlock.TextProperty, new Binding("LabelCost"));
        costBox.Child = costTxt;
        centerPanel.Children.Add(costBox);

        // Submit Button - użyj standardowego Button dla niezawodności
        var submitBtn = new Button
        {
            Background = PrimaryBrush,
            Foreground = AvBrushes.White,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Height = 70,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(12),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        
        var btnContent = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 15
        };
        btnContent.Children.Add(CreateIcon(IconSendPath, AvBrushes.White, 28));
        btnContent.Children.Add(new TextBlock 
        { 
            Text = "ZAPŁAĆ I WYŚLIJ", 
            Foreground = AvBrushes.White,
            FontSize = 18, 
            FontWeight = FontWeight.Bold, 
            VerticalAlignment = VerticalAlignment.Center 
        });
        submitBtn.Content = btnContent;
        
        submitBtn.Click += async (s, e) =>
        {
            // Zapobieganie wielokrotnym kliknięciom
            if (!submitBtn.IsEnabled) return;

            try
            {
                if (_viewModel.SendPackageCommand.CanExecute(null))
                {
                    submitBtn.IsEnabled = false;
                    ShowLoading("Wysyłanie paczki...");
                    
                    // Krótkie opóźnienie dla UX
                    await Task.Delay(300); 
                    
                    // Wykonanie komendy. Jeśli wystąpi błąd walidacji w ViewModel,
                    // zostanie wyświetlony alert (przez ErrorOccurred), a zadanie się zakończy.
                    await _viewModel.SendPackageCommand.ExecuteAsync(null);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Button error: {ex.Message}");
            }
            finally
            {
                // KLUCZOWA POPRAWKA:
                // Zawsze ukrywamy loading i odblokowujemy przycisk po zakończeniu operacji.
                // Niezależnie czy sukces (przejście na mapę), czy błąd walidacji.
                HideLoading();
                submitBtn.IsEnabled = true;
            }
        };
        
        centerPanel.Children.Add(submitBtn);

        scroll.Content = centerPanel;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        Content = root;
    }

    // --- ELEMENTY UI FORMULARZA ---

    private TextBox CreateStyledTextBox(string watermark, string bindingPath)
    {
        var tb = new TextBox 
        { 
            Watermark = watermark, 
            BorderBrush = new SolidColorBrush(AvColor.Parse("#D1D5DB")), // Light Gray Border
            BorderThickness = new Thickness(1),
            Background = AvBrushes.White,
            Foreground = TextDarkBrush, // Dark Text
            FontSize = 16,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10),
            DataContext = _viewModel  // Ustaw DataContext bezpośrednio
        };
        // Fix: TwoWay binding + PropertyChanged trigger
        tb.Bind(TextBox.TextProperty, new Binding(bindingPath) 
        { 
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged 
        });
        return tb;
    }

    private StackPanel CreateDatePicker(string label, string bindingPath)
    {
        var s = new StackPanel { Spacing = 8 };
        s.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = TextLightBrush, FontWeight = FontWeight.SemiBold });
        
        var picker = new CalendarDatePicker 
        { 
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = AvBrushes.White,
            Foreground = TextDarkBrush,
            BorderBrush = AvBrushes.LightGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5)
        };
        
        picker.Bind(CalendarDatePicker.SelectedDateProperty, new Binding(bindingPath)
        {
            Mode = BindingMode.TwoWay
        });
        
        s.Children.Add(picker);
        return s;
    }

    private Control CreateNavbar(string title)
    {
        var border = new Border 
        { 
            Background = AvBrushes.White, 
            BorderBrush = new SolidColorBrush(AvColor.Parse("#E5E7EB")), 
            BorderThickness = new Thickness(0,0,0,1),
            Padding = new Thickness(20),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 2, Blur = 4, Color = AvColor.Parse("#08000000") })
        };
        
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto") };

        var btnBack = new Button 
        { 
            Background = AvBrushes.Transparent, 
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10)
        };
        btnBack.Content = CreateIcon(IconBackPath, TextDarkBrush, 24);
        btnBack.Click += (s, e) => ShowMenu();
        Grid.SetColumn(btnBack, 0);
        grid.Children.Add(btnBack);

        var lblTitle = new TextBlock 
        { 
            Text = title, 
            FontWeight = FontWeight.SemiBold, 
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(lblTitle, 1);
        grid.Children.Add(lblTitle);

        border.Child = grid;
        return border;
    }

    private Border CreateListHeader(string text, int row)
    {
        var border = new Border 
        { 
            Background = new SolidColorBrush(AvColor.Parse("#E0E0E0")), // Szary jak na Mac
            Padding = new Thickness(10)
        };
        border.Child = new TextBlock 
        { 
            Text = text, 
            FontWeight = FontWeight.Bold, 
            FontSize = 13, 
            Foreground = TextDarkBrush 
        };
        Grid.SetRow(border, row);
        return border;
    }

    // --- MAPA ---

    private void UpdateMap(object? sender, EventArgs e)
    {
        if (_viewModel == null || _mapControl.Parent == null) return;

        var features = new List<IFeature>();
        
        // Pobierz ścieżkę SVG dla ikony drona (cachowane)
        _droneSvgUri ??= GetSvgUri("drone.svg");
        var droneUri = _droneSvgUri;

        // Szukaj aktywnie lecącego drona (pierwszy, który nie jest dostarczony)
        var allOrders = _viewModel.OutgoingOrders.Concat(_viewModel.IncomingOrders);

        foreach (var order in allOrders)
        {
            if (order.Status.Contains("Dostarczono")) continue;

            var current = SphericalMercator.FromLonLat(order.CurrentLng, order.CurrentLat);

            // Oblicz kąt rotacji drona na podstawie kierunku lotu
            var dest = SphericalMercator.FromLonLat(order.DestLng, order.DestLat);
            var angle = Math.Atan2(dest.y - current.y, dest.x - current.x) * 180 / Math.PI - 90;

            // Dron - niebieski okrąg z ikoną drona
            var droneFeature = new PointFeature(new MPoint(current.x, current.y));
            
            // Niebieski okrąg jako tło
            droneFeature.Styles.Add(new SymbolStyle
            {
                Fill = new MBrush(MColor.FromString("#3B82F6")),  // Niebieski
                SymbolScale = DroneBackdropScale,
                SymbolType = SymbolType.Ellipse,
                        Outline = new MPen { Color = MColor.White, Width = 0.5 }
            });
            
            // Ikona drona SVG lub fallback trójkąt
            if (droneUri != null)
            {
                droneFeature.Styles.Add(new ImageStyle
                {
                    Image = droneUri,
                    SymbolScale = DroneIconScale,
                });
            }
            else
            {
                // Fallback - biały trójkąt
                droneFeature.Styles.Add(new SymbolStyle
                {
                    Fill = new MBrush(MColor.White),
                    SymbolScale = 0.8,
                    SymbolType = SymbolType.Triangle,
                    SymbolRotation = angle
                });
            }
            features.Add(droneFeature);
            
            break; // Obsługujemy jednego drona naraz (jak na Mac)
        }

        _droneLayer.Features = features;
        _droneLayer.DataHasChanged();
        _mapControl.RefreshGraphics();
    }

    private FuncDataTemplate<DroneOrder> CreateOrderTemplate(bool isIncoming)
    {
        return new FuncDataTemplate<DroneOrder>((order, ns) =>
        {
            var card = new Border
            {
                Padding = new Thickness(8),
                Margin = new Thickness(5),
                Background = isIncoming ? new SolidColorBrush(AvColor.Parse("#E8F4FF")) : AvBrushes.White,
                BorderBrush = AvBrushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5)
            };

            // Główny grid z zawartością i przyciskiem usuwania
            var mainGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*, Auto")
            };

            var stack = new StackPanel { Spacing = 3 };

            // Główny tekst - adres docelowy (dla wychodzących) lub nadawcy (dla przychodzących)
            var mainText = new TextBlock 
            { 
                FontSize = 12, 
                FontWeight = FontWeight.Bold, 
                Foreground = TextDarkBrush, 
                TextTrimming = TextTrimming.CharacterEllipsis 
            };
            
            if (isIncoming)
            {
                mainText.Bind(TextBlock.TextProperty, new Binding("OriginAddress") { StringFormat = "Od: {0}" });
            }
            else
            {
                mainText.Bind(TextBlock.TextProperty, new Binding("DestinationAddress") { StringFormat = "Do: {0}" });
            }
            
            stack.Children.Add(mainText);

            // Status
            var txtStatus = new TextBlock { FontSize = 11, Foreground = TextLightBrush };
            txtStatus.Bind(TextBlock.TextProperty, new Binding("Status"));
            stack.Children.Add(txtStatus);

            // Progress Bar
            var prog = new ProgressBar 
            { 
                Height = 6, 
                Minimum = 0, 
                Maximum = 1, 
                Foreground = isIncoming ? AvBrushes.Green : PrimaryBrush,
                Background = new SolidColorBrush(AvColor.Parse("#E5E7EB")),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            prog.Bind(ProgressBar.ValueProperty, new Binding("Progress"));
            stack.Children.Add(prog);

            Grid.SetColumn(stack, 0);
            mainGrid.Children.Add(stack);

            // Przycisk usuwania (śmietnik)
            var deleteBtn = new Button
            {
                Background = AvBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4),
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Content = CreateIcon(IconDeletePath, new SolidColorBrush(AvColor.Parse("#EF4444")), 18)
            };
            
            deleteBtn.Click += async (s, e) =>
            {
                e.Handled = true;
                deleteBtn.IsEnabled = false;
                ShowLoading("Usuwanie paczki...");
                
                await Task.Delay(200); // Krótkie opóźnienie dla efektu wizualnego
                await _viewModel.DeleteOrderAsync(order);
                
                HideLoading();
            };
            
            Grid.SetColumn(deleteBtn, 1);
            mainGrid.Children.Add(deleteBtn);

            card.Child = mainGrid;
            return card;
        }, true);
    }
}