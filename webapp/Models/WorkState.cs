using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("WorkStates")]
public class WorkState
{
    public int    Id        { get; set; }
    public string Name      { get; set; } = "";
    public string Color     { get; set; } = "#094f70";
    public int    SortOrder { get; set; } = 0;
}
