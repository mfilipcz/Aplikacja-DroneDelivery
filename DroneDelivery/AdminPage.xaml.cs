using DroneServer;

namespace DroneDelivery;

public partial class AdminPage : ContentPage
{
    private readonly DroneService.DroneServiceClient _client;
    private readonly ClientSession _session;

    public AdminPage(DroneService.DroneServiceClient client, ClientSession session)
    {
        InitializeComponent(); // Tu był błąd wcześniej przez brak Convertera
        _client = client;
        _session = session;
    }

    // Ładujemy dane dopiero gdy strona się wyświetli (bezpieczniej niż w konstruktorze)
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadData();
    }

    private async Task LoadData()
    {
        try
        {
            var response = await _client.GetAllOrdersAsync(new Empty());
            OrdersList.ItemsSource = response.Orders;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Błąd", $"Nie udało się pobrać paczek: {ex.Message}", "OK");
        }
    }

    private void OnRefreshClicked(object sender, EventArgs e) => _ = LoadData();

    private async void OnApproveClicked(object sender, EventArgs e)
    {
        var button = sender as Button;
        var orderId = button?.CommandParameter as string;
        await UpdateStatus(orderId, "W drodze");
    }

    private async void OnRejectClicked(object sender, EventArgs e)
    {
        var button = sender as Button;
        var orderId = button?.CommandParameter as string;
        await UpdateStatus(orderId, "Odrzucono");
    }

    private async Task UpdateStatus(string orderId, string status)
    {
        if (string.IsNullOrEmpty(orderId)) return;

        try
        {
            var response = await _client.UpdateOrderStatusAsync(new StatusRequest 
            { 
                OrderId = orderId, 
                NewStatus = status, 
                IsAdmin = true 
            });

            if (response.Success)
            {
                await LoadData(); // Odśwież listę po zmianie
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Błąd", ex.Message, "OK");
        }
    }

    // --- LOGIKA WYLOGOWANIA ---
    private void OnLogoutClicked(object sender, EventArgs e)
    {
        // 1. Czyścimy sesję w RAM
        _session.ClientId = null;
        _session.IsAdmin = false;

        // 2. WAŻNE: Czyścimy zapamiętane logowanie w telefonie (z symulatora)
        // Musisz użyć TEGO SAMEGO klucza co w DroneSimulatorService
        Preferences.Remove("moje_id_klienta_v2");

        // 3. Wracamy do ekranu logowania
        Application.Current.MainPage = new NavigationPage(new LoginPage(_client, _session));
    }
}