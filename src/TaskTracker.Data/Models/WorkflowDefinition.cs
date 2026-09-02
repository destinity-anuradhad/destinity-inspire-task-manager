using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>
/// An immutable, published workflow revision for one work-item type.  TeamId is
/// null for a global default; a team-specific revision takes precedence.
/// </summary>
[Table("WorkflowDefinitions")]
public class WorkflowDefinition
{
    public int Id { get; set; }
    public int? TeamId { get; set; }
    public string ItemType { get; set; } = "Task";
    public string Name { get; set; } = "Default workflow";
    public int Version { get; set; }
    public bool IsPublished { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
}
