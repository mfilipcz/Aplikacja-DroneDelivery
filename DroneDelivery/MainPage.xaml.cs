using DroneServer;

namespace DroneDelivery;

public partial class MainPage : ContentPage
{
    private readonly DroneService.DroneServiceClient _client;
    private readonly DroneSimulatorService _simulator;
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
        await Navigation.PushAsync(new SendPackagePage(_client, _session));
    }

    private async void OnMyPackagesClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new TrackingPage(_client, _session));
    }

    // --- WYLOGOWANIE (POPRAWKA DLA .NET 9) ---
    private void OnLogoutClicked(object sender, EventArgs e)
    {
        _session.ClientId = null;
        _session.IsAdmin = false;
        Preferences.Remove("moje_id_klienta_v2");

        if (Application.Current != null && Application.Current.Windows.Count > 0)
        {
            Application.Current.Windows[0].Page = new NavigationPage(new LoginPage(_client, _session));
        }
    }
}