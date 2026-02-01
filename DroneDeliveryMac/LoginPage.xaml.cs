using DroneServer;
using Grpc.Core;

namespace DroneDelivery;

public partial class LoginPage : ContentPage
{
    private readonly DroneService.DroneServiceClient _client;
    private readonly ClientSession _session;

    public LoginPage(DroneService.DroneServiceClient client, ClientSession session)
    {
        InitializeComponent();
        _client = client;
        _session = session;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        string login = LoginEntry.Text;
        string pass = PasswordEntry.Text;

        try
        {
            var response = await _client.LoginAsync(new LoginRequest { Username = login, Password = pass });

            if (response.Success)
            {
                _session.ClientId = response.ClientId;
                _session.IsAdmin = response.Role == "admin";

                // Zapamiętaj ID
                Preferences.Set("moje_id_klienta_v2", _session.ClientId);

                // --- POPRAWKA DLA .NET 9 (Eliminacja ostrzeżeń) ---
                if (Application.Current != null && Application.Current.Windows.Count > 0)
                {
                    if (_session.IsAdmin)
                    {
                        Application.Current.Windows[0].Page = new NavigationPage(new AdminPage(_client, _session));
                    }
                    else
                    {
                        var simulator = new DroneSimulatorService(_client, _session);
                        Application.Current.Windows[0].Page = new NavigationPage(new MainPage(_client, simulator, _session));
                    }
                }
            }
            else
            {
                await DisplayAlert("Błąd", "Niepoprawne dane logowania.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Błąd połączenia", "Sprawdź czy serwer działa.\n" + ex.Message, "OK");
        }
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        string login = LoginEntry.Text;
        string pass = PasswordEntry.Text;

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(pass))
        {
            await DisplayAlert("Błąd", "Wpisz login i hasło, aby utworzyć konto.", "OK");
            return;
        }

        try
        {
            var response = await _client.RegisterUserAsync(new RegisterUserRequest { Username = login, Password = pass });

            if (response.Success)
            {
                await DisplayAlert("Sukces", "Konto utworzone! Możesz się teraz zalogować.", "OK");
            }
            else
            {
                await DisplayAlert("Błąd", response.Message, "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Błąd", ex.Message, "OK");
        }
    }
}