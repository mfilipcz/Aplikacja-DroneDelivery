using System.ComponentModel.DataAnnotations;

namespace DroneServer;

public class UserEntity
{
    [Key]
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = ""; // W prawdziwym projekcie tu byłby hash, dla szkoły wystarczy tekst
    public string Role { get; set; } = "user"; // "admin" lub "user"
}