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

    // --- UŻYTKOWNICY ---

    public override async Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        if (request.Username == "admin" && request.Password == "admin")
            return new LoginResponse { Success = true, Role = "admin", ClientId = "ADMIN", Message = "Witaj Adminie" };

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.Password == request.Password);

        if (user != null)
            return new LoginResponse { Success = true, Role = user.Role, ClientId = user.Username, Message = $"Witaj {user.Username}" };

        return new LoginResponse { Success = false, Message = "Błędny login lub hasło" };
    }

    public override async Task<RegisterUserResponse> RegisterUser(RegisterUserRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return new RegisterUserResponse { Success = false, Message = "Puste dane" };

        if (await _dbContext.Users.AnyAsync(u => u.Username == request.Username))
            return new RegisterUserResponse { Success = false, Message = "Użytkownik już istnieje" };

        _dbContext.Users.Add(new UserEntity { Username = request.Username, Password = request.Password, Role = "user" });
        await _dbContext.SaveChangesAsync();

        Console.WriteLine($"[SERWER] 👤 Utworzono użytkownika: {request.Username}");
        return new RegisterUserResponse { Success = true, Message = "Konto utworzone" };
    }

    // NOWE: Pobieranie wszystkich użytkowników
    public override async Task<UserList> GetAllUsers(Empty request, ServerCallContext context)
    {
        var users = await _dbContext.Users.ToListAsync();
        var response = new UserList();
        foreach (var u in users) response.Users.Add(new UserMsg { Id = u.Id, Username = u.Username, Role = u.Role });
        return response;
    }

    // NOWE: Usuwanie użytkownika
    public override async Task<ServerResponse> DeleteUser(UserRequest request, ServerCallContext context)
    {
        if (request.Username == "admin") 
            return new ServerResponse { Success = false, Message = "Nie można usunąć głównego administratora!" };

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        if (user != null)
        {
            // Usuwamy też paczki tego usera, żeby nie śmiecić w bazie
            var userOrders = _dbContext.Orders.Where(o => o.ClientId == user.Username);
            _dbContext.Orders.RemoveRange(userOrders);

            _dbContext.Users.Remove(user);
            await _dbContext.SaveChangesAsync();
            return new ServerResponse { Success = true, Message = "Usunięto użytkownika i jego paczki" };
        }
        return new ServerResponse { Success = false, Message = "Nie znaleziono" };
    }

    // --- PACZKI (ADMIN) ---

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

    // --- PACZKI (USER) ---

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
            Console.WriteLine($"[SERWER] 📦 Nowa paczka od {request.ClientId}");
        }
        return new ServerResponse { Success = true };
    }

    public override async Task<ServerResponse> UpdateOrder(DroneOrderMsg request, ServerCallContext context)
    {
        var entity = await _dbContext.Orders.FirstOrDefaultAsync(x => x.Id == request.Id);
        // User może aktualizować tylko paczki "W drodze"
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
    
    // --- Helpery ---
    private DroneOrderMsg MapToMsg(DroneEntity e) => new DroneOrderMsg { Id = e.Id, OriginAddress = e.OriginAddress, OriginLat = e.OriginLat, OriginLng = e.OriginLng, DestinationAddress = e.DestinationAddress, DestLat = e.DestLat, DestLng = e.DestLng, PackageWeightKg = e.PackageWeightKg, Status = e.Status, Progress = e.Progress, IsIncoming = e.IsIncoming, SendDate = e.SendDate, DeliveryDate = e.DeliveryDate, CurrentLat = e.CurrentLat, CurrentLng = e.CurrentLng, ClientId = e.ClientId };
    private DroneEntity MapToEntity(DroneOrderMsg m) => new DroneEntity { Id = m.Id, OriginAddress = m.OriginAddress, OriginLat = m.OriginLat, OriginLng = m.OriginLng, DestinationAddress = m.DestinationAddress, DestLat = m.DestLat, DestLng = m.DestLng, PackageWeightKg = m.PackageWeightKg, Status = m.Status, Progress = m.Progress, IsIncoming = m.IsIncoming, SendDate = m.SendDate, DeliveryDate = m.DeliveryDate, CurrentLat = m.CurrentLat, CurrentLng = m.CurrentLng, ClientId = m.ClientId };
    
    // Stare/Nieużywane
    public override async Task<ServerResponse> DeleteOrder(DeleteRequest request, ServerCallContext context) { var e = await _dbContext.Orders.FirstOrDefaultAsync(x=>x.Id==request.Id); if(e!=null){_dbContext.Orders.Remove(e);await _dbContext.SaveChangesAsync();return new ServerResponse{Success=true};} return new ServerResponse{Success=false};}
    public override Task<RegisterResponse> RegisterClient(ClientInfo request, ServerCallContext context) => Task.FromResult(new RegisterResponse { Success = true, ClientId = "Guest" });
}