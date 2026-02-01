using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DroneServer;

public class UserEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    
    public string Username { get; set; } = "";
    public string Password { get; set; } = ""; // W prawdziwej aplikacji to byłby Hash!
    public string Role { get; set; } = "User"; // "Admin" lub "User"
}