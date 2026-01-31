namespace DroneDelivery;

public class ClientSession
{
    // Tutaj przechowujemy nasze ID (np. "Mac1")
    public string? ClientId { get; set; }
    
    // Sprawdza, czy jesteśmy już zarejestrowani
    public bool IsRegistered => !string.IsNullOrEmpty(ClientId);
}