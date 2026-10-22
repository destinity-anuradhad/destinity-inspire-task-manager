using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("Modules")]
public class ModuleItem
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
}
