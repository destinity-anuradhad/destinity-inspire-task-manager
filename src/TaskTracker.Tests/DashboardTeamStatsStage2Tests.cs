using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

/// <summary>
/// Backlog #7, stage 2: the team dashboard's per-owner stats attribute tasks via the TaskOwners
/// junction (not the Owner string).
/// </summary>
public class DashboardTeamStatsStage2Tests
{
    [Fact]
    public async Task TeamOwnerStats_ResolvedFromJunction()
    {
        using var db = DbContextFactory.Create(nameof(TeamOwnerStats_ResolvedFromJunction));
        db.AppUsers.Add(new AppUser { Id = 1, DisplayName = "Alice", Username = "alice" });
        db.SaveChanges();

        // Owner string empty — only the junction attributes the task to Alice.
        var task = new TaskItem { Goal = "X", TeamId = 1, Owner = "", Status = "New", ItemType = "Task", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        db.SaveChanges();
        db.TaskOwners.Add(new TaskOwner { TaskId = task.Id, UserId = 1 });
        db.SaveChanges();

        var data = await new DashboardService(db).GetTeamAsync(1);

        Assert.Contains(data.OwnerStats, o => o.Name == "Alice" && o.Total == 1);
    }
}
