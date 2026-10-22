using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class OwnerReconciliationTests
{
    private static AppUser User(TaskTracker.Data.AppDbContext db, int id, string name)
    {
        var u = new AppUser { Id = id, DisplayName = name, Username = name.ToLowerInvariant() };
        db.AppUsers.Add(u);
        return u;
    }

    private static TaskItem Task(TaskTracker.Data.AppDbContext db, string owner)
    {
        var t = new TaskItem { Goal = "T", TeamId = 1, Owner = owner, Status = "New", ItemType = "Task", RowVersion = [] };
        db.Tasks.Add(t);
        db.SaveChanges();
        return t;
    }

    [Fact]
    public async Task Clean_WhenJunctionMatchesString()
    {
        using var db = DbContextFactory.Create(nameof(Clean_WhenJunctionMatchesString));
        var alice = User(db, 1, "Alice");
        var bob   = User(db, 2, "Bob");
        db.SaveChanges();
        var t = Task(db, "Alice, Bob");
        db.TaskOwners.AddRange(
            new TaskOwner { TaskId = t.Id, UserId = alice.Id },
            new TaskOwner { TaskId = t.Id, UserId = bob.Id });
        // A task with no owners at all is also consistent (empty == empty).
        Task(db, "");
        db.SaveChanges();

        var report = await new OwnerReconciliationService(db).ReconcileAsync();

        Assert.True(report.IsClean, string.Join(" | ", report.Rows.Select(r => r.Issue)));
        Assert.Equal(2, report.TasksChecked);
        Assert.Empty(report.Rows);
    }

    [Fact]
    public async Task Flags_JunctionStringMismatch()
    {
        using var db = DbContextFactory.Create(nameof(Flags_JunctionStringMismatch));
        var alice = User(db, 1, "Alice");
        var bob   = User(db, 2, "Bob");
        db.SaveChanges();
        // String says Alice; junction says Bob — a real disagreement.
        var t = Task(db, "Alice");
        db.TaskOwners.Add(new TaskOwner { TaskId = t.Id, UserId = bob.Id });
        db.SaveChanges();

        var report = await new OwnerReconciliationService(db).ReconcileAsync();

        Assert.False(report.IsClean);
        Assert.Equal(1, report.Mismatches);
        var row = Assert.Single(report.Rows);
        Assert.Equal("2", row.JunctionUserIds);
        Assert.Equal("1", row.ResolvedStringUserIds);
        Assert.Contains("junction != string", row.Issue);
    }

    [Fact]
    public async Task Flags_UnresolvableOwnerName()
    {
        using var db = DbContextFactory.Create(nameof(Flags_UnresolvableOwnerName));
        // "Ghost" is not an app user; junction empty. Sets agree (both empty) but the name is unresolvable.
        Task(db, "Ghost");

        var report = await new OwnerReconciliationService(db).ReconcileAsync();

        Assert.False(report.IsClean);
        Assert.Equal(1, report.TasksWithUnresolvableNames);
        Assert.Contains("unresolvable: Ghost", Assert.Single(report.Rows).Issue);
    }

    [Fact]
    public async Task Flags_AmbiguousDisplayName()
    {
        using var db = DbContextFactory.Create(nameof(Flags_AmbiguousDisplayName));
        User(db, 1, "Sam");
        User(db, 2, "Sam");   // duplicate display name → ambiguous, cannot resolve
        db.SaveChanges();
        Task(db, "Sam");

        var report = await new OwnerReconciliationService(db).ReconcileAsync();

        Assert.False(report.IsClean);
        Assert.Equal(1, report.TasksWithUnresolvableNames);
    }
}
