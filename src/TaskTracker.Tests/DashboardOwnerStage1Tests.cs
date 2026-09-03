using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

/// <summary>
/// Backlog #7, stage 1: the personal dashboard's "my tasks" set is resolved from the
/// <c>TaskOwners</c> junction (user id), not <c>Owner.Contains(displayName)</c>.
/// </summary>
public class DashboardOwnerStage1Tests
{
    private static TaskItem Task(TaskTracker.Data.AppDbContext db, string ownerString, int teamId = 1)
    {
        var t = new TaskItem {
            Goal = "T", TeamId = teamId, Owner = ownerString, Status = "New", ItemType = "Task",
            CreatedAt = DateTime.UtcNow, RowVersion = [],
        };
        db.Tasks.Add(t);
        db.SaveChanges();
        return t;
    }

    [Fact]
    public async Task MyTasks_ComeFromJunction_AndAvoidSubstringFalsePositive()
    {
        using var db = DbContextFactory.Create(nameof(MyTasks_ComeFromJunction_AndAvoidSubstringFalsePositive));
        var sam      = new AppUser { Id = 1, DisplayName = "Sam", Username = "sam" };
        var samantha = new AppUser { Id = 2, DisplayName = "Samantha", Username = "samantha" };
        db.AppUsers.AddRange(sam, samantha);
        db.SaveChanges();

        var samTask      = Task(db, ownerString: "Sam");
        var samanthaTask = Task(db, ownerString: "Samantha");   // old Owner.Contains("Sam") wrongly matched this
        db.TaskOwners.AddRange(
            new TaskOwner { TaskId = samTask.Id,      UserId = sam.Id },
            new TaskOwner { TaskId = samanthaTask.Id, UserId = samantha.Id });
        db.SaveChanges();

        var data = await new DashboardService(db).GetPersonalAsync("Sam", teamId: 1);

        Assert.Equal(1, data.Total);   // exactly Sam's one task — not Samantha's
    }

    [Fact]
    public async Task MyTasks_IgnoresOwnerStringWhenJunctionDisagrees()
    {
        using var db = DbContextFactory.Create(nameof(MyTasks_IgnoresOwnerStringWhenJunctionDisagrees));
        var alice = new AppUser { Id = 1, DisplayName = "Alice", Username = "alice" };
        db.AppUsers.Add(alice);
        db.SaveChanges();

        // Owner string names Alice, but the junction does not link her → not her task.
        Task(db, ownerString: "Alice");

        var data = await new DashboardService(db).GetPersonalAsync("Alice", teamId: 1);

        Assert.Equal(0, data.Total);   // junction is the source of truth, not the string
    }
}
