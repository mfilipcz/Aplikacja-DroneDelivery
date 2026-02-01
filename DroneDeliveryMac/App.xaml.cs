namespace DroneDelivery;

public partial class App : Application
{
    public App(LoginPage loginPage) // Wstrzykujemy LoginPage
    {
        InitializeComponent();
        MainPage = new NavigationPage(loginPage);
    }
}