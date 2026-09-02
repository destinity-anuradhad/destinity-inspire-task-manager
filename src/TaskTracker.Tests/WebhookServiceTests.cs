using Microsoft.Extensions.Logging.Abstractions;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class WebhookServiceTests
{
    private static WebhookService NewService(TaskTracker.Data.AppDbContext db)
        => new(db, NullLogger<WebhookService>.Instance);

    private static Team SeedTeam(TaskTracker.Data.AppDbContext db)
    {
        var team = new Team { Name = "Core", IsActive = true };
        db.Add(team);
        db.SaveChanges();
        return team;
    }

    [Fact]
    public async Task Publish_FansOutOnlyToMatchingSubscriptions()
    {
        using var db = DbContextFactory.Create(nameof(Publish_FansOutOnlyToMatchingSubscriptions));
        var team = SeedTeam(db);
        var svc = NewService(db);

        var created = await svc.CreateAsync(new WebhookCreateRequest(
            "hook", "https://example.com/h", team.Id, OwnerUserId: 1, [WebhookEvents.TaskCreated], "tester"));
        Assert.StartsWith("whsec_", created.Secret);

        await svc.PublishAsync(team.Id, WebhookEvents.TaskCreated, new { id = 5, goal = "Ship it" }, "tester");
        var delivery = Assert.Single(db.WebhookDeliveries);
        Assert.Equal("task.created", delivery.EventType);
        Assert.Contains("Ship it", delivery.Payload);
        Assert.Equal("Pending", delivery.Status);

        // A different event the subscription didn't request produces nothing.
        await svc.PublishAsync(team.Id, WebhookEvents.TaskCommentAdded, new { id = 5 }, "tester");
        Assert.Single(db.WebhookDeliveries);
    }

    [Fact]
    public async Task ClaimNext_then_MarkDelivered_CompletesDelivery()
    {
        using var db = DbContextFactory.Create(nameof(ClaimNext_then_MarkDelivered_CompletesDelivery));
        var team = SeedTeam(db);
        var svc = NewService(db);
        await svc.CreateAsync(new WebhookCreateRequest("h", "https://example.com/h", team.Id, 1, [WebhookEvents.Wildcard], "t"));
        await svc.PublishAsync(team.Id, WebhookEvents.TaskStatusChanged, new { id = 1 }, "t");

        var dispatch = await svc.ClaimNextAsync();
        Assert.NotNull(dispatch);
        Assert.Equal("https://example.com/h", dispatch!.Url);
        Assert.Equal("task.status_changed", dispatch.EventType);

        await svc.MarkDeliveredAsync(dispatch.DeliveryId, 200);
        var delivery = Assert.Single(db.WebhookDeliveries);
        Assert.Equal("Delivered", delivery.Status);
        Assert.Equal(200, delivery.ResponseStatusCode);
        Assert.Equal("Delivered", db.WebhookSubscriptions.Single().LastStatus);
    }

    [Fact]
    public async Task MarkFailed_ReschedulesWithBackoff()
    {
        using var db = DbContextFactory.Create(nameof(MarkFailed_ReschedulesWithBackoff));
        var team = SeedTeam(db);
        var svc = NewService(db);
        await svc.CreateAsync(new WebhookCreateRequest("h", "https://example.com/h", team.Id, 1, [WebhookEvents.Wildcard], "t"));
        await svc.PublishAsync(team.Id, WebhookEvents.TaskCreated, new { id = 1 }, "t");

        var dispatch = await svc.ClaimNextAsync();
        await svc.MarkFailedAsync(dispatch!.DeliveryId, 500, "boom");

        var delivery = Assert.Single(db.WebhookDeliveries);
        Assert.Equal("Pending", delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Equal("boom", delivery.LastError);
        Assert.True(delivery.AvailableAt > DateTime.UtcNow);   // backed off into the future
    }

    [Theory]
    [InlineData("*", "task.created", true)]
    [InlineData("task.created task.assigned", "task.assigned", true)]
    [InlineData("task.created", "task.assigned", false)]
    public void Matches_RespectsSubscribedEvents(string subscribed, string ev, bool expected)
        => Assert.Equal(expected, WebhookEvents.Matches(subscribed, ev));
}
