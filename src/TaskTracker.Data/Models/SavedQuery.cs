using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("SavedQueries")]
public class SavedQuery
{
    public int      Id          { get; set; }
    public int?     TeamId      { get; set; }
    public string   Name        { get; set; } = "";
    public string?  Description { get; set; }
    public string   Filters     { get; set; } = "{}";  // JSON: search/project/owner/status/priority/etc.
    public bool     IsShared    { get; set; }
    public string?  CreatedBy   { get; set; }
    public DateTime CreatedAt   { get; set; } = DateTime.Now;
}
