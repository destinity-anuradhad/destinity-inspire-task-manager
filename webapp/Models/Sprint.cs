using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("Sprints")]
public class Sprint
{
    public int     Id        { get; set; }
    public string  Name      { get; set; } = "";
    public string? StartDate { get; set; }
    public string? EndDate   { get; set; }
    public string? Goal      { get; set; }
    public bool    IsActive  { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? CreatedBy  { get; set; }
}
