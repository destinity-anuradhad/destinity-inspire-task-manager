using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("ProjectReleases")]
public class ProjectRelease
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "Planned";
    public DateTime? TargetDate { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}
