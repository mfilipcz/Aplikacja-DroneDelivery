using DroneServer;

namespace DroneDelivery;

public partial class AdminPage : ContentPage
{
    private readonly DroneService.DroneServiceClient _client;
    private readonly ClientSession _session;

    public AdminPage(DroneService.DroneServiceClient client, ClientSession session)
    {
        InitializeComponent();
        _client = client;
        _session = session;
        LoadOrders(); // Startujemy od paczek
    }

    // --- ZAKŁADKI ---
    private void OnTabChanged(object sender, EventArgs e)
    {
        var btn = sender as Button;
        if (btn == TabOrdersBtn)
        {
            OrdersView.IsVisible = true;
            UsersView.IsVisible = false;
            
            // Kolorowanie przycisków
            TabOrdersBtn.BackgroundColor = Color.FromArgb("#6200EE"); TabOrdersBtn.TextColor = Colors.White;
            TabUsersBtn.BackgroundColor = Colors.LightGray; TabUsersBtn.TextColor = Colors.Black;
            LoadOrders();
        }
        else
        {
            OrdersView.IsVisible = false;
            UsersView.IsVisible = true;

            TabUsersBtn.BackgroundColor = Color.FromArgb("#6200EE"); TabUsersBtn.TextColor = Colors.White;
            TabOrdersBtn.BackgroundColor = Colors.LightGray; TabOrdersBtn.TextColor = Colors.Black;
            LoadUsers();
        }
    }

    // --- LOGIKA PACZEK ---
    private async void LoadOrders()
    {
        try
        {
            var response = await _client.GetAllOrdersAsync(new Empty());
            OrdersList.ItemsSource = response.Orders;
            OrdersView.IsRefreshing = false;
        }
        catch (Exception ex) { await DisplayAlert("Błąd", ex.Message, "OK"); }
    }
    
    private void OnRefreshOrders(object sender, EventArgs e) => LoadOrders();

    private async void OnApproveClicked(object sender, EventArgs e) => await ChangeStatus(sender, "W drodze");
    private async void OnRejectClicked(object sender, EventArgs e) => await ChangeStatus(sender, "Odrzucono");

    private async Task ChangeStatus(object sender, string status)
    {
        var btn = sender as Button;
        var id = btn?.CommandParameter as string;
        if(id != null)
        {
            await _client.UpdateOrderStatusAsync(new StatusRequest { OrderId = id, NewStatus = status, IsAdmin = true });
            LoadOrders();
        }
    }

    // --- LOGIKA UŻYTKOWNIKÓW ---
    private async void LoadUsers()
    {
        try
        {
            var response = await _client.GetAllUsersAsync(new Empty());
            UsersList.ItemsSource = response.Users;
        }
        catch (Exception ex) { await DisplayAlert("Błąd", ex.Message, "OK"); }
    }

    private async void OnAddUserClicked(object sender, EventArgs e)
    {
        string username = NewUserEntry.Text;
        if(string.IsNullOrWhiteSpace(username)) return;

        // Szybkie dodawanie z domyślnym hasłem "user"
        var result = await _client.RegisterUserAsync(new RegisterUserRequest { Username = username, Password = "user" });
        if (result.Success)
        {
            NewUserEntry.Text = "";
            LoadUsers();
            await DisplayAlert("Sukces", $"Dodano użytkownika: {username}\nHasło: user", "OK");
        }
        else await DisplayAlert("Błąd", result.Message, "OK");
    }

    private async void OnDeleteUserClicked(object sender, EventArgs e)
    {
        var btn = sender as Button;
        var username = btn?.CommandParameter as string;
        
        bool answer = await DisplayAlert("Usuwanie", $"Usunąć użytkownika {username}?", "Tak", "Nie");
        if (answer)
        {
            var result = await _client.DeleteUserAsync(new UserRequest { Username = username });
            if (result.Success) LoadUsers();
            else await DisplayAlert("Błąd", result.Message, "OK");
        }
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