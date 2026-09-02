using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>
/// A team-scoped subscription that receives signed HTTP callbacks when subscribed domain events
/// occur. The <see cref="Secret"/> is the HMAC-SHA256 signing key shared with the receiver.
/// </summary>
[Table("WebhookSubscriptions")]
public class WebhookSubscription
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    [MaxLength(200)]  public string Name { get; set; } = "";
    [MaxLength(2048)] public string Url { get; set; } = "";
    [MaxLength(128)]  public string Secret { get; set; } = "";
    /// <summary>Space-separated event types this subscription wants, or <c>*</c> for all.</summary>
    [MaxLength(1000)] public string EventTypes { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int OwnerUserId { get; set; }
    [MaxLength(200)]  public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(30)]   public string? LastStatus { get; set; }
    public DateTime? LastDeliveryAt { get; set; }
}
