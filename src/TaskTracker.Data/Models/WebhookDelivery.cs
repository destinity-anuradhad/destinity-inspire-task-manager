using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>
/// A durable, at-least-once outbox row for a single webhook callback. Mirrors the email outbox:
/// a background sender claims due rows, POSTs the signed <see cref="Payload"/>, and either marks
/// them delivered or reschedules with exponential backoff until the dead-letter threshold.
/// </summary>
[Table("WebhookDeliveries")]
public class WebhookDelivery
{
    public long Id { get; set; }
    public int SubscriptionId { get; set; }
    /// <summary>Stable per-event id echoed to the receiver for idempotency/replay.</summary>
    [MaxLength(64)]  public string EventId { get; set; } = "";
    [MaxLength(100)] public string EventType { get; set; } = "";
    /// <summary>The exact JSON body that is signed and sent (so replays are byte-identical).</summary>
    public string Payload { get; set; } = "";
    [MaxLength(20)]  public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessingAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public int? ResponseStatusCode { get; set; }
    [MaxLength(2000)] public string? LastError { get; set; }
}
