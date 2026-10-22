using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("TaskWatchers")]
public class TaskWatcher
{
    public int      Id        { get; set; }
    public int      TaskId    { get; set; }
    public int      UserId    { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
