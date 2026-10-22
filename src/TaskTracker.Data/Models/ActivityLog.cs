using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("ActivityLog")]
public class ActivityLog
{
    public int      Id          { get; set; }
    public int?     TaskId      { get; set; }           // null for auth / system events
    public int?     TeamId      { get; set; }           // direct team association
    public string   TaskGoal    { get; set; } = "";
    public string   Project     { get; set; } = "";
    public string   ChangedBy   { get; set; } = "";
    public string   Action      { get; set; } = "";
    public string   Category    { get; set; } = "Task"; // Task | Auth | System
    public string   Details     { get; set; } = "";
    public string?  IpAddress   { get; set; }
    public string?  UserAgent   { get; set; }
    public DateTime ChangedAt   { get; set; } = DateTime.Now;
}
