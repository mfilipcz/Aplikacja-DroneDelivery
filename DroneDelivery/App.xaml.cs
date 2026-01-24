namespace DroneDelivery;

public partial class App : Application
{
    // Dodajemy parametr do konstruktora, żeby wymusić start serwisu
    public App(DroneSimulatorService simulator) 
    {
        InitializeComponent();
        MainPage = new AppShell(); // Lub NavigationPage(new MainPage(...)) w zależności od struktury, ale standardowo jest AppShell lub:
        // Jeśli nie masz AppShell, użyj: MainPage = new NavigationPage(new MainPage(null)); 
        // Ale skoro używamy DI w MauiProgram, to pewnie masz strukturę domyślną.
        // W najprostszym przypadku (bez Shella) w tym projekcie:
        // MainPage = new NavigationPage(new MainPage(null)); <- to by wymagało zmian w MainPage.
    }
}