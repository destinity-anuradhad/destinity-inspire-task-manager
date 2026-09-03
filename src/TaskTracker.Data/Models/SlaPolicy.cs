using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>
/// A team-scoped service-level target: work matching the policy's scope (optionally an item type
/// and/or priority) is expected to reach a "done" state within <see cref="TargetHours"/> of being
/// created. Policies are evaluated read-only against existing task fields — they never mutate a task.
/// </summary>
[Table("SlaPolicies")]
public class SlaPolicy
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    public bool IsEnabled { get; set; } = true;

    // Scope — a null facet matches any value. The first enabled policy (by SortOrder) whose scope
    // matches a task governs that task.
    [MaxLength(50)] public string? AppliesToItemType { get; set; }
    [MaxLength(50)] public string? AppliesToPriority { get; set; }

    /// <summary>Hours from task creation within which the task should reach a done state.</summary>
    public int TargetHours { get; set; }

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)] public string? CreatedBy { get; set; }
}
