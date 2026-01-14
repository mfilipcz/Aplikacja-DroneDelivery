using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DroneDeliveryLinux.ViewModels;

namespace DroneDeliveryLinux.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        Title = "Drone Delivery - Linux Edition";
        Width = 800;
        Height = 600;
        Background = Brushes.White;

        var vm = new MainViewModel();
        DataContext = vm;

        var tabs = new TabControl
        {
            TabStripPlacement = Dock.Bottom
        };

        // Tab 1: Orders
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(300, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        var leftPanel = new StackPanel { Background = Brushes.LightGray };
        leftPanel.Children.Add(new TextBlock { Text = "Wychodzące", FontWeight = FontWeight.Bold, Padding = new Thickness(10) });
        
        var listOutgoing = new ListBox { Height = 250 };
        listOutgoing.ItemsSource = vm.OutgoingOrders;
        leftPanel.Children.Add(listOutgoing);

        leftPanel.Children.Add(new TextBlock { Text = "Przychodzące", FontWeight = FontWeight.Bold, Padding = new Thickness(10) });
        var listIncoming = new ListBox { Height = 250 };
        listIncoming.ItemsSource = vm.IncomingOrders;
        leftPanel.Children.Add(listIncoming);

        Grid.SetColumn(leftPanel, 0);
        grid.Children.Add(leftPanel);

        var rightPanel = new Panel { Background = Brushes.Gray };
        rightPanel.Children.Add(new TextBlock { 
            Text = "MAPA LINUX (C# UI)", 
            VerticalAlignment = VerticalAlignment.Center, 
            HorizontalAlignment = HorizontalAlignment.Center 
        });
        Grid.SetColumn(rightPanel, 1);
        grid.Children.Add(rightPanel);

        tabs.Items.Add(new TabItem { Header = "Drony", Content = grid });

        // Tab 2: Send
        var sendPanel = new StackPanel { Spacing = 20, Margin = new Thickness(40), MaxWidth = 500 };
        sendPanel.Children.Add(new TextBlock { Text = "Nadaj Paczkę", FontSize = 24, FontWeight = FontWeight.Bold });
        
        var entryOrigin = new TextBox { Watermark = "Adres Nadania" };
        entryOrigin.Bind(TextBox.TextProperty, new Avalonia.Data.Binding("EntryOrigin"));
        sendPanel.Children.Add(entryOrigin);

        var entryDest = new TextBox { Watermark = "Adres Dostawy" };
        entryDest.Bind(TextBox.TextProperty, new Avalonia.Data.Binding("EntryDest"));
        sendPanel.Children.Add(entryDest);

        var btnSend = new Button { 
            Content = "WYŚLIJ", 
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Blue,
            Foreground = Brushes.White
        };
        btnSend.Bind(Button.CommandProperty, new Avalonia.Data.Binding("SendPackageCommand"));
        sendPanel.Children.Add(btnSend);

        tabs.Items.Add(new TabItem { Header = "Nadaj", Content = sendPanel });

        Content = tabs;
    }
}