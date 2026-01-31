using System.ComponentModel.DataAnnotations;

namespace DroneServer;

public class UserEntity
{
    [Key]
    public string Username { get; set; } = "";
    public string Password { get; set; } = ""; // W prawdziwej aplikacji to byłby Hash!
    public string Role { get; set; } = "User"; // "Admin" lub "User"
}