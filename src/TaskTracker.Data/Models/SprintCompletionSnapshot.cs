using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>Immutable completion facts used by historical sprint reporting.</summary>
[Table("SprintCompletionSnapshots")]
public class SprintCompletionSnapshot
{
    public int Id { get; set; }
    public int SprintId { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public string? CompletedBy { get; set; }
    public int CommittedCount { get; set; }
    public int CompletedCount { get; set; }
    public int CarriedForwardCount { get; set; }
    public decimal CommittedEstimate { get; set; }
    public decimal CompletedEstimate { get; set; }
}
