using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class ForecastServiceTests
{
    private static void CompletedSprint(TaskTracker.Data.AppDbContext db, int id, decimal velocity, DateTime completedAt, int teamId = 1)
    {
        db.Sprints.Add(new Sprint { Id = id, TeamId = teamId, Name = $"S{id}", CompletedAt = completedAt, IsActive = false });
        db.SprintCompletionSnapshots.Add(new SprintCompletionSnapshot
        {
            SprintId = id, CompletedEstimate = velocity, CommittedEstimate = velocity, CompletedCount = 1, CommittedCount = 1
        });
    }

    private static void BacklogTask(TaskTracker.Data.AppDbContext db, decimal hours, string status, string itemType = "Task", string unit = "Hours", int teamId = 1)
        => db.Tasks.Add(new TaskItem { Goal = "b", TeamId = teamId, Weight = hours, Status = status, ItemType = itemType, EstimateUnit = unit, RowVersion = [] });

    [Fact]
    public async Task NotEnoughData_WhenNoCompletedSprints()
    {
        using var db = DbContextFactory.Create(nameof(NotEnoughData_WhenNoCompletedSprints));
        BacklogTask(db, 20, "New");
        db.SaveChanges();

        var f = await new ForecastService(db).GetForecastAsync(teamId: 1);

        Assert.False(f.HasEnoughData);
        Assert.Equal(20, f.RemainingHours);   // still reports remaining backlog
        Assert.Null(f.ProjectedSprintsExpected);
    }

    [Fact]
    public async Task ProjectsExpectedOptimisticPessimistic_FromVelocityRange()
    {
        using var db = DbContextFactory.Create(nameof(ProjectsExpectedOptimisticPessimistic_FromVelocityRange));
        var t = new DateTime(2026, 1, 1);
        CompletedSprint(db, 1, velocity: 10, completedAt: t.AddDays(10));
        CompletedSprint(db, 2, velocity: 20, completedAt: t.AddDays(20));
        CompletedSprint(db, 3, velocity: 30, completedAt: t.AddDays(30));   // avg 20, min 10, max 30
        BacklogTask(db, 100, "New");                                        // 100 remaining hours
        db.SaveChanges();

        var f = await new ForecastService(db).GetForecastAsync(teamId: 1);

        Assert.True(f.HasEnoughData);
        Assert.Equal(3, f.CompletedSprintsUsed);
        Assert.Equal(20m, f.AverageVelocity);
        Assert.Equal(100m, f.RemainingHours);
        Assert.Equal(5, f.ProjectedSprintsExpected);       // 100 / 20
        Assert.Equal(4, f.ProjectedSprintsOptimistic);     // ceil(100 / 30)
        Assert.Equal(10, f.ProjectedSprintsPessimistic);   // 100 / 10
        Assert.Equal(3, f.History.Count);
        Assert.True(f.History[0].CompletedAt <= f.History[2].CompletedAt);   // oldest first
    }

    [Fact]
    public async Task RemainingHours_ExcludesDoneWork_IncludesAllEstimatedWork()
    {
        using var db = DbContextFactory.Create(nameof(RemainingHours_ExcludesDoneWork_IncludesAllEstimatedWork));
        CompletedSprint(db, 1, velocity: 10, completedAt: new DateTime(2026, 1, 5));
        BacklogTask(db, 8, "New");                             // counts
        BacklogTask(db, 5, "Done", itemType: "Task");          // done → excluded
        BacklogTask(db, 3, "Closed", itemType: "Bug");         // done → excluded
        BacklogTask(db, 40, "New", unit: "Points");            // still estimated work → counts (hours project)
        db.SaveChanges();

        var f = await new ForecastService(db).GetForecastAsync(teamId: 1);

        Assert.Equal(48m, f.RemainingHours);                   // 8 + 40
        Assert.Equal(5, f.ProjectedSprintsExpected);           // ceil(48 / 10)
    }

    [Fact]
    public async Task NoRemainingWork_ProjectsZeroSprints()
    {
        using var db = DbContextFactory.Create(nameof(NoRemainingWork_ProjectsZeroSprints));
        CompletedSprint(db, 1, velocity: 15, completedAt: new DateTime(2026, 2, 1));
        db.SaveChanges();

        var f = await new ForecastService(db).GetForecastAsync(teamId: 1);

        Assert.True(f.HasEnoughData);
        Assert.Equal(0m, f.RemainingHours);
        Assert.Equal(0, f.ProjectedSprintsExpected);
    }

    [Fact]
    public async Task IsTeamScoped()
    {
        using var db = DbContextFactory.Create(nameof(IsTeamScoped));
        CompletedSprint(db, 1, velocity: 10, completedAt: new DateTime(2026, 1, 5), teamId: 1);
        CompletedSprint(db, 2, velocity: 99, completedAt: new DateTime(2026, 1, 6), teamId: 2);
        BacklogTask(db, 30, "New", teamId: 1);
        BacklogTask(db, 500, "New", teamId: 2);
        db.SaveChanges();

        var f = await new ForecastService(db).GetForecastAsync(teamId: 1);

        Assert.Equal(10m, f.AverageVelocity);     // only team 1's sprint
        Assert.Equal(30m, f.RemainingHours);      // only team 1's backlog
    }

    [Fact]
    public async Task ReportsActualHoursVelocity_AlongsideEstimate()
    {
        using var db = DbContextFactory.Create(nameof(ReportsActualHoursVelocity_AlongsideEstimate));
        CompletedSprint(db, 1, velocity: 20, completedAt: new DateTime(2026, 1, 5));   // estimate delivered 20
        CompletedSprint(db, 2, velocity: 20, completedAt: new DateTime(2026, 1, 12));  // estimate delivered 20
        // Actual hours logged: sprint 1 → 30 (10+20), sprint 2 → 20.
        db.SprintHoursLogs.AddRange(
            new SprintHoursLog { SprintId = 1, TaskId = 1, Hours = 10, LoggedBy = "a", LoggedAt = new DateTime(2026, 1, 3) },
            new SprintHoursLog { SprintId = 1, TaskId = 2, Hours = 20, LoggedBy = "a", LoggedAt = new DateTime(2026, 1, 4) },
            new SprintHoursLog { SprintId = 2, TaskId = 3, Hours = 20, LoggedBy = "a", LoggedAt = new DateTime(2026, 1, 10) });
        BacklogTask(db, 40, "New");
        db.SaveChanges();

        var f = await new ForecastService(db).GetForecastAsync(teamId: 1);

        Assert.Equal(20m, f.AverageVelocity);          // estimate velocity unchanged
        Assert.Equal(25m, f.AverageActualVelocity);    // (30 + 20) / 2
        // History carries both figures per sprint (oldest first: sprint 1 then 2).
        Assert.Equal(30m, f.History[0].ActualHours);
        Assert.Equal(20m, f.History[0].CompletedEstimate);
        Assert.Equal(20m, f.History[1].ActualHours);
    }
}
