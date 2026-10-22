using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>Append-only record of a work item's sprint membership lifecycle.</summary>
[Table("SprintWorkItemEvents")]
public class SprintWorkItemEvent
{
    public long Id { get; set; }
    public int TaskId { get; set; }
    public int? FromSprintId { get; set; }
    public int? ToSprintId { get; set; }
    public string EventType { get; set; } = "Assigned";
    public string? Status { get; set; }
    public decimal? Estimate { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string? OccurredBy { get; set; }
}
