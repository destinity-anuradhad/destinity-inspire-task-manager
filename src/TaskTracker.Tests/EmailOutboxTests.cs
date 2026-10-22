using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class EmailOutboxTests
{
    [Fact]
    public async Task EnqueueAsync_PersistsOneMessageForAnIdempotencyKey()
    {
        using var db = DbContextFactory.Create(nameof(EnqueueAsync_PersistsOneMessageForAnIdempotencyKey));
        var queue = new EmailQueue(db);
        var item = new EmailQueueItem("priya@example.test", "Priya", "Assignment", "<p>Hello</p>", "task-42-assigned-priya");

        await queue.EnqueueAsync(item);
        await queue.EnqueueAsync(item);

        var message = Assert.Single(db.EmailOutboxMessages);
        Assert.Equal("Pending", message.Status);
        Assert.Equal("task-42-assigned-priya", message.IdempotencyKey);
    }

    [Fact]
    public async Task ClaimNextAsync_AndMarkSent_RecordsDeliveryLifecycle()
    {
        using var db = DbContextFactory.Create(nameof(ClaimNextAsync_AndMarkSent_RecordsDeliveryLifecycle));
        var queue = new EmailQueue(db);
        await queue.EnqueueAsync(new EmailQueueItem("priya@example.test", "Priya", "Assignment", "<p>Hello</p>"));

        var claimed = await queue.ClaimNextAsync();

        Assert.NotNull(claimed);
        Assert.Equal("Processing", claimed.Status);
        Assert.Equal(1, claimed.AttemptCount);
        await queue.MarkSentAsync(claimed.Id);
        var sent = Assert.Single(db.EmailOutboxMessages);
        Assert.Equal("Sent", sent.Status);
        Assert.NotNull(sent.SentAt);
    }

    [Fact]
    public async Task MarkFailedAsync_RetriesThenMovesMessageToDeadLetter()
    {
        using var db = DbContextFactory.Create(nameof(MarkFailedAsync_RetriesThenMovesMessageToDeadLetter));
        var queue = new EmailQueue(db);
        await queue.EnqueueAsync(new EmailQueueItem("priya@example.test", "Priya", "Assignment", "<p>Hello</p>"));

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var claimed = await queue.ClaimNextAsync();
            Assert.NotNull(claimed);
            await queue.MarkFailedAsync(claimed.Id, "SMTP unavailable");
            if (attempt < 5)
            {
                var pending = Assert.Single(db.EmailOutboxMessages);
                pending.AvailableAt = DateTime.UtcNow.AddSeconds(-1);
                await db.SaveChangesAsync();
            }
        }

        var deadLetter = Assert.Single(db.EmailOutboxMessages);
        Assert.Equal("DeadLetter", deadLetter.Status);
        Assert.Equal(5, deadLetter.AttemptCount);
        Assert.Equal("SMTP unavailable", deadLetter.LastError);
    }

    [Fact]
    public async Task ReplayAsync_OnlyRequeuesDeadLetterMessages()
    {
        using var db = DbContextFactory.Create(nameof(ReplayAsync_OnlyRequeuesDeadLetterMessages));
        var queue = new EmailQueue(db);
        await queue.EnqueueAsync(new EmailQueueItem("priya@example.test", "Priya", "Assignment", "<p>Hello</p>"));
        var message = await queue.ClaimNextAsync();
        Assert.NotNull(message);
        message.AttemptCount = 5;
        await db.SaveChangesAsync();
        await queue.MarkFailedAsync(message.Id, "Permanent SMTP failure");

        Assert.True(await queue.ReplayAsync(message.Id));
        var replayed = Assert.Single(db.EmailOutboxMessages);
        Assert.Equal("Pending", replayed.Status);
        Assert.Equal(0, replayed.AttemptCount);
        Assert.Null(replayed.LastError);
        Assert.False(await queue.ReplayAsync(message.Id));
    }
}
