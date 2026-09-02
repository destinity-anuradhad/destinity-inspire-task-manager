using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>
/// One parsed CSV line within an <see cref="ImportJob"/>. <see cref="Payload"/> holds the parsed
/// field values as JSON so the apply step never re-parses the raw file. <see cref="ExternalKey"/>
/// (optional) drives idempotency — a key already created for the team is skipped on re-import.
/// </summary>
[Table("ImportRows")]
public class ImportRow
{
    public long Id { get; set; }
    public int ImportJobId { get; set; }
    public int RowNumber { get; set; }
    public string Payload { get; set; } = "";
    [MaxLength(200)] public string? ExternalKey { get; set; }
    /// <summary>Valid | Invalid (preview) → Created | Skipped | Failed (apply).</summary>
    [MaxLength(20)]  public string Status { get; set; } = "Valid";
    [MaxLength(500)] public string? Message { get; set; }
    public int? CreatedTaskId { get; set; }
}
