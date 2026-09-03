using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

/// <summary>
/// Backlog #7, stage 2: the sprint over-capacity warning attributes hour-allocation via the
/// TaskOwners junction and compares to SprintCapacity keyed by user id (not by matching the Owner
/// display string).
/// </summary>
public class CapacityWarningStage2Tests
{
    private static readonly UserCtx Admin = new(9, "Admin", "admin", true, "TeamAdmin", 1);

    private static (ITaskService svc, int sprintId, int taskId) Setup(
        TaskTracker.Data.AppDbContext db, decimal capacity, decimal taskHours)
    {
        db.AppUsers.Add(new AppUser { Id = 1, DisplayName = "Alice", Username = "alice", Status = "Active" });
        db.Sprints.Add(new Sprint { Id = 1, TeamId = 1, Name = "S1", IsActive = true });
        db.SaveChanges();
        db.SprintCapacities.Add(new SprintCapacity { SprintId = 1, UserId = 1, OwnerName = "Alice", CapacityHours = capacity });
        var task = new TaskItem {
            Goal = "Work", TeamId = 1, Owner = "Alice", ItemType = "Task", Status = "New",
            EstimateUnit = "Hours", Weight = taskHours, CreatedAt = DateTime.UtcNow, RowVersion = [],
        };
        db.Tasks.Add(task);
        db.SaveChanges();
        db.TaskOwners.Add(new TaskOwner { TaskId = task.Id, UserId = 1 });
        db.SaveChanges();
        return (ServiceFactory.CreateTaskService(db), 1, task.Id);
    }

    [Fact]
    public async Task Warns_WhenOwnerAllocationExceedsCapacity()
    {
        using var db = DbContextFactory.Create(nameof(Warns_WhenOwnerAllocationExceedsCapacity));
        var (svc, sprintId, taskId) = Setup(db, capacity: 5m, taskHours: 8m);   // 8h assigned vs 5h cap

        var result = await svc.AssignSprintAsync(taskId, sprintId, Admin);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.CapacityWarning);
        Assert.Contains("Alice", result.CapacityWarning);
        Assert.Contains("over capacity", result.CapacityWarning);
    }

    [Fact]
    public async Task NoWarning_WhenWithinCapacity()
    {
        using var db = DbContextFactory.Create(nameof(NoWarning_WhenWithinCapacity));
        var (svc, sprintId, taskId) = Setup(db, capacity: 10m, taskHours: 4m);   // 4h vs 10h cap

        var result = await svc.AssignSprintAsync(taskId, sprintId, Admin);

        Assert.True(result.Success, result.Error);
        Assert.Null(result.CapacityWarning);
    }
}
