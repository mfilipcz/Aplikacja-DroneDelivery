using DroneServer;
using Grpc.Core;

namespace DroneDelivery;

public partial class SendPackagePage : ContentPage
{
    private readonly DroneService.DroneServiceClient _client;
    // Potrzebujemy sesji, żeby wiedzieć, jakie jest nasze ID
    private readonly ClientSession _session;

    // Konstruktor przyjmuje teraz także sesję
    public SendPackagePage(DroneService.DroneServiceClient client, ClientSession session)
    {
        InitializeComponent();
        _client = client;
        _session = session;
        
        // Domyślne ustawienia daty
        DeliveryDate.Date = DateTime.Now.AddDays(1);
        UpdateCost();
    }

    private void OnSliderValueChanged(object sender, ValueChangedEventArgs e)
    {
        if (WeightLabel != null)
            WeightLabel.Text = $"{e.NewValue:F1} kg";
            
        UpdateCost();
    }

    private void OnDateChanged(object sender, DateChangedEventArgs e)
    {
        UpdateCost();
    }

    private void UpdateCost()
    {
        if (CostLabel == null) return;

        TimeSpan diff = DeliveryDate.Date - SendDate.Date;
        int days = (int)diff.TotalDays;

        if (days < 0)
        {
            CostLabel.Text = "Błędna data!";
            return;
        }

        // Prosty algorytm kosztów: Waga * 10 + Szybkość
        double weightCost = WeightSlider.Value * 10;
        double speedCost = 50.0 / (days + 1);
        double totalCost = 20 + weightCost + speedCost;

        CostLabel.Text = $"Koszt: {totalCost:F2} PLN";
    }

    private async void OnPayAndSendClicked(object sender, EventArgs e)
    {
        // --- 1. MECHANIZM RATUNKOWY (AUTO-REJESTRACJA) ---
        // Jeśli z jakiegoś powodu nie mamy ID (serwis w tle nie zdążył),
        // próbujemy pobrać je teraz, zamiast blokować użytkownika.
        if (!_session.IsRegistered)
        {
            try
            {
                var regResponse = await _client.RegisterClientAsync(new ClientInfo { Platform = "Mac" });
                if (regResponse.Success)
                {
                    _session.ClientId = regResponse.ClientId;
                    Console.WriteLine($"[SendPage] Uratowano sesję. Nowe ID: {_session.ClientId}");
                }
                else
                {
                    await DisplayAlert("Błąd", "Serwer odrzucił rejestrację.", "OK");
                    return;
                }
            }
            catch (Exception ex)
            {
                // To wyłapie błąd, jeśli serwer w ogóle nie odpowiada (zły port/IP)
                await DisplayAlert("Błąd połączenia", 
                    $"Nie można połączyć z serwerem.\nUpewnij się, że serwer działa na porcie 5011.\nBłąd: {ex.Message}", 
                    "OK");
                return;
            }
        }

        // --- 2. WALIDACJA FORMULARZA ---
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

        // --- 3. WYSYŁKA ---
        try 
        {
            // Geocoding - zamiana adresu na współrzędne
            var startLocations = await Geocoding.GetLocationsAsync(OriginEntry.Text);
            var startLoc = startLocations?.FirstOrDefault();

            var destLocations = await Geocoding.GetLocationsAsync(DestEntry.Text);
            var destLoc = destLocations?.FirstOrDefault();

            if (startLoc == null || destLoc == null)
            {
                await DisplayAlert("Błąd", "Nie znaleziono podanego adresu na mapie.", "OK");
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
                IsIncoming = false,
                
                // WAŻNE: Przypisujemy ID klienta, żeby widzieć tę paczkę na mapie
                ClientId = _session.ClientId 
            };

            var response = await _client.AddOrderAsync(order);
            
            if (response.Success)
            {
                await DisplayAlert("Sukces", "Paczka nadana!", "OK");
                
                // Przechodzimy do mapy, przekazując klienta i sesję
                await Navigation.PushAsync(new TrackingPage(_client, _session));
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Błąd", $"Wystąpił błąd podczas wysyłania: {ex.Message}", "OK");
        }
    }
}