using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("RecurringTaskDefinitions")]
public class RecurringTaskDefinition
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public int TaskTemplateId { get; set; }
    public string Goal { get; set; } = "";
    public int? OwnerUserId { get; set; }
    public string Frequency { get; set; } = "Weekly";
    public DateTime NextRunAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}
