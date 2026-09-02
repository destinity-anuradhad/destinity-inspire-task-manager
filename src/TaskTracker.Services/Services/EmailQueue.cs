using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public record EmailQueueItem(string ToEmail, string ToName, string Subject, string HtmlBody, string? IdempotencyKey = null);

public interface IEmailQueue
{
    Task EnqueueAsync(EmailQueueItem item, CancellationToken ct = default);
    Task<EmailOutboxMessage?> ClaimNextAsync(CancellationToken ct = default);
    Task MarkSentAsync(long id, CancellationToken ct = default);
    Task MarkFailedAsync(long id, string? error, CancellationToken ct = default);
    Task<List<EmailOutboxMessage>> GetRecentAsync(int maxCount = 100, CancellationToken ct = default);
    Task<bool> ReplayAsync(long id, CancellationToken ct = default);
}

public sealed class EmailQueue(AppDbContext db) : IEmailQueue
{
    private const int MaxAttempts = 5;

    public async Task EnqueueAsync(EmailQueueItem item, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(item.ToEmail)) return;
        if (!string.IsNullOrWhiteSpace(item.IdempotencyKey)
            && await db.EmailOutboxMessages.AnyAsync(m => m.IdempotencyKey == item.IdempotencyKey, ct)) return;

        db.EmailOutboxMessages.Add(new EmailOutboxMessage {
            ToEmail = item.ToEmail.Trim(), ToName = item.ToName.Trim(), Subject = item.Subject,
            HtmlBody = item.HtmlBody, IdempotencyKey = item.IdempotencyKey,
            Status = "Pending", AvailableAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(item.IdempotencyKey))
        {
            // Another request won the unique-key race. Clear the failed insert so
            // this scoped context remains usable for the rest of the command.
            db.ChangeTracker.Clear();
        }
    }

    public async Task<EmailOutboxMessage?> ClaimNextAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var message = await db.EmailOutboxMessages
            .Where(m => (m.Status == "Pending" && m.AvailableAt <= now)
                || (m.Status == "Processing" && m.ProcessingAt < now.AddMinutes(-10)))
            .OrderBy(m => m.AvailableAt).ThenBy(m => m.Id).FirstOrDefaultAsync(ct);
        if (message == null) return null;

        message.Status = "Processing";
        message.ProcessingAt = now;
        message.AttemptCount++;
        await db.SaveChangesAsync(ct);
        return message;
    }

    public async Task MarkSentAsync(long id, CancellationToken ct = default)
    {
        var message = await db.EmailOutboxMessages.FindAsync([id], ct);
        if (message == null) return;
        message.Status = "Sent";
        message.SentAt = DateTime.UtcNow;
        message.LastError = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(long id, string? error, CancellationToken ct = default)
    {
        var message = await db.EmailOutboxMessages.FindAsync([id], ct);
        if (message == null) return;
        message.LastError = string.IsNullOrWhiteSpace(error) ? "Unknown delivery failure." : error[..Math.Min(error.Length, 4000)];
        message.ProcessingAt = null;
        if (message.AttemptCount >= MaxAttempts) message.Status = "DeadLetter";
        else
        {
            message.Status = "Pending";
            message.AvailableAt = DateTime.UtcNow.AddMinutes(Math.Min(30, Math.Pow(2, message.AttemptCount)));
        }
        await db.SaveChangesAsync(ct);
    }

    public Task<List<EmailOutboxMessage>> GetRecentAsync(int maxCount = 100, CancellationToken ct = default)
        => db.EmailOutboxMessages.AsNoTracking()
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(Math.Clamp(maxCount, 1, 500)).ToListAsync(ct);

    public async Task<bool> ReplayAsync(long id, CancellationToken ct = default)
    {
        var message = await db.EmailOutboxMessages.FindAsync([id], ct);
        if (message?.Status != "DeadLetter") return false;
        message.Status = "Pending";
        message.AttemptCount = 0;
        message.AvailableAt = DateTime.UtcNow;
        message.ProcessingAt = null;
        message.LastError = null;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
