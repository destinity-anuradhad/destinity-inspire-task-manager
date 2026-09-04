using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class PortfolioServiceTests
{
    private static TaskItem Task(TaskTracker.Data.AppDbContext db, int id, int teamId, string status = "Active", string itemType = "Task")
    {
        var t = new TaskItem { Id = id, Goal = $"T{id}", TeamId = teamId, Status = status, ItemType = itemType, RowVersion = [] };
        db.Tasks.Add(t);
        return t;
    }

    private static void Link(TaskTracker.Data.AppDbContext db, int from, int to, string type)
        => db.TaskLinks.Add(new TaskLink { FromTaskId = from, ToTaskId = to, LinkType = type });

    private static void Teams(TaskTracker.Data.AppDbContext db)
    {
        db.Teams.AddRange(
            new Team { Id = 1, Name = "Alpha", IsActive = true },
            new Team { Id = 2, Name = "Beta", IsActive = true });
    }

    [Fact]
    public async Task Detects_CrossTeamBlock_AndFlagsAtRisk()
    {
        using var db = DbContextFactory.Create(nameof(Detects_CrossTeamBlock_AndFlagsAtRisk));
        Teams(db);
        Task(db, 1, teamId: 1, status: "Active");   // blocker (open) in team Alpha
        Task(db, 2, teamId: 2, status: "New");      // blocked in team Beta
        Link(db, 1, 2, "Blocks");                    // #1 blocks #2 (cross-team)
        db.SaveChanges();

        var report = await new PortfolioService(db).GetCrossTeamDependenciesAsync(null, includeAllTeams: true);

        var edge = Assert.Single(report.Edges);
        Assert.Equal(1, edge.BlockerTaskId);
        Assert.Equal(2, edge.BlockedTaskId);
        Assert.Equal("Alpha", edge.BlockerTeamName);
        Assert.Equal("Beta", edge.BlockedTeamName);
        Assert.True(edge.IsAtRisk);          // blocker still open
        Assert.Equal(1, report.AtRisk);
    }

    [Fact]
    public async Task Excludes_SameTeamDependencies()
    {
        using var db = DbContextFactory.Create(nameof(Excludes_SameTeamDependencies));
        Teams(db);
        Task(db, 1, teamId: 1);
        Task(db, 2, teamId: 1);   // same team
        Link(db, 1, 2, "Blocks");
        db.SaveChanges();

        var report = await new PortfolioService(db).GetCrossTeamDependenciesAsync(null, includeAllTeams: true);

        Assert.Equal(0, report.Total);
    }

    [Fact]
    public async Task ClearedWhenBlockerDone_NotAtRisk()
    {
        using var db = DbContextFactory.Create(nameof(ClearedWhenBlockerDone_NotAtRisk));
        Teams(db);
        Task(db, 1, teamId: 1, status: "Done", itemType: "Task");   // blocker completed
        Task(db, 2, teamId: 2, status: "New");
        Link(db, 2, 1, "Blocked By");   // #2 blocked by #1 — same edge, reversed link type
        db.SaveChanges();

        var report = await new PortfolioService(db).GetCrossTeamDependenciesAsync(null, includeAllTeams: true);

        var edge = Assert.Single(report.Edges);
        Assert.Equal(1, edge.BlockerTaskId);   // resolves blocker correctly from "Blocked By"
        Assert.Equal(2, edge.BlockedTaskId);
        Assert.False(edge.IsAtRisk);           // blocker is Done
        Assert.Equal(0, report.AtRisk);
    }

    [Fact]
    public async Task TeamScoped_OnlyEdgesTouchingTeam()
    {
        using var db = DbContextFactory.Create(nameof(TeamScoped_OnlyEdgesTouchingTeam));
        db.Teams.AddRange(
            new Team { Id = 1, Name = "Alpha", IsActive = true },
            new Team { Id = 2, Name = "Beta", IsActive = true },
            new Team { Id = 3, Name = "Gamma", IsActive = true });
        Task(db, 1, teamId: 1); Task(db, 2, teamId: 2);   // Alpha→Beta
        Task(db, 3, teamId: 2); Task(db, 4, teamId: 3);   // Beta→Gamma
        Link(db, 1, 2, "Blocks");
        Link(db, 3, 4, "Blocks");
        db.SaveChanges();

        // Team 3 (Gamma) only sees the edge that touches it.
        var report = await new PortfolioService(db).GetCrossTeamDependenciesAsync(teamId: 3, includeAllTeams: false);

        var edge = Assert.Single(report.Edges);
        Assert.Equal(4, edge.BlockedTaskId);
        Assert.Equal("Gamma", edge.BlockedTeamName);
    }

    [Fact]
    public async Task NonBlockingLinks_Ignored()
    {
        using var db = DbContextFactory.Create(nameof(NonBlockingLinks_Ignored));
        Teams(db);
        Task(db, 1, teamId: 1);
        Task(db, 2, teamId: 2);
        Link(db, 1, 2, "Related");     // not a blocking relationship
        Link(db, 1, 2, "Duplicate");
        db.SaveChanges();

        var report = await new PortfolioService(db).GetCrossTeamDependenciesAsync(null, includeAllTeams: true);

        Assert.Equal(0, report.Total);
    }
}
