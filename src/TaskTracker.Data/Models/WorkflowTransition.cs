using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("WorkflowTransitions")]
public class WorkflowTransition
{
    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public int FromStateId { get; set; }
    public int ToStateId { get; set; }
    public bool MemberCanTransition { get; set; } = true;
}
