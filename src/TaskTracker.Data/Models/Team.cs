using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("Teams")]
public class Team
{
    public int      Id          { get; set; }
    public string   Name        { get; set; } = "";
    public string?  Description { get; set; }
    public string   Color       { get; set; } = "#094f70";
    public bool     IsActive    { get; set; } = true;
    public DateTime CreatedAt   { get; set; } = DateTime.Now;
    public string?  CreatedBy   { get; set; }
}
