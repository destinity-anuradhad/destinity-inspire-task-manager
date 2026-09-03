using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

/// <summary>
/// Backlog #7, stage 2: on every owner write the <c>Owner</c> display string is derived from the
/// resolved owners (canonical display names, caller order, de-duplicated). The junction is
/// authoritative and the string is a lock-step cache, so the two cannot drift.
/// </summary>
public class OwnerStringDerivationStage2Tests
{
    private static readonly UserCtx Admin = new(9, "Admin", "admin", true, "TeamAdmin", 1);

    private static void SeedUsers(TaskTracker.Data.AppDbContext db)
    {
        db.AppUsers.AddRange(
            new AppUser { Id = 1, DisplayName = "Alice", Username = "alice", Status = "Active" },
            new AppUser { Id = 2, DisplayName = "Bob",   Username = "bob",   Status = "Active" });
        db.SaveChanges();
    }

    private static TaskItem SeedTask(TaskTracker.Data.AppDbContext db)
    {
        var t = new TaskItem { Goal = "T", TeamId = 1, Owner = "", Status = "New", ItemType = "Task", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(t);
        db.SaveChanges();
        return t;
    }

    [Fact]
    public async Task Write_NormalizesSpacing_FromJunction()
    {
        using var db = DbContextFactory.Create(nameof(Write_NormalizesSpacing_FromJunction));
        SeedUsers(db);
        var task = SeedTask(db);
        var svc = ServiceFactory.CreateTaskService(db);

        var (ok, err) = await svc.QuickSetOwnerAsync(task.Id, "Alice,Bob", Admin);   // no space after comma
        Assert.True(ok, err);

        db.Entry(task).Reload();
        Assert.Equal("Alice, Bob", task.Owner);                                       // canonical spacing
        Assert.Equal(2, db.TaskOwners.Count(o => o.TaskId == task.Id));
    }

    [Fact]
    public async Task Write_PreservesOrder_AndDeduplicates()
    {
        using var db = DbContextFactory.Create(nameof(Write_PreservesOrder_AndDeduplicates));
        SeedUsers(db);
        var task = SeedTask(db);
        var svc = ServiceFactory.CreateTaskService(db);

        await svc.QuickSetOwnerAsync(task.Id, "Bob, Alice, Bob", Admin);

        db.Entry(task).Reload();
        Assert.Equal("Bob, Alice", task.Owner);   // input order kept, duplicate dropped
        Assert.Equal(2, db.TaskOwners.Count(o => o.TaskId == task.Id));
    }

    [Fact]
    public async Task Write_Unassign_ClearsStringAndJunction()
    {
        using var db = DbContextFactory.Create(nameof(Write_Unassign_ClearsStringAndJunction));
        SeedUsers(db);
        var task = SeedTask(db);
        var svc = ServiceFactory.CreateTaskService(db);
        await svc.QuickSetOwnerAsync(task.Id, "Alice", Admin);

        await svc.QuickSetOwnerAsync(task.Id, "", Admin);

        db.Entry(task).Reload();
        Assert.Null(task.Owner);
        Assert.False(db.TaskOwners.Any(o => o.TaskId == task.Id));
    }

    [Fact]
    public async Task Write_LeavesReconciliationClean()
    {
        using var db = DbContextFactory.Create(nameof(Write_LeavesReconciliationClean));
        SeedUsers(db);
        var task = SeedTask(db);
        var svc = ServiceFactory.CreateTaskService(db);

        await svc.QuickSetOwnerAsync(task.Id, "Bob,Alice ,Bob", Admin);   // messy spacing + duplicate

        var report = await new OwnerReconciliationService(db).ReconcileAsync();
        Assert.True(report.IsClean, string.Join(" | ", report.Rows.Select(r => r.Issue)));
    }
}
