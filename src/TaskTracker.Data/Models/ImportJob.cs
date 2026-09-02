using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>
/// A team-scoped CSV import. Created in <c>Preview</c> state (parsed + validated, nothing written),
/// then transitioned to <c>Applied</c> when the valid rows are created through the governed task
/// command. Retained for auditability.
/// </summary>
[Table("ImportJobs")]
public class ImportJob
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    [MaxLength(260)] public string FileName { get; set; } = "";
    [MaxLength(20)]  public string Status { get; set; } = "Preview";   // Preview | Applied
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int ErrorRows { get; set; }
    public int CreatedRows { get; set; }
    public int SkippedRows { get; set; }
    public int FailedRows { get; set; }
    public int CreatedByUserId { get; set; }
    [MaxLength(200)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AppliedAt { get; set; }
}
