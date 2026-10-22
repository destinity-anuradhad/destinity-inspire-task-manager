using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>Append-only work log. Corrections are new rows, never edits in place.</summary>
[Table("TimeEntries")]
public class TimeEntry
{
    public long Id { get; set; }
    public int TaskId { get; set; }
    public int? SprintId { get; set; }
    public int? UserId { get; set; }
    public decimal Hours { get; set; }
    public DateTime WorkDate { get; set; }
    public string EntryType { get; set; } = "Logged";
    public string? Note { get; set; }
    public long? ReversesEntryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}
