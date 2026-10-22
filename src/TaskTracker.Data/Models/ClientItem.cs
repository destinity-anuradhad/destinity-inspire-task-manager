using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("Clients")]
public class ClientItem
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
}
