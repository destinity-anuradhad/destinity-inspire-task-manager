using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("AppUsers")]
public class AppUser
{
    public int    Id               { get; set; }
    public string Username         { get; set; } = "";
    public string PasswordHash     { get; set; } = "";
    public string Role             { get; set; } = "Member";
    public string DisplayName      { get; set; } = "";
    public string? ProfileImagePath { get; set; }
    public bool   NotifyOnAssigned { get; set; } = true;
    public DateTime CreatedAt      { get; set; } = DateTime.Now;
    public string? Color           { get; set; }
}
