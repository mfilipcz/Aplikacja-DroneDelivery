using DroneServer;

namespace DroneDelivery;

public partial class MainPage : ContentPage
{
    private readonly DroneService.DroneServiceClient _client;
    private readonly DroneSimulatorService _simulator;
    // Dodajemy sesję
    private readonly ClientSession _session;

    public MainPage(DroneService.DroneServiceClient client, DroneSimulatorService simulator, ClientSession session)
    {
        InitializeComponent();
        _client = client;
        _simulator = simulator;
        _session = session;
    }

    private async void OnSendPackageClicked(object sender, EventArgs e)
    {
        // Przekazujemy klienta i sesję
        await Navigation.PushAsync(new SendPackagePage(_client, _session));
    }

    private async void OnMyPackagesClicked(object sender, EventArgs e)
    {
        // Przekazujemy klienta i sesję
        await Navigation.PushAsync(new TrackingPage(_client, _session));
    }
}