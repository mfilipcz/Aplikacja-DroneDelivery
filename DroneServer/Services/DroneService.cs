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
        SeedAdmin();
    }

    private bool IsSuspicious(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        // Sprawdź typowe znaki używane w SQL Injection
        string[] suspicious = { "'", ";", "--", "/*", "*/", " OR ", " AND " };
        foreach (var s in suspicious)
        {
            if (input.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private void SeedAdmin()
    {
        // Proste seedowanie przy każdym uruchomieniu serwisu (w produkcji robi się to inaczej)
        if (!_dbContext.Users.Any(u => u.Username == "admin"))
        {
            _dbContext.Users.Add(new UserEntity { Username = "admin", Password = "admin", Role = "Admin" });
            _dbContext.SaveChanges();
            Console.WriteLine("[SERWER] Utworzono konto admina (admin:admin)");
        }
    }

    // --- AUTH ---

    public override async Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        if (IsSuspicious(request.Username) || IsSuspicious(request.Password))
        {
            return new LoginResponse { Success = false, Message = "Wykryto niedozwolone znaki." };
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.Password == request.Password);
        if (user != null)
        {
            Console.WriteLine($"[SERWER] Zalogowano: {user.Username} ({user.Role})");
            return new LoginResponse 
            { 
                Success = true, 
                Message = "Zalogowano", 
                Username = user.Username, 
                Role = user.Role 
            };
        }
        return new LoginResponse { Success = false, Message = "Błędne dane logowania" };
    }

    public override async Task<RegisterResponse> RegisterUser(RegisterRequest request, ServerCallContext context)
    {
        if (IsSuspicious(request.Username) || IsSuspicious(request.Password))
        {
             return new RegisterResponse { Success = false, Message = "Wykryto niedozwolone znaki." };
        }

        if (await _dbContext.Users.AnyAsync(u => u.Username == request.Username))
        {
            return new RegisterResponse { Success = false, Message = "Użytkownik już istnieje" };
        }

        var newUser = new UserEntity 
        { 
            Username = request.Username, 
            Password = request.Password, 
            Role = "User" // Domyślnie User
        };

        _dbContext.Users.Add(newUser);
        await _dbContext.SaveChangesAsync();
        
        Console.WriteLine($"[SERWER] Zarejestrowano nowego użytkownika: {request.Username}");
        return new RegisterResponse { Success = true, Message = "Konto utworzone" };
    }

    public override async Task<UserList> GetAllUsers(Empty request, ServerCallContext context)
    {
        // Tutaj powinniśmy sprawdzać czy dzwoniący to Admin, ale upraszczamy
        var users = await _dbContext.Users.ToListAsync();
        var response = new UserList();
        foreach (var u in users)
        {
            response.Users.Add(new UserMsg { Username = u.Username, Role = u.Role });
        }
        return response;
    }

    public override async Task<ServerResponse> DeleteUser(UserRequest request, ServerCallContext context)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        if (user != null)
        {
            if (user.Username == "admin") 
                return new ServerResponse { Success = false, Message = "Nie można usunąć głównego admina" };

            _dbContext.Users.Remove(user);
            
            // Opcjonalnie: usuń też paczki tego usera?
            var orders = _dbContext.Orders.Where(o => o.ClientId == request.Username);
            _dbContext.Orders.RemoveRange(orders);
            
            await _dbContext.SaveChangesAsync();
            return new ServerResponse { Success = true, Message = "Usunięto użytkownika" };
        }
        return new ServerResponse { Success = false, Message = "Nie znaleziono użytkownika" };
    }

    // --- ORDERS ---

    public override async Task<OrderList> GetOrders(ClientRequest request, ServerCallContext context)
    {
        List<DroneEntity> entities;

        if (request.Role == "Admin")
        {
            // Admin widzi wszystko
            entities = await _dbContext.Orders.ToListAsync();
        }
        else
        {
            // User widzi tylko swoje
            entities = await _dbContext.Orders
                .Where(o => o.ClientId == request.ClientId) // ClientId to teraz Username
                .ToListAsync();
        }

        var response = new OrderList();
        foreach (var e in entities)
        {
            response.Orders.Add(MapToMsg(e));
        }
        return response;
    }

    public override async Task<ServerResponse> AddOrder(DroneOrderMsg request, ServerCallContext context)
    {
        // Sprawdź duplikaty
        if (await _dbContext.Orders.AnyAsync(x => x.Id == request.Id))
             return new ServerResponse { Success = true, Message = "Już istnieje" };

        var entity = new DroneEntity
        {
            Id = request.Id,
            OriginAddress = request.OriginAddress,
            OriginLat = request.OriginLat,
            OriginLng = request.OriginLng,
            DestinationAddress = request.DestinationAddress,
            DestLat = request.DestLat,
            DestLng = request.DestLng,
            PackageWeightKg = request.PackageWeightKg,
            
            // WAŻNE: Domyślny status dla Usera to "Oczekuje na zatwierdzenie"
            // Chyba że status przyszedł już jako "W drodze" (np. od Admina)
            Status = string.IsNullOrEmpty(request.Status) ? "Oczekuje na zatwierdzenie" : request.Status,
            
            Progress = request.Progress,
            IsIncoming = request.IsIncoming,
            SendDate = request.SendDate,
            DeliveryDate = request.DeliveryDate,
            CurrentLat = request.CurrentLat,
            CurrentLng = request.CurrentLng,
            ClientId = request.ClientId // Username
        };

        _dbContext.Orders.Add(entity);
        await _dbContext.SaveChangesAsync();
        
        Console.WriteLine($"[SERWER] Dodano paczkę: {request.Id} (User: {request.ClientId}, Status: {entity.Status})");
        return new ServerResponse { Success = true, Message = "OK" };
    }

    public override async Task<ServerResponse> UpdateOrder(DroneOrderMsg request, ServerCallContext context)
    {
        var entity = await _dbContext.Orders.FirstOrDefaultAsync(x => x.Id == request.Id);
        
        if (entity != null)
        {
            entity.CurrentLat = request.CurrentLat;
            entity.CurrentLng = request.CurrentLng;
            entity.Status = request.Status;
            entity.Progress = request.Progress;
            
            // Jeśli paczka została "wysłana" przez admina (zmieniono status na W drodze),
            // to tutaj się to zapisze.

            await _dbContext.SaveChangesAsync();
        }
        return new ServerResponse { Success = true };
    }

    public override async Task<ServerResponse> DeleteOrder(DeleteRequest request, ServerCallContext context)
    {
        // Admin może usuwać wszystko, User tylko swoje (sprawdzamy to?)
        // Na razie prosta logika:
        var entity = await _dbContext.Orders.FirstOrDefaultAsync(x => x.Id == request.Id);
        if (entity != null)
        {
            // Dodatkowe zabezpieczenie: jeśli request.ClientId jest podane i nie jest Adminem, sprawdź własność
            // Ale dla uproszczenia pomijamy tu walidację roli w Delete
            _dbContext.Orders.Remove(entity);
            await _dbContext.SaveChangesAsync();
            return new ServerResponse { Success = true, Message = "Usunięto" };
        }
        return new ServerResponse { Success = false, Message = "Nie znaleziono" };
    }

    private DroneOrderMsg MapToMsg(DroneEntity e)
    {
        return new DroneOrderMsg
        {
            Id = e.Id,
            OriginAddress = e.OriginAddress,
            OriginLat = e.OriginLat,
            OriginLng = e.OriginLng,
            DestinationAddress = e.DestinationAddress,
            DestLat = e.DestLat,
            DestLng = e.DestLng,
            PackageWeightKg = e.PackageWeightKg,
            Status = e.Status,
            Progress = e.Progress,
            IsIncoming = e.IsIncoming,
            SendDate = e.SendDate,
            DeliveryDate = e.DeliveryDate,
            CurrentLat = e.CurrentLat,
            CurrentLng = e.CurrentLng,
            ClientId = e.ClientId
        };
    }
}
