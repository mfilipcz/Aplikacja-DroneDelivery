using Grpc.Core;
using DroneServer;
using Microsoft.EntityFrameworkCore;

namespace DroneServer.Services;

public class DroneApiService : DroneService.DroneServiceBase
{
    private readonly DroneDbContext _dbContext;

    public DroneApiService(DroneDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // --- REJESTRACJA UŻYTKOWNIKA (NOWOŚĆ) ---
    public override async Task<RegisterUserResponse> RegisterUser(RegisterUserRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return new RegisterUserResponse { Success = false, Message = "Puste dane" };

        // Sprawdź czy taki login już istnieje
        var existing = await _dbContext.Users.AnyAsync(u => u.Username == request.Username);
        if (existing)
            return new RegisterUserResponse { Success = false, Message = "Użytkownik już istnieje" };

        // Dodaj do bazy
        var newUser = new UserEntity
        {
            Username = request.Username,
            Password = request.Password,
            Role = "user" // Domyślnie każdy jest zwykłym userem
        };

        _dbContext.Users.Add(newUser);
        await _dbContext.SaveChangesAsync();

        Console.WriteLine($"[SERWER] 👤 Zarejestrowano nowego użytkownika: {request.Username}");
        return new RegisterUserResponse { Success = true, Message = "Konto utworzone!" };
    }

    // --- LOGOWANIE (Zmienione na bazę danych) ---
    public override async Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        // 1. Backdoor dla Admina (zawsze działa, nawet bez bazy)
        if (request.Username == "admin" && request.Password == "admin")
        {
            return new LoginResponse { Success = true, Role = "admin", ClientId = "ADMIN", Message = "Witaj Adminie" };
        }

        // 2. Szukamy w bazie danych
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username && u.Password == request.Password);

        if (user != null)
        {
            // Używamy nazwy użytkownika jako ClientId -> Paczki są przypisane do loginu!
            return new LoginResponse 
            { 
                Success = true, 
                Role = user.Role, 
                ClientId = user.Username, // TO KLUCZOWE! 
                Message = $"Witaj {user.Username}" 
            };
        }

        return new LoginResponse { Success = false, Message = "Błędny login lub hasło" };
    }

    // --- Reszta metod bez zmian logicznych, ale muszą tu być ---
    
    public override async Task<OrderList> GetAllOrders(Empty request, ServerCallContext context)
    {
        var entities = await _dbContext.Orders.ToListAsync();
        var response = new OrderList();
        foreach (var e in entities) response.Orders.Add(MapToMsg(e));
        return response;
    }

    public override async Task<ServerResponse> UpdateOrderStatus(StatusRequest request, ServerCallContext context)
    {
        if (!request.IsAdmin) return new ServerResponse { Success = false, Message = "Brak uprawnień" };
        var entity = await _dbContext.Orders.FirstOrDefaultAsync(x => x.Id == request.OrderId);
        if (entity != null)
        {
            entity.Status = request.NewStatus;
            await _dbContext.SaveChangesAsync();
            return new ServerResponse { Success = true };
        }
        return new ServerResponse { Success = false };
    }

    public override async Task<OrderList> GetOrders(ClientRequest request, ServerCallContext context)
    {
        var entities = await _dbContext.Orders.Where(o => o.ClientId == request.ClientId).ToListAsync();
        var response = new OrderList();
        foreach (var e in entities) response.Orders.Add(MapToMsg(e));
        return response;
    }

    public override async Task<ServerResponse> AddOrder(DroneOrderMsg request, ServerCallContext context)
    {
        if (!await _dbContext.Orders.AnyAsync(x => x.Id == request.Id))
        {
            request.Status = "Oczekuje na zatwierdzenie";
            request.Progress = 0;
            _dbContext.Orders.Add(MapToEntity(request));
            await _dbContext.SaveChangesAsync();
            Console.WriteLine($"[SERWER] 📦 {request.ClientId} nadał paczkę (czeka na admina)");
        }
        return new ServerResponse { Success = true };
    }

    public override async Task<ServerResponse> UpdateOrder(DroneOrderMsg request, ServerCallContext context)
    {
        var entity = await _dbContext.Orders.FirstOrDefaultAsync(x => x.Id == request.Id);
        if (entity != null && entity.Status == "W drodze")
        {
            entity.CurrentLat = request.CurrentLat;
            entity.CurrentLng = request.CurrentLng;
            entity.Status = request.Status;
            entity.Progress = request.Progress;
            await _dbContext.SaveChangesAsync();
        }
        return new ServerResponse { Success = true };
    }

    // Mapery i DeleteOrder
    private DroneOrderMsg MapToMsg(DroneEntity e) => new DroneOrderMsg {
        Id = e.Id, OriginAddress = e.OriginAddress, OriginLat = e.OriginLat, OriginLng = e.OriginLng,
        DestinationAddress = e.DestinationAddress, DestLat = e.DestLat, DestLng = e.DestLng,
        PackageWeightKg = e.PackageWeightKg, Status = e.Status, Progress = e.Progress,
        IsIncoming = e.IsIncoming, SendDate = e.SendDate, DeliveryDate = e.DeliveryDate,
        CurrentLat = e.CurrentLat, CurrentLng = e.CurrentLng, ClientId = e.ClientId
    };
    private DroneEntity MapToEntity(DroneOrderMsg m) => new DroneEntity {
        Id = m.Id, OriginAddress = m.OriginAddress, OriginLat = m.OriginLat, OriginLng = m.OriginLng,
        DestinationAddress = m.DestinationAddress, DestLat = m.DestLat, DestLng = m.DestLng,
        PackageWeightKg = m.PackageWeightKg, Status = m.Status, Progress = m.Progress,
        IsIncoming = m.IsIncoming, SendDate = m.SendDate, DeliveryDate = m.DeliveryDate,
        CurrentLat = m.CurrentLat, CurrentLng = m.CurrentLng, ClientId = m.ClientId
    };
    public override async Task<ServerResponse> DeleteOrder(DeleteRequest request, ServerCallContext context) {
         var entity = await _dbContext.Orders.FirstOrDefaultAsync(x => x.Id == request.Id);
         if(entity!=null){_dbContext.Orders.Remove(entity); await _dbContext.SaveChangesAsync(); return new ServerResponse{Success=true};}
         return new ServerResponse{Success=false};
    }
    public override Task<RegisterResponse> RegisterClient(ClientInfo request, ServerCallContext context) => Task.FromResult(new RegisterResponse { Success = true, ClientId = "Guest" });
}