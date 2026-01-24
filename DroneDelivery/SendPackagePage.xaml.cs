using DroneServer;
using Grpc.Core;

namespace DroneDelivery;

public partial class SendPackagePage : ContentPage
{
    private readonly DroneService.DroneServiceClient _client;

    public SendPackagePage(DroneService.DroneServiceClient client)
    {
        InitializeComponent();
        _client = client;
        // Domyślnie dostawa na jutro
        DeliveryDate.Date = DateTime.Now.AddDays(1);
        UpdateCost();
    }

    private void OnSliderValueChanged(object sender, ValueChangedEventArgs e)
    {
        WeightLabel.Text = $"{e.NewValue:F1} kg";
        UpdateCost();
    }

    private void OnDateChanged(object sender, DateChangedEventArgs e)
    {
        UpdateCost();
    }

    private void UpdateCost()
    {
        TimeSpan diff = DeliveryDate.Date - SendDate.Date;
        int days = (int)diff.TotalDays;

        if (days < 0)
        {
            CostLabel.Text = "Błędna data!";
            return;
        }

        // Algorytm: Waga + Szybkość dostawy
        double weightCost = WeightSlider.Value * 10;
        double speedCost = 50.0 / (days + 1);
        double totalCost = 20 + weightCost + speedCost;

        CostLabel.Text = $"Koszt: {totalCost:F2} PLN";
    }

    private async void OnPayAndSendClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OriginEntry.Text) || string.IsNullOrWhiteSpace(DestEntry.Text))
        {
            await DisplayAlert("Błąd", "Wprowadź adresy!", "OK");
            return;
        }

        if (DeliveryDate.Date < SendDate.Date)
        {
             await DisplayAlert("Błąd", "Data dostawy nie może być wcześniejsza!", "OK");
             return;
        }

        try 
        {
            // Geocoding (zamiana adresu na współrzędne)
            var startLocations = await Geocoding.GetLocationsAsync(OriginEntry.Text);
            var startLoc = startLocations?.FirstOrDefault();

            var destLocations = await Geocoding.GetLocationsAsync(DestEntry.Text);
            var destLoc = destLocations?.FirstOrDefault();

            if (startLoc == null || destLoc == null)
            {
                await DisplayAlert("Błąd", "Nie znaleziono adresu.", "OK");
                return;
            }

            var order = new DroneOrderMsg
            {
                Id = Guid.NewGuid().ToString(),
                OriginAddress = OriginEntry.Text,
                DestinationAddress = DestEntry.Text,
                PackageWeightKg = WeightSlider.Value,
                Status = "W drodze",
                SendDate = SendDate.Date.ToString("O"),
                OriginLat = startLoc.Latitude,
                OriginLng = startLoc.Longitude,
                DestLat = destLoc.Latitude,
                DestLng = destLoc.Longitude,
                CurrentLat = startLoc.Latitude,
                CurrentLng = startLoc.Longitude,
                IsIncoming = false
            };

            var response = await _client.AddOrderAsync(order);
            
            if (response.Success)
            {
                await DisplayAlert("Sukces", "Paczka nadana!", "OK");
                
                // POPRAWKA: Przekierowanie prosto do mapy zamiast cofania
                await Navigation.PushAsync(new TrackingPage(_client));
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Błąd", $"Coś poszło nie tak: {ex.Message}", "OK");
        }
    }
}