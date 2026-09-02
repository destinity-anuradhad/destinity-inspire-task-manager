using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("TaskTemplates")]
public class TaskTemplate
{
    public int Id { get; set; }
    public int? TeamId { get; set; }
    public string Name { get; set; } = "";
    public string ItemType { get; set; } = "Task";
    public string? Priority { get; set; }
    public decimal? Estimate { get; set; }
    public string EstimateUnit { get; set; } = "Points";
    public string? Notes { get; set; }
    public string? Labels { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}

[Table("TaskTemplateChecklistItems")]
public class TaskTemplateChecklistItem
{
    public int Id { get; set; }
    public int TaskTemplateId { get; set; }
    public string Text { get; set; } = "";
    public int SortOrder { get; set; }
    public TaskTemplate? TaskTemplate { get; set; }
}
