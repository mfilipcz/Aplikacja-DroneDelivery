namespace DroneDeliveryMac;

public partial class StartPage : ContentPage
{
    public StartPage()
    {
        InitializeComponent();
    }

    private async void OnMyPackagesClicked(object sender, EventArgs e)
    {
        // Otwórz zakładkę 0 (Moje Drony)
        await Navigation.PushAsync(new MainPage(0));
    }

    private async void OnSendClicked(object sender, EventArgs e)
    {
        // Otwórz zakładkę 1 (Nadaj)
        await Navigation.PushAsync(new MainPage(1));
    }

    private async void OnReceiveClicked(object sender, EventArgs e)
    {
        // Otwórz zakładkę 2 (Odbierz)
        await Navigation.PushAsync(new MainPage(2));
    }
}