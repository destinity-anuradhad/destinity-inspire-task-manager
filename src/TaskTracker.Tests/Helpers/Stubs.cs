using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaskTracker.Hubs;
using TaskTracker.Services;

namespace TaskTracker.Tests.Helpers;

// ── SignalR stubs ─────────────────────────────────────────────────────────────

internal sealed class NullTaskHubContext : IHubContext<TaskHub>
{
    public IHubClients Clients { get; } = new NullHubClients();
    public IGroupManager Groups { get; } = new NullGroupManager();
}

internal sealed class NullHubClients : IHubClients
{
    private static readonly IClientProxy _p = new NullClientProxy();
    public IClientProxy All => _p;
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _p;
    public IClientProxy Client(string connectionId) => _p;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _p;
    public IClientProxy Group(string groupName) => _p;
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _p;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => _p;
    public IClientProxy User(string userId) => _p;
    public IClientProxy Users(IReadOnlyList<string> userIds) => _p;
}

internal sealed class NullClientProxy : IClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class NullGroupManager : IGroupManager
{
    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

// ── Email stub ────────────────────────────────────────────────────────────────

internal sealed class NullEmailService : IEmailService
{
    public Task<EmailDeliveryResult> SendAsync(string toEmail, string toName, string subject, string htmlBody)
        => Task.FromResult(new EmailDeliveryResult(true));
}

internal sealed class NullEmailQueue : IEmailQueue
{
    public Task EnqueueAsync(EmailQueueItem item, CancellationToken ct = default) => Task.CompletedTask;

    public Task<TaskTracker.Models.EmailOutboxMessage?> ClaimNextAsync(CancellationToken ct = default)
        => Task.FromResult<TaskTracker.Models.EmailOutboxMessage?>(null);
    public Task MarkSentAsync(long id, CancellationToken ct = default) => Task.CompletedTask;
    public Task MarkFailedAsync(long id, string? error, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<TaskTracker.Models.EmailOutboxMessage>> GetRecentAsync(int maxCount = 100, CancellationToken ct = default)
        => Task.FromResult(new List<TaskTracker.Models.EmailOutboxMessage>());
    public Task<bool> ReplayAsync(long id, CancellationToken ct = default) => Task.FromResult(false);
}

// ── Webhook stub ────────────────────────────────────────────────────────────

internal sealed class NullWebhookService : IWebhookService
{
    public Task<IReadOnlyList<WebhookSubscriptionSummary>> ListAsync(int? teamId, bool includeGlobal, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WebhookSubscriptionSummary>>([]);
    public Task<WebhookCreateResult> CreateAsync(WebhookCreateRequest request, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<bool> SetActiveAsync(int id, bool active, int? teamId, bool isSysAdmin, CancellationToken ct = default) => Task.FromResult(false);
    public Task<bool> DeleteAsync(int id, int? teamId, bool isSysAdmin, CancellationToken ct = default) => Task.FromResult(false);
    public Task PublishAsync(int? teamId, string eventType, object data, string? actor, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<WebhookDeliverySummary>> GetRecentDeliveriesAsync(int? teamId, bool includeGlobal, int maxCount = 100, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WebhookDeliverySummary>>([]);
    public Task<bool> ReplayAsync(long deliveryId, int? teamId, bool isSysAdmin, CancellationToken ct = default) => Task.FromResult(false);
    public Task<WebhookDispatch?> ClaimNextAsync(CancellationToken ct = default) => Task.FromResult<WebhookDispatch?>(null);
    public Task MarkDeliveredAsync(long id, int statusCode, CancellationToken ct = default) => Task.CompletedTask;
    public Task MarkFailedAsync(long id, int? statusCode, string? error, CancellationToken ct = default) => Task.CompletedTask;
}

// ── Factory helpers ───────────────────────────────────────────────────────────

internal static class ServiceFactory
{
    public static TaskService CreateTaskService(TaskTracker.Data.AppDbContext db)
    {
        var config = new ConfigurationBuilder().Build();
        var cache  = new MemoryCache(new MemoryCacheOptions());
        return new TaskService(db, config, new NullTaskHubContext(), new NullEmailQueue(),
            NullLogger<TaskService>.Instance, cache, new NullWebhookService());
    }

    public static ActivityService CreateActivityService(TaskTracker.Data.AppDbContext db)
        => new ActivityService(db);

    public static UserService CreateUserService(TaskTracker.Data.AppDbContext db)
        => new UserService(db);
}
