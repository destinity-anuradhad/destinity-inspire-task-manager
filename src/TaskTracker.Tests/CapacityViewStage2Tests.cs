using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

/// <summary>
/// Backlog #7, stage 2: the sprint capacity view resolves each task's owners from the TaskOwners
/// junction (not the Owner string) and still splits a co-owned task's estimate evenly.
/// </summary>
public class CapacityViewStage2Tests
{
    [Fact]
    public async Task CapacityView_SourcesOwnersFromJunction_AndSplitsCoOwnedHours()
    {
        using var db = DbContextFactory.Create(nameof(CapacityView_SourcesOwnersFromJunction_AndSplitsCoOwnedHours));
        db.AppUsers.AddRange(
            new AppUser { Id = 1, DisplayName = "Alice", Username = "alice", Status = "Active" },
            new AppUser { Id = 2, DisplayName = "Bob",   Username = "bob",   Status = "Active" });
        db.Sprints.Add(new Sprint { Id = 1, TeamId = 1, Name = "S1", IsActive = true });
        db.SaveChanges();

        // Owner string intentionally EMPTY — only the junction names the owners.
        var task = new TaskItem {
            Goal = "X", TeamId = 1, SprintId = 1, Owner = "", ItemType = "Task", Status = "New",
            EstimateUnit = "Hours", Weight = 8m, CreatedAt = DateTime.UtcNow, RowVersion = [],
        };
        db.Tasks.Add(task);
        db.SaveChanges();
        db.TaskOwners.AddRange(
            new TaskOwner { TaskId = task.Id, UserId = 1 },
            new TaskOwner { TaskId = task.Id, UserId = 2 });
        db.SaveChanges();

        var data = await new SprintService(db).GetCapacityAsync(1, teamId: 1);

        Assert.NotNull(data);
        var alice = data!.Rows.FirstOrDefault(r => r.OwnerName == "Alice");
        var bob   = data.Rows.FirstOrDefault(r => r.OwnerName == "Bob");
        Assert.NotNull(alice);
        Assert.NotNull(bob);
        Assert.Equal(4m, alice!.AllocatedHours);   // 8h split evenly between the two junction owners
        Assert.Equal(4m, bob!.AllocatedHours);
        Assert.Equal(1, alice.TaskCount);
    }
}
