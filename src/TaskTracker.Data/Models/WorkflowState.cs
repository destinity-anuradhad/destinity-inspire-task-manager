using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("WorkflowStates")]
public class WorkflowState
{
    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    // Stable identifier used by transitions; Name remains the task-facing label.
    public string Key { get; set; } = "new";
    public string Name { get; set; } = "New";
    public string Category { get; set; } = "NotStarted";
    public int SortOrder { get; set; }
    public bool IsInitial { get; set; }
    public bool IsTerminal { get; set; }
    public bool RequiresOwner { get; set; }
    public bool RequiresActualHours { get; set; }
}
