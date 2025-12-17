namespace DroneDeliveryMac;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        // Nie ustawiamy tutaj MainPage! To było źródłem ostrzeżeń.
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Tutaj tworzymy okno startowe z nawigacją
        var navigationPage = new NavigationPage(new StartPage());
        var window = new Window(navigationPage);
        window.Title = "Drone Delivery System";
        return window;
    }
}