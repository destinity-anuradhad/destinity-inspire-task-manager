using TaskTracker.Models;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class ActivityServiceTests
{
    [Fact]
    public async Task LogAsync_AddsEntryToDatabase()
    {
        using var db  = DbContextFactory.Create(nameof(LogAsync_AddsEntryToDatabase));
        var svc = ServiceFactory.CreateActivityService(db);

        await svc.LogAsync("alice", "Created", "Task", "Created task #1");

        var log = db.ActivityLog.Single();
        Assert.Equal("alice",    log.ChangedBy);
        Assert.Equal("Created",  log.Action);
        Assert.Equal("Task",     log.Category);
    }

    [Fact]
    public async Task LogAsync_ResolvesTeamIdFromTask()
    {
        using var db  = DbContextFactory.Create(nameof(LogAsync_ResolvesTeamIdFromTask));
        var task = new TaskItem { Goal = "T", TeamId = 5, CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateActivityService(db);
        await svc.LogAsync("bob", "Updated", "Task", "details", taskId: task.Id);

        var log = db.ActivityLog.Single();
        Assert.Equal(5, log.TeamId);
    }

    [Fact]
    public async Task GetLogsAsync_FiltersByProject()
    {
        using var db  = DbContextFactory.Create(nameof(GetLogsAsync_FiltersByProject));
        db.ActivityLog.AddRange(
            new ActivityLog { ChangedBy = "a", Action = "x", Category = "Task", Project = "Alpha", ChangedAt = DateTime.UtcNow },
            new ActivityLog { ChangedBy = "b", Action = "x", Category = "Task", Project = "Beta",  ChangedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateActivityService(db);
        var result = await svc.GetLogsAsync(isAdmin: true, teamId: null, page: 1,
            project: "Alpha", user: null, dateFrom: null, dateTo: null, category: null);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Alpha", result.Logs[0].Project);
    }

    [Fact]
    public async Task GetLogsAsync_FiltersByUser()
    {
        using var db  = DbContextFactory.Create(nameof(GetLogsAsync_FiltersByUser));
        db.ActivityLog.AddRange(
            new ActivityLog { ChangedBy = "alice", Action = "x", Category = "Task", ChangedAt = DateTime.UtcNow },
            new ActivityLog { ChangedBy = "bob",   Action = "x", Category = "Task", ChangedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateActivityService(db);
        var result = await svc.GetLogsAsync(isAdmin: true, teamId: null, page: 1,
            project: null, user: "alice", dateFrom: null, dateTo: null, category: null);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("alice", result.Logs[0].ChangedBy);
    }

    [Fact]
    public async Task GetLogsAsync_PaginatesResults()
    {
        using var db  = DbContextFactory.Create(nameof(GetLogsAsync_PaginatesResults));
        for (int i = 1; i <= 60; i++)
            db.ActivityLog.Add(new ActivityLog {
                ChangedBy = $"user{i}", Action = "x", Category = "Task",
                ChangedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateActivityService(db);
        var p1  = await svc.GetLogsAsync(isAdmin: true, teamId: null, page: 1,
            project: null, user: null, dateFrom: null, dateTo: null, category: null);
        var p2  = await svc.GetLogsAsync(isAdmin: true, teamId: null, page: 2,
            project: null, user: null, dateFrom: null, dateTo: null, category: null);

        Assert.Equal(60, p1.TotalCount);
        Assert.Equal(50, p1.Logs.Count);
        Assert.Equal(10, p2.Logs.Count);
    }

    [Fact]
    public async Task GetLogsAsync_FiltersByDateRange()
    {
        using var db  = DbContextFactory.Create(nameof(GetLogsAsync_FiltersByDateRange));
        db.ActivityLog.AddRange(
            new ActivityLog { ChangedBy = "a", Action = "x", Category = "Task", ChangedAt = new DateTime(2026, 1, 5) },
            new ActivityLog { ChangedBy = "b", Action = "x", Category = "Task", ChangedAt = new DateTime(2026, 1, 15) },
            new ActivityLog { ChangedBy = "c", Action = "x", Category = "Task", ChangedAt = new DateTime(2026, 1, 25) }
        );
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateActivityService(db);
        var result = await svc.GetLogsAsync(isAdmin: true, teamId: null, page: 1,
            project: null, user: null, dateFrom: "2026-01-10", dateTo: "2026-01-20", category: null);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("b", result.Logs[0].ChangedBy);
    }
}
