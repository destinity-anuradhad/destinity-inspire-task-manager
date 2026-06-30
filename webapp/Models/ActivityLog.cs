using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("ActivityLog")]
public class ActivityLog
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string TaskGoal { get; set; } = "";
    public string Project { get; set; } = "";
    public string ChangedBy { get; set; } = "";
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime ChangedAt { get; set; } = DateTime.Now;
}
