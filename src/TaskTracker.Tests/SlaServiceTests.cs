using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class SlaServiceTests
{
    private static TaskItem Task(string? itemType, string? priority, string? status, DateTime createdAt,
        DateTime? actualEnd = null, int teamId = 1) => new()
    {
        Goal = "T", TeamId = teamId, ItemType = itemType, Priority = priority, Status = status,
        CreatedAt = createdAt, ActualEndDate = actualEnd, RowVersion = [],
    };

    private static SlaPolicy Policy(int targetHours, string? itemType = null, string? priority = null) => new()
    {
        TeamId = 1, Name = "P", IsEnabled = true, TargetHours = targetHours,
        AppliesToItemType = itemType, AppliesToPriority = priority,
    };

    // ── Evaluation (pure) ─────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_OpenTask_IsOnTrackWellBeforeDeadline()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var task = Task("Bug", "Critical", "Active", createdAt: now.AddHours(-1));   // 1h old, 8h target
        var eval = SlaService.Evaluate(Policy(8), task, now);

        Assert.Equal(SlaStatuses.OnTrack, eval.Status);
        Assert.True(eval.IsOpen);
        Assert.False(eval.IsBreached);
        Assert.Equal(now.AddHours(7), eval.Deadline);
    }

    [Fact]
    public void Evaluate_OpenTask_IsAtRiskInsideThreshold()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        // 8h target, 7h elapsed → 1h remaining ≤ 25% of 8h (2h) → at risk.
        var task = Task("Bug", "Critical", "Active", createdAt: now.AddHours(-7));
        var eval = SlaService.Evaluate(Policy(8), task, now);

        Assert.Equal(SlaStatuses.AtRisk, eval.Status);
    }

    [Fact]
    public void Evaluate_OpenTask_IsBreachedPastDeadline()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var task = Task("Bug", "Critical", "Active", createdAt: now.AddHours(-10));   // 8h target, 10h old
        var eval = SlaService.Evaluate(Policy(8), task, now);

        Assert.Equal(SlaStatuses.Breached, eval.Status);
        Assert.True(eval.IsBreached);
        Assert.True(eval.HoursRemaining < 0);
    }

    [Fact]
    public void Evaluate_DoneTask_IsMetWhenResolvedInTime_ElseBreached()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var onTime  = Task("Bug", "Critical", "Closed", created, actualEnd: created.AddHours(5));
        var late    = Task("Bug", "Critical", "Closed", created, actualEnd: created.AddHours(20));

        Assert.Equal(SlaStatuses.Met,      SlaService.Evaluate(Policy(8), onTime, created.AddDays(1)).Status);
        Assert.Equal(SlaStatuses.Breached, SlaService.Evaluate(Policy(8), late,   created.AddDays(1)).Status);
    }

    [Fact]
    public void FirstMatching_RespectsScopeAndOrder()
    {
        var task = Task("Bug", "Critical", "Active", DateTime.UtcNow);
        var policies = new[]
        {
            Policy(24, itemType: "Story"),               // wrong type
            Policy(48, priority: "Low"),                 // wrong priority
            Policy(8,  itemType: "Bug", priority: "Critical"), // matches
        };

        var match = SlaService.FirstMatching(policies, task);
        Assert.NotNull(match);
        Assert.Equal(8, match!.TargetHours);

        // A task matching no policy scope returns null.
        var story = Task("Epic", "Low", "Active", DateTime.UtcNow);
        Assert.Null(SlaService.FirstMatching(new[] { Policy(8, itemType: "Bug") }, story));
    }

    // ── Reporting ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReport_CountsOpenWorkByStatus_AndExcludesDone()
    {
        using var db = DbContextFactory.Create(nameof(GetReport_CountsOpenWorkByStatus_AndExcludesDone));
        var now = DateTime.UtcNow;
        db.SlaPolicies.Add(Policy(8, itemType: "Bug"));
        db.Tasks.AddRange(
            Task("Bug", "Critical", "Active", now.AddHours(-1)),   // on track
            Task("Bug", "High",     "Active", now.AddHours(-7)),   // at risk
            Task("Bug", "Low",      "Active", now.AddHours(-20)),  // breached
            Task("Bug", "Low",      "Closed", now.AddHours(-20)),  // done → excluded
            Task("Story", "High",   "Active", now.AddHours(-20))); // no matching policy → excluded
        await db.SaveChangesAsync();
        var svc = new SlaService(db);

        var report = await svc.GetReportAsync(1);

        Assert.Equal(3, report.Total);
        Assert.Equal(1, report.OnTrack);
        Assert.Equal(1, report.AtRisk);
        Assert.Equal(1, report.Breached);
        // Most urgent (breached, least remaining) sorts first.
        Assert.Equal(SlaStatuses.Breached, report.Items[0].SlaStatus);
    }

    [Fact]
    public async Task GetReport_EmptyWhenNoEnabledPolicies()
    {
        using var db = DbContextFactory.Create(nameof(GetReport_EmptyWhenNoEnabledPolicies));
        db.Tasks.Add(Task("Bug", "Critical", "Active", DateTime.UtcNow.AddHours(-50)));
        await db.SaveChangesAsync();
        var svc = new SlaService(db);

        var report = await svc.GetReportAsync(1);
        Assert.Equal(0, report.Total);
        Assert.Empty(report.Items);
    }

    // ── Management ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidatesNameAndTarget_AndBlanksEmptyScope()
    {
        using var db = DbContextFactory.Create(nameof(Create_ValidatesNameAndTarget_AndBlanksEmptyScope));
        var svc = new SlaService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(new SlaPolicyInput("", 1, null, null, 8, "t")));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(new SlaPolicyInput("x", 1, null, null, 0, "t")));

        var policy = await svc.CreateAsync(new SlaPolicyInput("Resolve fast", 1, "  ", "High", 12, "t"));
        Assert.Null(policy.AppliesToItemType);       // whitespace scope normalised to null (any)
        Assert.Equal("High", policy.AppliesToPriority);
        Assert.Equal(12, policy.TargetHours);
    }

    [Fact]
    public async Task SetEnabled_and_Delete_AreTeamScoped()
    {
        using var db = DbContextFactory.Create(nameof(SetEnabled_and_Delete_AreTeamScoped));
        var policy = Policy(8);
        db.SlaPolicies.Add(policy);
        await db.SaveChangesAsync();
        var svc = new SlaService(db);

        Assert.False(await svc.SetEnabledAsync(policy.Id, false, teamId: 2, isSysAdmin: false));
        Assert.False(await svc.DeleteAsync(policy.Id, teamId: 2, isSysAdmin: false));

        Assert.True(await svc.SetEnabledAsync(policy.Id, false, teamId: 1, isSysAdmin: false));
        Assert.True(await svc.DeleteAsync(policy.Id, teamId: 1, isSysAdmin: false));
        Assert.Empty(db.SlaPolicies);
    }
}
