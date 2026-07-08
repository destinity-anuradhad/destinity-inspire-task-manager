using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("SprintCapacities")]
public class SprintCapacity
{
    public int     Id            { get; set; }
    public int     SprintId      { get; set; }
    public string  OwnerName     { get; set; } = "";
    public decimal CapacityHours { get; set; }
}
