using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("SprintHoursLogs")]
public class SprintHoursLog
{
    public int      Id        { get; set; }
    public int      SprintId  { get; set; }
    public int      TaskId    { get; set; }
    public decimal  Hours     { get; set; }
    public string   LoggedBy  { get; set; } = "";
    public DateTime LoggedAt  { get; set; }
}
