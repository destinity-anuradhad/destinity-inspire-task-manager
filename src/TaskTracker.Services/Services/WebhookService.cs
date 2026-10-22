using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public sealed class WebhookService(AppDbContext db, ILogger<WebhookService> logger) : IWebhookService
{
    private const int MaxAttempts = 6;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // ── Subscriptions ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<WebhookSubscriptionSummary>> ListAsync(int? teamId, bool includeGlobal, CancellationToken ct = default)
    {
        var query = db.WebhookSubscriptions.AsNoTracking();
        if (!includeGlobal)
        {
            if (teamId is null) return [];
            query = query.Where(s => s.TeamId == teamId);
        }

        var subs = await query.OrderByDescending(s => s.IsActive).ThenByDescending(s => s.Id).ToListAsync(ct);
        var teamNames = await ResolveTeamNamesAsync(subs.Select(s => s.TeamId), ct);

        return subs.Select(s => new WebhookSubscriptionSummary(
            s.Id, s.Name, s.Url, s.EventTypes, s.IsActive, s.TeamId, teamNames.GetValueOrDefault(s.TeamId),
            s.CreatedBy, s.CreatedAt, s.LastStatus, s.LastDeliveryAt)).ToList();
    }

    public async Task<WebhookCreateResult> CreateAsync(WebhookCreateRequest request, CancellationToken ct = default)
    {
        var name = (request.Name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("A subscription name is required.", nameof(request));

        var url = (request.Url ?? "").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("A valid absolute http(s) URL is required.", nameof(request));

        var events = WebhookEvents.Normalize(request.EventTypes ?? []);
        if (events.Length == 0) throw new ArgumentException("Select at least one event type.", nameof(request));

        var secret = "whsec_" + RandomToken(32);
        var sub = new WebhookSubscription
        {
            Name = name, Url = url, Secret = secret, EventTypes = events,
            TeamId = request.TeamId, OwnerUserId = request.OwnerUserId,
            CreatedBy = request.CreatedBy, CreatedAt = DateTime.UtcNow, IsActive = true,
        };
        db.WebhookSubscriptions.Add(sub);
        await db.SaveChangesAsync(ct);

        var teamNames = await ResolveTeamNamesAsync([sub.TeamId], ct);
        var summary = new WebhookSubscriptionSummary(
            sub.Id, sub.Name, sub.Url, sub.EventTypes, sub.IsActive, sub.TeamId,
            teamNames.GetValueOrDefault(sub.TeamId), sub.CreatedBy, sub.CreatedAt, null, null);
        return new WebhookCreateResult(summary, secret);
    }

    public async Task<bool> SetActiveAsync(int id, bool active, int? teamId, bool isSysAdmin, CancellationToken ct = default)
    {
        var sub = await FindOwnedAsync(id, teamId, isSysAdmin, ct);
        if (sub == null) return false;
        sub.IsActive = active;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, int? teamId, bool isSysAdmin, CancellationToken ct = default)
    {
        var sub = await FindOwnedAsync(id, teamId, isSysAdmin, ct);
        if (sub == null) return false;
        db.WebhookSubscriptions.Remove(sub);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Publishing ──────────────────────────────────────────────────────────

    public async Task PublishAsync(int? teamId, string eventType, object data, string? actor, CancellationToken ct = default)
    {
        if (teamId is null) return;   // only team-scoped events fan out to subscriptions
        try
        {
            var subs = await db.WebhookSubscriptions.AsNoTracking()
                .Where(s => s.TeamId == teamId && s.IsActive)
                .ToListAsync(ct);

            var targets = subs.Where(s => WebhookEvents.Matches(s.EventTypes, eventType)).ToList();
            if (targets.Count == 0) return;

            var createdAt = DateTime.UtcNow;
            foreach (var sub in targets)
            {
                var eventId = Guid.NewGuid().ToString("N");
                var envelope = new
                {
                    id = eventId,
                    type = eventType,
                    createdAt,
                    teamId,
                    actor,
                    data
                };
                db.WebhookDeliveries.Add(new WebhookDelivery
                {
                    SubscriptionId = sub.Id,
                    EventId = eventId,
                    EventType = eventType,
                    Payload = JsonSerializer.Serialize(envelope, JsonOptions),
                    Status = "Pending",
                    AvailableAt = createdAt,
                    CreatedAt = createdAt,
                });
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Publishing must never break the originating command.
            logger.LogError(ex, "Webhook publish failed for event {EventType} (team {TeamId}).", eventType, teamId);
            db.ChangeTracker.Clear();
        }
    }

    // ── Deliveries ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<WebhookDeliverySummary>> GetRecentDeliveriesAsync(
        int? teamId, bool includeGlobal, int maxCount = 100, CancellationToken ct = default)
    {
        var subs = db.WebhookSubscriptions.AsNoTracking();
        if (!includeGlobal)
        {
            if (teamId is null) return [];
            subs = subs.Where(s => s.TeamId == teamId);
        }

        var query =
            from d in db.WebhookDeliveries.AsNoTracking()
            join s in subs on d.SubscriptionId equals s.Id
            orderby d.Id descending
            select new WebhookDeliverySummary(
                d.Id, d.SubscriptionId, s.Name, d.EventType, d.EventId,
                d.Status, d.AttemptCount, d.ResponseStatusCode, d.LastError, d.CreatedAt, d.DeliveredAt);

        return await query.Take(Math.Clamp(maxCount, 1, 500)).ToListAsync(ct);
    }

    public async Task<bool> ReplayAsync(long deliveryId, int? teamId, bool isSysAdmin, CancellationToken ct = default)
    {
        var delivery = await db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);
        if (delivery == null) return false;

        var sub = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == delivery.SubscriptionId, ct);
        if (sub == null) return false;
        if (!isSysAdmin && sub.TeamId != teamId) return false;

        delivery.Status = "Pending";
        delivery.AttemptCount = 0;
        delivery.AvailableAt = DateTime.UtcNow;
        delivery.ProcessingAt = null;
        delivery.LastError = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Delivery outbox (background sender) ─────────────────────────────────

    public async Task<WebhookDispatch?> ClaimNextAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var delivery = await db.WebhookDeliveries
            .Where(d => (d.Status == "Pending" && d.AvailableAt <= now)
                     || (d.Status == "Processing" && d.ProcessingAt < now.AddMinutes(-10)))
            .OrderBy(d => d.AvailableAt).ThenBy(d => d.Id)
            .FirstOrDefaultAsync(ct);
        if (delivery == null) return null;

        var sub = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == delivery.SubscriptionId, ct);
        if (sub is null || !sub.IsActive)
        {
            delivery.Status = "Canceled";
            delivery.LastError = "Subscription is inactive or was deleted.";
            delivery.ProcessingAt = null;
            await db.SaveChangesAsync(ct);
            return null;
        }

        delivery.Status = "Processing";
        delivery.ProcessingAt = now;
        delivery.AttemptCount++;
        await db.SaveChangesAsync(ct);

        return new WebhookDispatch(delivery.Id, sub.Id, sub.Url, sub.Secret, delivery.EventType, delivery.EventId, delivery.Payload);
    }

    public async Task MarkDeliveredAsync(long id, int statusCode, CancellationToken ct = default)
    {
        var delivery = await db.WebhookDeliveries.FindAsync([id], ct);
        if (delivery == null) return;
        delivery.Status = "Delivered";
        delivery.DeliveredAt = DateTime.UtcNow;
        delivery.ResponseStatusCode = statusCode;
        delivery.LastError = null;
        await db.SaveChangesAsync(ct);
        await UpdateSubscriptionHealthAsync(delivery.SubscriptionId, "Delivered", ct);
    }

    public async Task MarkFailedAsync(long id, int? statusCode, string? error, CancellationToken ct = default)
    {
        var delivery = await db.WebhookDeliveries.FindAsync([id], ct);
        if (delivery == null) return;

        delivery.ResponseStatusCode = statusCode;
        delivery.LastError = string.IsNullOrWhiteSpace(error)
            ? "Unknown delivery failure."
            : error[..Math.Min(error.Length, 2000)];
        delivery.ProcessingAt = null;

        if (delivery.AttemptCount >= MaxAttempts)
        {
            delivery.Status = "DeadLetter";
        }
        else
        {
            delivery.Status = "Pending";
            delivery.AvailableAt = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, delivery.AttemptCount)));
        }
        await db.SaveChangesAsync(ct);
        await UpdateSubscriptionHealthAsync(delivery.SubscriptionId, delivery.Status == "DeadLetter" ? "DeadLetter" : "Failed", ct);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task UpdateSubscriptionHealthAsync(int subscriptionId, string status, CancellationToken ct)
    {
        var sub = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
        if (sub == null) return;
        sub.LastStatus = status;
        sub.LastDeliveryAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<WebhookSubscription?> FindOwnedAsync(int id, int? teamId, bool isSysAdmin, CancellationToken ct)
    {
        var sub = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sub == null) return null;
        if (!isSysAdmin && sub.TeamId != teamId) return null;
        return sub;
    }

    private async Task<Dictionary<int, string>> ResolveTeamNamesAsync(IEnumerable<int> teamIds, CancellationToken ct)
    {
        var ids = teamIds.Distinct().ToList();
        return await db.Teams.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
    }

    private static string RandomToken(int bytes)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
