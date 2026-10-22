using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("TaskChecklistItems")]
public class TaskChecklistItem
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string Text { get; set; } = "";
    public bool IsCompleted { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
}
