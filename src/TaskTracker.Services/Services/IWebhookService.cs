namespace TaskTracker.Services;

/// <summary>
/// Manages webhook subscriptions and the durable delivery outbox. Publishing is best-effort and
/// must never fail the originating command; delivery is retried by a background sender.
/// </summary>
public interface IWebhookService
{
    // ── Subscriptions (management) ──────────────────────────────────────────
    Task<IReadOnlyList<WebhookSubscriptionSummary>> ListAsync(int? teamId, bool includeGlobal, CancellationToken ct = default);
    Task<WebhookCreateResult> CreateAsync(WebhookCreateRequest request, CancellationToken ct = default);
    Task<bool> SetActiveAsync(int id, bool active, int? teamId, bool isSysAdmin, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, int? teamId, bool isSysAdmin, CancellationToken ct = default);

    // ── Publishing (called from domain commands) ────────────────────────────
    Task PublishAsync(int? teamId, string eventType, object data, string? actor, CancellationToken ct = default);

    // ── Deliveries (observability + replay) ─────────────────────────────────
    Task<IReadOnlyList<WebhookDeliverySummary>> GetRecentDeliveriesAsync(int? teamId, bool includeGlobal, int maxCount = 100, CancellationToken ct = default);
    Task<bool> ReplayAsync(long deliveryId, int? teamId, bool isSysAdmin, CancellationToken ct = default);

    // ── Delivery outbox (used by the background sender) ─────────────────────
    Task<WebhookDispatch?> ClaimNextAsync(CancellationToken ct = default);
    Task MarkDeliveredAsync(long id, int statusCode, CancellationToken ct = default);
    Task MarkFailedAsync(long id, int? statusCode, string? error, CancellationToken ct = default);
}

public sealed record WebhookCreateRequest(
    string Name, string Url, int TeamId, int OwnerUserId, IEnumerable<string> EventTypes, string? CreatedBy);

public sealed record WebhookCreateResult(WebhookSubscriptionSummary Subscription, string Secret);

public sealed record WebhookSubscriptionSummary(
    int Id, string Name, string Url, string EventTypes, bool IsActive,
    int TeamId, string? TeamName, string? CreatedBy, DateTime CreatedAt,
    string? LastStatus, DateTime? LastDeliveryAt);

public sealed record WebhookDeliverySummary(
    long Id, int SubscriptionId, string SubscriptionName, string EventType, string EventId,
    string Status, int AttemptCount, int? ResponseStatusCode, string? LastError,
    DateTime CreatedAt, DateTime? DeliveredAt);

/// <summary>Everything the background sender needs to POST one delivery.</summary>
public sealed record WebhookDispatch(
    long DeliveryId, int SubscriptionId, string Url, string Secret,
    string EventType, string EventId, string Payload);
