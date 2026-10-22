using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>
/// Caches the outcome of a completed API write so that a retried request carrying the same
/// <c>Idempotency-Key</c> (scoped to the calling API client) replays the original response instead
/// of performing the action again.
/// </summary>
[Table("ApiIdempotencyRecords")]
public class ApiIdempotencyRecord
{
    public long Id { get; set; }
    public int ApiClientId { get; set; }
    [MaxLength(200)] public string IdempotencyKey { get; set; } = "";
    [MaxLength(10)]  public string Method { get; set; } = "";
    [MaxLength(500)] public string Path { get; set; } = "";
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
