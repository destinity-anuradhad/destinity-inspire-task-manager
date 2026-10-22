using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("RecurringTaskRuns")]
public class RecurringTaskRun
{
    public long Id { get; set; }
    public int RecurringTaskDefinitionId { get; set; }
    public DateTime ScheduledFor { get; set; }
    public int? TaskId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
