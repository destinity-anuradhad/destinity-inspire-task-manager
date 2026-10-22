using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

/// <summary>
/// Backlog #7, stage 1: notification readers resolve owners from the <c>TaskOwners</c> junction, not
/// the denormalized <c>Owner</c> string. These prove the junction is the source of truth (an owner in
/// the junction but missing from the string is still notified) while preserving prior behaviour
/// (self-skip, opt-out, newly-assigned-only).
/// </summary>
public class OwnerNotificationStage1Tests
{
    private static AppUser SeedUser(TaskTracker.Data.AppDbContext db, int id, string name, bool notifyComment = true)
    {
        var u = new AppUser {
            Id = id, DisplayName = name, Username = name.ToLowerInvariant(),
            NotifyOnComment = notifyComment, NotifyOnAssigned = true, Status = "Active",
        };
        db.AppUsers.Add(u);
        return u;
    }

    private static TaskItem SeedTask(TaskTracker.Data.AppDbContext db, string ownerString, int teamId = 1)
    {
        var t = new TaskItem {
            Goal = "Ship", TeamId = teamId, Owner = ownerString, Status = "New", ItemType = "Task",
            EstimateUnit = "Points", CreatedBy = "Admin", CreatedAt = DateTime.UtcNow, RowVersion = [],
        };
        db.Tasks.Add(t);
        db.SaveChanges();
        return t;
    }

    [Fact]
    public async Task Comment_NotifiesOwnerFromJunction_EvenWhenOwnerStringIsStale()
    {
        using var db = DbContextFactory.Create(nameof(Comment_NotifiesOwnerFromJunction_EvenWhenOwnerStringIsStale));
        var alice = SeedUser(db, 5, "Alice");
        // Owner display string intentionally does NOT name Alice — only the junction does.
        var task = SeedTask(db, ownerString: "");
        db.TaskOwners.Add(new TaskOwner { TaskId = task.Id, UserId = alice.Id });
        db.SaveChanges();

        var svc = ServiceFactory.CreateTaskService(db);
        var (ok, err) = await svc.AddCommentAsync(task.Id, "please review", new UserCtx(9, "Bob", "bob", true, "TeamAdmin", 1));

        Assert.True(ok, err);
        Assert.Contains(db.Notifications, n => n.UserId == alice.Id && n.TaskId == task.Id && n.Message.Contains("commented"));
    }

    [Fact]
    public async Task Comment_SkipsCommenterAndOptedOutOwners()
    {
        using var db = DbContextFactory.Create(nameof(Comment_SkipsCommenterAndOptedOutOwners));
        var alice = SeedUser(db, 5, "Alice");                    // is the commenter → never self-notified
        var carol = SeedUser(db, 6, "Carol", notifyComment: false); // opted out → not notified
        var task = SeedTask(db, ownerString: "Alice, Carol");
        db.TaskOwners.AddRange(
            new TaskOwner { TaskId = task.Id, UserId = alice.Id },
            new TaskOwner { TaskId = task.Id, UserId = carol.Id });
        db.SaveChanges();

        var svc = ServiceFactory.CreateTaskService(db);
        await svc.AddCommentAsync(task.Id, "note", new UserCtx(alice.Id, "Alice", "alice", true, "TeamAdmin", 1));

        Assert.DoesNotContain(db.Notifications, n => n.UserId == alice.Id);   // commenter
        Assert.DoesNotContain(db.Notifications, n => n.UserId == carol.Id);   // opted out
    }

    [Fact]
    public async Task Edit_Assignment_NotifiesOnlyNewlyAddedOwner_FromJunction()
    {
        using var db = DbContextFactory.Create(nameof(Edit_Assignment_NotifiesOnlyNewlyAddedOwner_FromJunction));
        var alice = SeedUser(db, 5, "Alice");
        var bob   = SeedUser(db, 6, "Bob");
        var task  = SeedTask(db, ownerString: "Alice");
        db.TaskOwners.Add(new TaskOwner { TaskId = task.Id, UserId = alice.Id });
        db.SaveChanges();

        var svc = ServiceFactory.CreateTaskService(db);
        var edit = new TaskItem {
            Id = task.Id, Goal = "Ship", Owner = "Alice, Bob", Status = "New",
            ItemType = "Task", EstimateUnit = "Points", TeamId = 1,
        };
        var result = await svc.SaveAsync(edit, new UserCtx(1, "Admin", "admin", true, "TeamAdmin", 1), forceClose: false);
        Assert.True(result.Success, result.Error);

        // Bob is the newly added owner (from the synced junction); Alice was already an owner.
        Assert.Contains(db.Notifications, n => n.UserId == bob.Id && n.Message.Contains("assigned"));
        Assert.DoesNotContain(db.Notifications, n => n.UserId == alice.Id && n.Message.Contains("assigned"));
    }
}
