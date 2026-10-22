using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class UserServiceTests
{
    [Fact]
    public async Task GetByUsernameAsync_ReturnsUser_WhenFound()
    {
        using var db  = DbContextFactory.Create(nameof(GetByUsernameAsync_ReturnsUser_WhenFound));
        db.AppUsers.Add(new AppUser { Username = "priya", DisplayName = "Priya" });
        await db.SaveChangesAsync();

        var svc  = ServiceFactory.CreateUserService(db);
        var user = await svc.GetByUsernameAsync("priya");

        Assert.NotNull(user);
        Assert.Equal("Priya", user.DisplayName);
    }

    [Fact]
    public async Task GetByUsernameAsync_ReturnsNull_WhenNotFound()
    {
        using var db  = DbContextFactory.Create(nameof(GetByUsernameAsync_ReturnsNull_WhenNotFound));
        var svc  = ServiceFactory.CreateUserService(db);
        var user = await svc.GetByUsernameAsync("nobody");

        Assert.Null(user);
    }

    [Fact]
    public async Task GetProfileStatsAsync_CountsAssignedAndCompletedTasks()
    {
        using var db  = DbContextFactory.Create(nameof(GetProfileStatsAsync_CountsAssignedAndCompletedTasks));
        var user = new AppUser { Username = "kasun", DisplayName = "Kasun" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        // 3 tasks assigned: 1 completed (has ActualEndDate), 2 open
        var t1 = new TaskItem { Goal = "T1", CreatedAt = DateTime.UtcNow, ActualEndDate = DateTime.UtcNow, RowVersion = [] };
        var t2 = new TaskItem { Goal = "T2", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        var t3 = new TaskItem { Goal = "T3", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.AddRange(t1, t2, t3);
        await db.SaveChangesAsync();

        db.TaskOwners.AddRange(
            new TaskOwner { TaskId = t1.Id, UserId = user.Id },
            new TaskOwner { TaskId = t2.Id, UserId = user.Id },
            new TaskOwner { TaskId = t3.Id, UserId = user.Id }
        );
        await db.SaveChangesAsync();

        var svc   = ServiceFactory.CreateUserService(db);
        var stats = await svc.GetProfileStatsAsync(user.Id, user.DisplayName);

        Assert.Equal(3, stats.TasksAssigned);
        Assert.Equal(1, stats.TasksCompleted);
    }

    [Fact]
    public async Task UpdateProfileAsync_UpdatesDisplayName()
    {
        using var db  = DbContextFactory.Create(nameof(UpdateProfileAsync_UpdatesDisplayName));
        var user = new AppUser { Username = "anuradha", DisplayName = "Anuradha" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateUserService(db);
        var result = await svc.UpdateProfileAsync(user.Id, new ProfileUpdateRequest {
            DisplayName = "Anuradha D"
        });

        Assert.True(result.Success);
        var updated = await db.AppUsers.FindAsync(user.Id);
        Assert.Equal("Anuradha D", updated!.DisplayName);
    }

    [Fact]
    public async Task UpdateProfileAsync_ReturnsError_WhenUserNotFound()
    {
        using var db  = DbContextFactory.Create(nameof(UpdateProfileAsync_ReturnsError_WhenUserNotFound));
        var svc    = ServiceFactory.CreateUserService(db);
        var result = await svc.UpdateProfileAsync(999, new ProfileUpdateRequest { DisplayName = "X" });

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task GetProfileStatsAsync_CountsWatchedTasks()
    {
        using var db  = DbContextFactory.Create(nameof(GetProfileStatsAsync_CountsWatchedTasks));
        var user = new AppUser { Username = "watcher", DisplayName = "Watcher" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        var task = new TaskItem { Goal = "Watched", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        db.TaskWatchers.Add(new TaskWatcher { TaskId = task.Id, UserId = user.Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var svc   = ServiceFactory.CreateUserService(db);
        var stats = await svc.GetProfileStatsAsync(user.Id, user.DisplayName);

        Assert.Equal(1, stats.TasksWatching);
    }

    [Fact]
    public async Task AddTimeOffAsync_StoresNormalizedDatesAndReason()
    {
        using var db = DbContextFactory.Create(nameof(AddTimeOffAsync_StoresNormalizedDatesAndReason));
        var user = new AppUser { Username = "priya", DisplayName = "Priya" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        var result = await ServiceFactory.CreateUserService(db).AddTimeOffAsync(
            user.Id, new DateTime(2026, 9, 4, 13, 0, 0), new DateTime(2026, 9, 5, 9, 0, 0), "  Annual leave  ", "Admin");

        Assert.True(result.Success, result.Error);
        var timeOff = Assert.Single(db.UserTimeOff);
        Assert.Equal(new DateTime(2026, 9, 4), timeOff.StartDate);
        Assert.Equal(new DateTime(2026, 9, 5), timeOff.EndDate);
        Assert.Equal("Annual leave", timeOff.Reason);
    }

    [Fact]
    public async Task AddTimeOffAsync_RejectsEndBeforeStart()
    {
        using var db = DbContextFactory.Create(nameof(AddTimeOffAsync_RejectsEndBeforeStart));
        var result = await ServiceFactory.CreateUserService(db).AddTimeOffAsync(
            123, new DateTime(2026, 9, 5), new DateTime(2026, 9, 4), null, "Admin");

        Assert.False(result.Success);
        Assert.Contains("End date", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
