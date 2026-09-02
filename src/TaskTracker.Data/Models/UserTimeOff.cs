using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("UserTimeOff")]
public class UserTimeOff
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}
