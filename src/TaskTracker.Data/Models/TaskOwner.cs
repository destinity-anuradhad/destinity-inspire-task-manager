using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("TaskOwners")]
public class TaskOwner
{
    public int TaskId { get; set; }
    public int UserId { get; set; }
}
