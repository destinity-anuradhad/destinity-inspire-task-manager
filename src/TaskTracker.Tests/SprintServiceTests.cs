using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class SprintServiceTests
{
    [Fact]
    public async Task CompleteAsync_CreatesImmutableSnapshotAndCarryForwardEvents()
    {
        using var db = DbContextFactory.Create(nameof(CompleteAsync_CreatesImmutableSnapshotAndCarryForwardEvents));
        var source = new Sprint { Name = "Sprint 1", TeamId = 1, IsActive = true };
        var target = new Sprint { Name = "Sprint 2", TeamId = 1, IsActive = true };
        db.Sprints.AddRange(source, target);
        await db.SaveChangesAsync();
        var done = new TaskItem { Goal = "Done", ItemType = "Task", Status = "Done", Weight = 3, SprintId = source.Id, TeamId = 1, RowVersion = [] };
        var open = new TaskItem { Goal = "Open", ItemType = "Task", Status = "In Progress", Weight = 5, SprintId = source.Id, TeamId = 1, RowVersion = [] };
        db.Tasks.AddRange(done, open);
        await db.SaveChangesAsync();

        var service = new SprintService(db);
        var result = await service.CompleteAsync(source.Id, target.Id.ToString(), 1, "Admin");

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.Moved);
        var snapshot = Assert.Single(db.SprintCompletionSnapshots);
        Assert.Equal(2, snapshot.CommittedCount);
        Assert.Equal(1, snapshot.CompletedCount);
        Assert.Equal(1, snapshot.CarriedForwardCount);
        Assert.Equal(8, snapshot.CommittedEstimate);
        Assert.Equal(3, snapshot.CompletedEstimate);
        Assert.Equal(target.Id, (await db.Tasks.FindAsync(open.Id))!.SprintId);
        Assert.Equal(source.Id, (await db.Tasks.FindAsync(done.Id))!.SprintId);
        Assert.Equal(2, db.SprintWorkItemEvents.Count());
        Assert.NotNull((await db.Sprints.FindAsync(source.Id))!.CompletedAt);
    }

    [Fact]
    public async Task CompleteAsync_RejectsRepeatedCompletionAndSameSprintCarryForward()
    {
        using var db = DbContextFactory.Create(nameof(CompleteAsync_RejectsRepeatedCompletionAndSameSprintCarryForward));
        var sprint = new Sprint { Name = "Sprint", TeamId = 1, IsActive = true };
        db.Sprints.Add(sprint);
        await db.SaveChangesAsync();
        var service = new SprintService(db);

        var sameTarget = await service.CompleteAsync(sprint.Id, sprint.Id.ToString(), 1, "Admin");
        Assert.False(sameTarget.Success);
        Assert.Contains("itself", sameTarget.Error, StringComparison.OrdinalIgnoreCase);

        var complete = await service.CompleteAsync(sprint.Id, "backlog", 1, "Admin");
        Assert.True(complete.Success, complete.Error);
        var repeat = await service.CompleteAsync(sprint.Id, "backlog", 1, "Admin");
        Assert.False(repeat.Success);
        Assert.Contains("already", repeat.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCapacityAsync_SplitsCoOwnedHoursAndExcludesTimeOff()
    {
        using var db = DbContextFactory.Create(nameof(GetCapacityAsync_SplitsCoOwnedHoursAndExcludesTimeOff));
        var priya = new AppUser { Username = "priya", DisplayName = "Priya", DailyCapacityHours = 8, WorkingDays = "Mon,Tue,Wed,Thu,Fri" };
        var kasun = new AppUser { Username = "kasun", DisplayName = "Kasun", DailyCapacityHours = 8, WorkingDays = "Mon,Tue,Wed,Thu,Fri" };
        var sprint = new Sprint { Name = "Capacity", TeamId = 1, StartDate = new DateTime(2026, 9, 7), EndDate = new DateTime(2026, 9, 11), IsActive = true };
        db.AppUsers.AddRange(priya, kasun);
        db.Sprints.Add(sprint);
        await db.SaveChangesAsync();
        db.TeamMembers.AddRange(
            new TeamMember { TeamId = 1, UserId = priya.Id, TeamRole = "Member" },
            new TeamMember { TeamId = 1, UserId = kasun.Id, TeamRole = "Member" });
        db.Tasks.Add(new TaskItem {
            Goal = "Shared work", Owner = "Priya, Kasun", EstimateUnit = "Hours", Weight = 10,
            SprintId = sprint.Id, TeamId = 1, Status = "New", RowVersion = []
        });
        db.UserTimeOff.Add(new UserTimeOff {
            UserId = priya.Id, StartDate = new DateTime(2026, 9, 9), EndDate = new DateTime(2026, 9, 9), CreatedBy = "Admin"
        });
        await db.SaveChangesAsync();

        var result = await new SprintService(db).GetCapacityAsync(sprint.Id, 1);

        Assert.NotNull(result);
        Assert.Equal(10, result.TotalAllocated);
        var priyaRow = Assert.Single(result.Rows, r => r.OwnerName == "Priya");
        var kasunRow = Assert.Single(result.Rows, r => r.OwnerName == "Kasun");
        Assert.Equal(5, priyaRow.AllocatedHours);
        Assert.Equal(5, kasunRow.AllocatedHours);
        Assert.Equal(32, priyaRow.CapacityHours);
        Assert.Equal(40, kasunRow.CapacityHours);
    }
}
