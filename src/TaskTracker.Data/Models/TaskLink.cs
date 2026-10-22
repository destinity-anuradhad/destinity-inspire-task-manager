using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("TaskLinks")]
public class TaskLink
{
    public int     Id         { get; set; }
    public int     FromTaskId { get; set; }
    public int     ToTaskId   { get; set; }
    public string  LinkType   { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? CreatedBy  { get; set; }
}
