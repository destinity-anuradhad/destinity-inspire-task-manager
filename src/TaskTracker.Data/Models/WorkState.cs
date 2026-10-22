using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("WorkStates")]
public class WorkState
{
    public int     Id        { get; set; }
    public string  Name      { get; set; } = "";
    public string  Color     { get; set; } = "#094f70";
    public int     SortOrder { get; set; } = 0;
    public int?    WipLimit  { get; set; }
    // Comma-separated item types (e.g. "Task,Bug"). Null = applies to all types.
    public string? ItemTypes { get; set; }
}
