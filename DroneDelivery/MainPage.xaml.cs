using DroneServer;

namespace DroneDelivery;

public partial class MainPage : ContentPage
{
    private readonly DroneService.DroneServiceClient _client;
    // Przyjmujemy symulator tylko po to, żeby system go utworzył i uruchomił timer
    private readonly DroneSimulatorService _simulator; 

    public MainPage(DroneService.DroneServiceClient client, DroneSimulatorService simulator)
    {
        InitializeComponent();
        _client = client;
        _simulator = simulator; // To uruchamia konstruktor serwisu i startuje animację w tle
    }

    private async void OnSendPackageClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new SendPackagePage(_client));
    }

    private async void OnMyPackagesClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new TrackingPage(_client));
    }
}