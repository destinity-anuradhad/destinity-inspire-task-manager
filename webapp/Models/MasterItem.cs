using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("Projects")]
public class ProjectItem
{
    public int    Id    { get; set; }
    public string Name  { get; set; } = "";
    public string? Color { get; set; }
}

[Table("Properties")]
public class PropertyItem
{
    public int    Id    { get; set; }
    public string Name  { get; set; } = "";
    public string? Color { get; set; }
}

[Table("Owners")]
public class OwnerItem
{
    public int    Id    { get; set; }
    public string Name  { get; set; } = "";

    [NotMapped]
    public string Color { get; set; } = "#094f70";
}
