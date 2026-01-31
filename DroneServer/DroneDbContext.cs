using Microsoft.EntityFrameworkCore;

namespace DroneServer;

public class DroneDbContext : DbContext
{
    public DbSet<DroneEntity> Orders { get; set; }
    public DbSet<UserEntity> Users { get; set; } // <--- NOWOŚĆ

    public DroneDbContext(DbContextOptions<DroneDbContext> options) : base(options) { }
}