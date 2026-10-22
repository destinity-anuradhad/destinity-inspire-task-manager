using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class AutomationServiceTests
{
    private static readonly UserCtx Admin = new(1, "Tester", "tester", true, "TeamAdmin", 1);

    private static TaskItem SeedTask(TaskTracker.Data.AppDbContext db, string? priority = null, string? owner = null,
        string? itemType = "Task", string? status = "New", int teamId = 1)
    {
        var task = new TaskItem
        {
            Goal = "Ship it", TeamId = teamId, Priority = priority, Owner = owner,
            ItemType = itemType, Status = status, CreatedAt = DateTime.UtcNow, RowVersion = [],
        };
        db.Tasks.Add(task);
        db.SaveChanges();
        return task;
    }

    private static AutomationRule SeedRule(TaskTracker.Data.AppDbContext db, string trigger, string action,
        string? actionConfig = null, string? field = "Any", string? op = "any", string? value = null,
        bool enabled = true, int teamId = 1)
    {
        var rule = new AutomationRule
        {
            TeamId = teamId, Name = $"{trigger}->{action}", IsEnabled = enabled, TriggerType = trigger,
            ConditionField = field, ConditionOperator = op, ConditionValue = value,
            ActionType = action, ActionConfig = actionConfig, CreatedAt = DateTime.UtcNow, CreatedBy = "tester",
        };
        db.AutomationRules.Add(rule);
        db.SaveChanges();
        return rule;
    }

    // ── Execution ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunForEvent_UnconditionalRule_ExecutesAndAudits()
    {
        using var db = DbContextFactory.Create(nameof(RunForEvent_UnconditionalRule_ExecutesAndAudits));
        var task = SeedTask(db);
        var rule = SeedRule(db, AutomationTriggers.TaskCreated, AutomationTriggers.ActionAddComment, "Auto note: {goal}");
        var svc = ServiceFactory.CreateAutomationService(db);

        await svc.RunForEventAsync(task, AutomationTriggers.TaskCreated, Admin);

        var comment = Assert.Single(db.ActivityLog.Where(a => a.Action == "Comment"));
        Assert.Equal("Auto note: Ship it", comment.Details);
        Assert.Equal("automation", comment.ChangedBy);

        var run = Assert.Single(db.AutomationRuleRuns);
        Assert.Equal("Executed", run.Status);
        Assert.Equal(rule.Id, run.RuleId);

        db.Entry(rule).Reload();
        Assert.Equal(1, rule.RunCount);
        Assert.NotNull(rule.LastRunAt);
    }

    [Fact]
    public async Task RunForEvent_SetPriority_MutatesTaskWhenConditionMatches()
    {
        using var db = DbContextFactory.Create(nameof(RunForEvent_SetPriority_MutatesTaskWhenConditionMatches));
        var task = SeedTask(db, itemType: "Bug", priority: "Low");
        SeedRule(db, AutomationTriggers.TaskCreated, AutomationTriggers.ActionSetPriority, "High",
            field: AutomationTriggers.FieldItemType, op: AutomationTriggers.OpEquals, value: "Bug");
        var svc = ServiceFactory.CreateAutomationService(db);

        await svc.RunForEventAsync(task, AutomationTriggers.TaskCreated, Admin);

        db.Entry(task).Reload();
        Assert.Equal("High", task.Priority);
    }

    [Fact]
    public async Task RunForEvent_AddLabel_AppendsAndIsIdempotent()
    {
        using var db = DbContextFactory.Create(nameof(RunForEvent_AddLabel_AppendsAndIsIdempotent));
        var task = SeedTask(db);
        task.Labels = "backend";
        db.SaveChanges();
        SeedRule(db, AutomationTriggers.TaskStatusChanged, AutomationTriggers.ActionAddLabel, "needs-review");
        var svc = ServiceFactory.CreateAutomationService(db);

        await svc.RunForEventAsync(task, AutomationTriggers.TaskStatusChanged, Admin);
        db.Entry(task).Reload();
        Assert.Equal("backend, needs-review", task.Labels);

        // Running again does not duplicate the label; run is still audited but reports no change.
        await svc.RunForEventAsync(task, AutomationTriggers.TaskStatusChanged, Admin);
        db.Entry(task).Reload();
        Assert.Equal("backend, needs-review", task.Labels);
    }

    [Fact]
    public async Task RunForEvent_NotifyOwner_CreatesNotificationForOwnerUser()
    {
        using var db = DbContextFactory.Create(nameof(RunForEvent_NotifyOwner_CreatesNotificationForOwnerUser));
        db.AppUsers.Add(new AppUser { DisplayName = "Alice", Username = "alice" });
        db.SaveChanges();
        var task = SeedTask(db, owner: "Alice");
        SeedRule(db, AutomationTriggers.TaskAssigned, AutomationTriggers.ActionNotifyOwner, "You own {goal}");
        var svc = ServiceFactory.CreateAutomationService(db);

        await svc.RunForEventAsync(task, AutomationTriggers.TaskAssigned, Admin);

        var notif = Assert.Single(db.Notifications);
        Assert.Equal("You own Ship it", notif.Message);
        Assert.Equal(task.Id, notif.TaskId);
    }

    [Fact]
    public async Task RunForEvent_NotifyWatchers_CreatesNotificationForEachWatcher()
    {
        using var db = DbContextFactory.Create(nameof(RunForEvent_NotifyWatchers_CreatesNotificationForEachWatcher));
        db.AppUsers.Add(new AppUser { Id = 7, DisplayName = "Watcher", Username = "watcher" });
        db.SaveChanges();
        var task = SeedTask(db);
        db.TaskWatchers.Add(new TaskWatcher { TaskId = task.Id, UserId = 7, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
        SeedRule(db, AutomationTriggers.TaskStatusChanged, AutomationTriggers.ActionNotifyWatchers, "{goal} changed");
        var svc = ServiceFactory.CreateAutomationService(db);

        await svc.RunForEventAsync(task, AutomationTriggers.TaskStatusChanged, Admin);

        var notif = Assert.Single(db.Notifications);
        Assert.Equal(7, notif.UserId);
        Assert.Equal("Ship it changed", notif.Message);
    }

    [Fact]
    public async Task RunForEvent_ConditionNotMet_DoesNothing()
    {
        using var db = DbContextFactory.Create(nameof(RunForEvent_ConditionNotMet_DoesNothing));
        var task = SeedTask(db, priority: "Low");
        SeedRule(db, AutomationTriggers.TaskCreated, AutomationTriggers.ActionAddComment, "note",
            field: AutomationTriggers.FieldPriority, op: AutomationTriggers.OpEquals, value: "Critical");
        var svc = ServiceFactory.CreateAutomationService(db);

        await svc.RunForEventAsync(task, AutomationTriggers.TaskCreated, Admin);

        Assert.Empty(db.ActivityLog.Where(a => a.Action == "Comment"));
        Assert.Empty(db.AutomationRuleRuns);   // non-matches are not audited
    }

    [Fact]
    public async Task RunForEvent_DisabledRule_DoesNotFire()
    {
        using var db = DbContextFactory.Create(nameof(RunForEvent_DisabledRule_DoesNotFire));
        var task = SeedTask(db);
        SeedRule(db, AutomationTriggers.TaskCreated, AutomationTriggers.ActionAddComment, "note", enabled: false);
        var svc = ServiceFactory.CreateAutomationService(db);

        await svc.RunForEventAsync(task, AutomationTriggers.TaskCreated, Admin);

        Assert.Empty(db.AutomationRuleRuns);
    }

    [Fact]
    public async Task RunForEvent_WrongTriggerOrOtherTeam_DoesNotFire()
    {
        using var db = DbContextFactory.Create(nameof(RunForEvent_WrongTriggerOrOtherTeam_DoesNotFire));
        var task = SeedTask(db, teamId: 1);
        SeedRule(db, AutomationTriggers.TaskStatusChanged, AutomationTriggers.ActionAddComment, "note", teamId: 1); // wrong trigger
        SeedRule(db, AutomationTriggers.TaskCreated, AutomationTriggers.ActionAddComment, "note", teamId: 2);        // other team
        var svc = ServiceFactory.CreateAutomationService(db);

        await svc.RunForEventAsync(task, AutomationTriggers.TaskCreated, Admin);

        Assert.Empty(db.AutomationRuleRuns);
    }

    // ── Management ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_RejectsUnknownTriggerAndAction()
    {
        using var db = DbContextFactory.Create(nameof(Create_RejectsUnknownTriggerAndAction));
        var svc = ServiceFactory.CreateAutomationService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(new AutomationRuleInput(
            "bad", 1, "not.a.trigger", null, null, null, AutomationTriggers.ActionAddComment, "x", "t")));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(new AutomationRuleInput(
            "bad", 1, AutomationTriggers.TaskCreated, null, null, null, "detonate", "x", "t")));
    }

    [Fact]
    public async Task Create_RequiresValueForMutatingActions_and_NormalizesUnconditional()
    {
        using var db = DbContextFactory.Create(nameof(Create_RequiresValueForMutatingActions_and_NormalizesUnconditional));
        var svc = ServiceFactory.CreateAutomationService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(new AutomationRuleInput(
            "no value", 1, AutomationTriggers.TaskCreated, null, null, null, AutomationTriggers.ActionSetPriority, null, "t")));

        // No condition field supplied → normalised to the unconditional "Any"/"any".
        var rule = await svc.CreateAsync(new AutomationRuleInput(
            "notify", 1, AutomationTriggers.TaskCreated, null, null, null, AutomationTriggers.ActionNotifyOwner, "hi", "t"));
        Assert.Equal(AutomationTriggers.FieldAny, rule.ConditionField);
        Assert.Equal(AutomationTriggers.OpAny, rule.ConditionOperator);
        Assert.Equal(1, rule.SortOrder);
    }

    [Fact]
    public async Task SetEnabled_and_Delete_AreTeamScoped()
    {
        using var db = DbContextFactory.Create(nameof(SetEnabled_and_Delete_AreTeamScoped));
        var rule = SeedRule(db, AutomationTriggers.TaskCreated, AutomationTriggers.ActionAddComment, "note", teamId: 1);
        var svc = ServiceFactory.CreateAutomationService(db);

        // A different team cannot toggle or delete it.
        Assert.False(await svc.SetEnabledAsync(rule.Id, false, teamId: 2, isSysAdmin: false));
        Assert.False(await svc.DeleteAsync(rule.Id, teamId: 2, isSysAdmin: false));

        // Its own team can.
        Assert.True(await svc.SetEnabledAsync(rule.Id, false, teamId: 1, isSysAdmin: false));
        db.Entry(rule).Reload();
        Assert.False(rule.IsEnabled);
        Assert.True(await svc.DeleteAsync(rule.Id, teamId: 1, isSysAdmin: false));
        Assert.Empty(db.AutomationRules);
    }

    // ── Dry-run / preview ─────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_ReportsMatchAndActionWithoutMutating()
    {
        using var db = DbContextFactory.Create(nameof(Preview_ReportsMatchAndActionWithoutMutating));
        var task = SeedTask(db, itemType: "Bug", priority: "Low");
        var rule = SeedRule(db, AutomationTriggers.TaskCreated, AutomationTriggers.ActionSetPriority, "High",
            field: AutomationTriggers.FieldItemType, op: AutomationTriggers.OpEquals, value: "Bug");
        var svc = ServiceFactory.CreateAutomationService(db);

        var result = await svc.PreviewAsync(rule.Id, task.Id, teamId: 1, isSysAdmin: false);

        Assert.True(result.Matched);
        Assert.Contains("High", result.Description);
        // Dry-run mutates nothing.
        db.Entry(task).Reload();
        Assert.Equal("Low", task.Priority);
        Assert.Empty(db.AutomationRuleRuns);
    }

    [Fact]
    public async Task Preview_ReportsNoMatch_WhenConditionFails()
    {
        using var db = DbContextFactory.Create(nameof(Preview_ReportsNoMatch_WhenConditionFails));
        var task = SeedTask(db, priority: "Low");
        var rule = SeedRule(db, AutomationTriggers.TaskCreated, AutomationTriggers.ActionAddComment, "note",
            field: AutomationTriggers.FieldPriority, op: AutomationTriggers.OpEquals, value: "Critical");
        var svc = ServiceFactory.CreateAutomationService(db);

        var result = await svc.PreviewAsync(rule.Id, task.Id, teamId: 1, isSysAdmin: false);
        Assert.True(result.TaskFound);
        Assert.False(result.Matched);
    }

    [Fact]
    public async Task Preview_IsTeamScoped()
    {
        using var db = DbContextFactory.Create(nameof(Preview_IsTeamScoped));
        var task = SeedTask(db, teamId: 1);
        var rule = SeedRule(db, AutomationTriggers.TaskCreated, AutomationTriggers.ActionAddLabel, "x", teamId: 1);
        var svc = ServiceFactory.CreateAutomationService(db);

        // A different team can't preview this team's rule.
        var byOther = await svc.PreviewAsync(rule.Id, task.Id, teamId: 2, isSysAdmin: false);
        Assert.False(byOther.RuleFound);
    }

    // ── Scheduled ("due soon") ────────────────────────────────────────────────

    [Fact]
    public async Task RunDueSoon_FiresForDueTask_AndIsIdempotentPerDay()
    {
        using var db = DbContextFactory.Create(nameof(RunDueSoon_FiresForDueTask_AndIsIdempotentPerDay));
        db.AppUsers.Add(new AppUser { DisplayName = "Alice", Username = "alice" });
        db.SaveChanges();
        var now = DateTime.UtcNow;
        var task = SeedTask(db, owner: "Alice");
        task.TargetDate = now.Date.AddDays(1);   // due tomorrow
        db.SaveChanges();
        SeedRule(db, AutomationTriggers.TaskDueSoon, AutomationTriggers.ActionNotifyOwner, "Due soon: {goal}");
        var svc = ServiceFactory.CreateAutomationService(db);

        Assert.Equal(1, await svc.RunDueSoonAsync(now));
        Assert.Single(db.Notifications);

        // A second sweep the same day must not re-remind.
        Assert.Equal(0, await svc.RunDueSoonAsync(now));
        Assert.Single(db.Notifications);
    }

    [Fact]
    public async Task RunDueSoon_SkipsDoneOutOfWindowAndOtherTeam()
    {
        using var db = DbContextFactory.Create(nameof(RunDueSoon_SkipsDoneOutOfWindowAndOtherTeam));
        var now = DateTime.UtcNow;

        var done = SeedTask(db, status: "Done", itemType: "Task"); done.TargetDate = now.Date.AddDays(1);
        var far  = SeedTask(db);                                    far.TargetDate  = now.Date.AddDays(5);
        var other = SeedTask(db, teamId: 2);                        other.TargetDate = now.Date.AddDays(1);
        db.SaveChanges();
        SeedRule(db, AutomationTriggers.TaskDueSoon, AutomationTriggers.ActionAddLabel, "due", teamId: 1);
        var svc = ServiceFactory.CreateAutomationService(db);

        Assert.Equal(0, await svc.RunDueSoonAsync(now));
        Assert.Empty(db.AutomationRuleRuns);
    }

    // ── Wiring ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TaskComment_FiresAutomationThroughWritePath()
    {
        using var db = DbContextFactory.Create(nameof(TaskComment_FiresAutomationThroughWritePath));
        var task = SeedTask(db);   // seeded directly so RowVersion is set (store-generated on SQL Server)
        SeedRule(db, AutomationTriggers.TaskCommentAdded, AutomationTriggers.ActionAddLabel, "triage");

        var automation = ServiceFactory.CreateAutomationService(db);
        var tasks = ServiceFactory.CreateTaskService(db, automation);

        var (ok, error) = await tasks.AddCommentAsync(task.Id, "please look", Admin);
        Assert.True(ok, error);

        db.Entry(task).Reload();
        Assert.Equal("triage", task.Labels);
        Assert.Contains(db.AutomationRuleRuns, r => r.Status == "Executed");
    }
}
