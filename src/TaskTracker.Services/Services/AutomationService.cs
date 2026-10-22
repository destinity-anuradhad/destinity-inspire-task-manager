using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

/// <summary>
/// Evaluates and runs team automation rules. Rules fire from the task write path on the same events
/// that drive webhooks; matching is a fixed field/operator/value test and actions come from a small
/// allow-list. A re-entrancy guard plus a per-event rule cap keep automation bounded — an action can
/// never cascade into another round of automation, and a runaway rule set can never stall a write.
/// </summary>
public sealed class AutomationService(AppDbContext db, ILogger<AutomationService> logger) : IAutomationService
{
    // Scoped per request; set for the duration of one event so an action's own writes can never
    // re-enter the engine. Belt-and-suspenders: actions are already chosen not to re-trigger events.
    private bool _isRunning;

    // ── Management ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AutomationRuleSummary>> ListAsync(int? teamId, bool includeGlobal, CancellationToken ct = default)
    {
        var query = db.AutomationRules.AsNoTracking();
        if (!includeGlobal)
        {
            if (teamId is null) return [];
            query = query.Where(r => r.TeamId == teamId);
        }

        var rules = await query
            .OrderByDescending(r => r.IsEnabled)
            .ThenBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToListAsync(ct);
        var teamNames = await ResolveTeamNamesAsync(rules.Select(r => r.TeamId), ct);

        return rules.Select(r => new AutomationRuleSummary(
            r.Id, r.Name, r.TeamId, teamNames.GetValueOrDefault(r.TeamId), r.IsEnabled,
            r.TriggerType, r.ConditionField, r.ConditionOperator, r.ConditionValue,
            r.ActionType, r.ActionConfig, r.RunCount, r.LastRunAt, r.CreatedAt, r.CreatedBy)).ToList();
    }

    public async Task<AutomationRule> CreateAsync(AutomationRuleInput input, CancellationToken ct = default)
    {
        var name = (input.Name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("A rule name is required.", nameof(input));
        if (!AutomationTriggers.IsValidTrigger(input.TriggerType))
            throw new ArgumentException("Unknown trigger type.", nameof(input));
        if (!AutomationTriggers.IsValidAction(input.ActionType))
            throw new ArgumentException("Unknown action type.", nameof(input));

        // Normalise the condition: default to the unconditional "any" unless a real field is given.
        var field = input.ConditionField;
        var op    = input.ConditionOperator;
        var value = input.ConditionValue?.Trim();
        if (string.IsNullOrEmpty(field) || field == AutomationTriggers.FieldAny)
        {
            field = AutomationTriggers.FieldAny;
            op    = AutomationTriggers.OpAny;
            value = null;
        }
        else
        {
            if (!AutomationTriggers.IsValidField(field)) throw new ArgumentException("Unknown condition field.", nameof(input));
            if (string.IsNullOrEmpty(op) || op == AutomationTriggers.OpAny) op = AutomationTriggers.OpEquals;
            if (!AutomationTriggers.IsValidOperator(op)) throw new ArgumentException("Unknown condition operator.", nameof(input));
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("A condition value is required for the chosen field.", nameof(input));
        }

        // set_priority / add_label / notify text carry a required parameter for the mutating actions.
        var actionConfig = input.ActionConfig?.Trim();
        if (input.ActionType is AutomationTriggers.ActionSetPriority or AutomationTriggers.ActionAddLabel
            && string.IsNullOrEmpty(actionConfig))
            throw new ArgumentException("This action needs a value (the priority or label to apply).", nameof(input));

        var nextOrder = (await db.AutomationRules
            .Where(r => r.TeamId == input.TeamId)
            .MaxAsync(r => (int?)r.SortOrder, ct) ?? 0) + 1;

        var rule = new AutomationRule
        {
            TeamId = input.TeamId, Name = name, IsEnabled = true,
            TriggerType = input.TriggerType,
            ConditionField = field, ConditionOperator = op, ConditionValue = value,
            ActionType = input.ActionType, ActionConfig = actionConfig,
            SortOrder = nextOrder, CreatedAt = DateTime.UtcNow, CreatedBy = input.CreatedBy,
        };
        db.AutomationRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task<bool> SetEnabledAsync(int id, bool enabled, int? teamId, bool isSysAdmin, CancellationToken ct = default)
    {
        var rule = await FindOwnedAsync(id, teamId, isSysAdmin, ct);
        if (rule == null) return false;
        rule.IsEnabled = enabled;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, int? teamId, bool isSysAdmin, CancellationToken ct = default)
    {
        var rule = await FindOwnedAsync(id, teamId, isSysAdmin, ct);
        if (rule == null) return false;
        db.AutomationRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<AutomationRunSummary>> GetRecentRunsAsync(
        int? teamId, bool includeGlobal, int maxCount = 100, CancellationToken ct = default)
    {
        var rules = db.AutomationRules.AsNoTracking();
        if (!includeGlobal)
        {
            if (teamId is null) return [];
            rules = rules.Where(r => r.TeamId == teamId);
        }

        var query =
            from run in db.AutomationRuleRuns.AsNoTracking()
            join rule in rules on run.RuleId equals rule.Id
            orderby run.Id descending
            select new AutomationRunSummary(
                run.Id, run.RuleId, rule.Name, run.TaskId, run.EventType,
                run.Status, run.Detail, run.OccurredAt, run.OccurredBy);

        return await query.Take(Math.Clamp(maxCount, 1, 500)).ToListAsync(ct);
    }

    // ── Execution ───────────────────────────────────────────────────────────

    public async Task RunForEventAsync(TaskItem task, string eventType, UserCtx ctx, CancellationToken ct = default)
    {
        if (_isRunning) return;                                  // re-entrancy guard
        if (task.TeamId is not int teamId) return;               // only team-scoped work fires rules
        if (!AutomationTriggers.IsValidTrigger(eventType)) return;

        List<AutomationRule> rules;
        try
        {
            rules = await db.AutomationRules
                .Where(r => r.TeamId == teamId && r.IsEnabled && r.TriggerType == eventType)
                .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
                .Take(AutomationTriggers.MaxRulesPerEvent)
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Automation lookup failed for {EventType} (team {TeamId}).", eventType, teamId);
            db.ChangeTracker.Clear();
            return;
        }
        if (rules.Count == 0) return;

        _isRunning = true;
        try
        {
            var acted = false;
            foreach (var rule in rules)
            {
                if (!Matches(rule, task)) continue;              // non-matches aren't audited (too noisy)
                try
                {
                    var detail = await ExecuteActionAsync(rule, task, ctx, ct);
                    rule.RunCount++;
                    rule.LastRunAt = DateTime.UtcNow;
                    RecordRun(rule, task, eventType, "Executed", detail, ctx);
                    acted = true;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Automation rule {RuleId} failed on task {TaskId}.", rule.Id, task.Id);
                    RecordRun(rule, task, eventType, "Failed", ex.Message, ctx);
                    acted = true;
                }
            }

            if (acted) await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Automation is best-effort; never surface its failure to the originating command.
            logger.LogError(ex, "Automation run failed for {EventType} on task {TaskId}.", eventType, task.Id);
            db.ChangeTracker.Clear();
        }
        finally
        {
            _isRunning = false;
        }
    }

    // ── Dry-run / preview ─────────────────────────────────────────────────────

    public async Task<AutomationPreviewResult> PreviewAsync(
        int ruleId, int taskId, int? teamId, bool isSysAdmin, CancellationToken ct = default)
    {
        var rule = await db.AutomationRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == ruleId, ct);
        if (rule == null || (!isSysAdmin && rule.TeamId != teamId))
            return new AutomationPreviewResult(false, false, false, null, null, "Rule not found.");

        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null || (!isSysAdmin && task.TeamId != teamId))
            return new AutomationPreviewResult(true, false, false, rule.Name, null,
                "Task not found in this team.");

        if (!Matches(rule, task))
            return new AutomationPreviewResult(true, true, false, rule.Name, task.Goal,
                $"Condition not met — {AutomationTriggers.Label(rule.ActionType)} would not run.");

        var description = await DescribeActionAsync(rule, task, ct);
        return new AutomationPreviewResult(true, true, true, rule.Name, task.Goal, description);
    }

    // Read-only mirror of ExecuteActionAsync — says what an action WOULD do, changing nothing.
    private async Task<string> DescribeActionAsync(AutomationRule rule, TaskItem task, CancellationToken ct) =>
        rule.ActionType switch
        {
            AutomationTriggers.ActionNotifyOwner    => await DescribeNotifyAsync(rule, task, ct),
            AutomationTriggers.ActionNotifyWatchers => await DescribeNotifyWatchersAsync(task, ct),
            AutomationTriggers.ActionAddComment     => $"Would add comment: \"{RenderMessage(rule, task)}\".",
            AutomationTriggers.ActionSetPriority => DescribeSetPriority(rule, task),
            AutomationTriggers.ActionAddLabel    => DescribeAddLabel(rule, task),
            _ => "Would do nothing (unknown action).",
        };

    private async Task<string> DescribeNotifyAsync(AutomationRule rule, TaskItem task, CancellationToken ct)
    {
        var ownerNames = SplitCsv(task.Owner);
        if (ownerNames.Count == 0) return "Would skip: task has no owner.";
        var users = await db.AppUsers.AsNoTracking().Where(u => ownerNames.Contains(u.DisplayName)).ToListAsync(ct);
        if (users.Count == 0) return "Would skip: owner is not a known user.";
        return $"Would notify {users.Count} owner(s): {string.Join(", ", users.Select(u => u.DisplayName))}.";
    }

    private async Task<string> DescribeNotifyWatchersAsync(TaskItem task, CancellationToken ct)
    {
        var count = await db.TaskWatchers.CountAsync(w => w.TaskId == task.Id, ct);
        return count == 0 ? "Would skip: task has no watchers." : $"Would notify {count} watcher(s).";
    }

    private static string DescribeSetPriority(AutomationRule rule, TaskItem task)
    {
        var priority = rule.ActionConfig?.Trim();
        if (string.IsNullOrEmpty(priority)) return "Would skip: no priority configured.";
        return string.Equals(task.Priority, priority, StringComparison.OrdinalIgnoreCase)
            ? $"No change: priority is already '{priority}'."
            : $"Would set priority '{task.Priority ?? "—"}' → '{priority}'.";
    }

    private static string DescribeAddLabel(AutomationRule rule, TaskItem task)
    {
        var label = rule.ActionConfig?.Trim();
        if (string.IsNullOrEmpty(label)) return "Would skip: no label configured.";
        return SplitCsv(task.Labels).Any(l => string.Equals(l, label, StringComparison.OrdinalIgnoreCase))
            ? $"No change: label '{label}' is already present."
            : $"Would add label '{label}'.";
    }

    // ── Scheduled execution ─────────────────────────────────────────────────

    // No real user drives a scheduled sweep; actions and audit rows are attributed to the system.
    private static readonly UserCtx SystemCtx = new(0, "automation", "automation", false, "", null);

    public Task<int> RunScheduledAsync(CancellationToken ct = default) =>
        RunDueSoonAsync(DateTime.UtcNow, ct);

    /// <summary>
    /// Evaluates every enabled <c>task.due_soon</c> rule against the tasks whose target date falls in
    /// the "due soon" window and that aren't done. De-duplicates per (rule, task) per day so a task
    /// due tomorrow is reminded once, not once per sweep. <paramref name="nowUtc"/> is injectable so
    /// the window is deterministic under test.
    /// </summary>
    public async Task<int> RunDueSoonAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var windowStart = nowUtc.Date;
        var windowEnd   = windowStart.AddDays(AutomationTriggers.DueSoonWindowDays);

        var rules = await db.AutomationRules
            .Where(r => r.IsEnabled && r.TriggerType == AutomationTriggers.TaskDueSoon)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToListAsync(ct);
        if (rules.Count == 0) return 0;

        var teamIds = rules.Select(r => r.TeamId).Distinct().ToList();
        var candidates = await db.Tasks
            .Where(t => t.TeamId != null && teamIds.Contains(t.TeamId.Value)
                     && t.TargetDate != null
                     && t.TargetDate >= windowStart && t.TargetDate <= windowEnd)
            .ToListAsync(ct);
        // "Done" is item-type dependent, so filter in memory rather than in SQL.
        candidates = candidates.Where(t => !TaskStatusRules.IsDone(t.ItemType, t.Status)).ToList();
        if (candidates.Count == 0) return 0;

        var actions = 0;
        foreach (var rule in rules)
        {
            foreach (var task in candidates.Where(t => t.TeamId == rule.TeamId))
            {
                if (!Matches(rule, task)) continue;

                // Once per rule/task/day — don't re-remind on every hourly sweep.
                var alreadyRan = await db.AutomationRuleRuns.AnyAsync(
                    r => r.RuleId == rule.Id && r.TaskId == task.Id && r.OccurredAt >= windowStart, ct);
                if (alreadyRan) continue;

                try
                {
                    var detail = await ExecuteActionAsync(rule, task, SystemCtx, ct);
                    rule.RunCount++;
                    rule.LastRunAt = nowUtc;
                    RecordRun(rule, task, AutomationTriggers.TaskDueSoon, "Executed", detail, SystemCtx);
                    actions++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Scheduled automation rule {RuleId} failed on task {TaskId}.", rule.Id, task.Id);
                    RecordRun(rule, task, AutomationTriggers.TaskDueSoon, "Failed", ex.Message, SystemCtx);
                    actions++;
                }
            }
        }

        if (actions > 0) await db.SaveChangesAsync(ct);
        return actions;
    }

    // ── Matching ────────────────────────────────────────────────────────────

    private static bool Matches(AutomationRule rule, TaskItem task)
    {
        var op = rule.ConditionOperator ?? AutomationTriggers.OpAny;
        if (op == AutomationTriggers.OpAny
            || rule.ConditionField is null or AutomationTriggers.FieldAny)
            return true;

        var actual   = FieldValue(rule.ConditionField, task) ?? "";
        var expected = rule.ConditionValue ?? "";
        return op switch
        {
            AutomationTriggers.OpEquals    => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            AutomationTriggers.OpNotEquals => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            AutomationTriggers.OpContains  => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }

    private static string? FieldValue(string field, TaskItem task) => field switch
    {
        AutomationTriggers.FieldPriority => task.Priority,
        AutomationTriggers.FieldStatus   => task.Status,
        AutomationTriggers.FieldItemType => task.ItemType,
        AutomationTriggers.FieldOwner    => task.Owner,
        _ => null,
    };

    // ── Actions ─────────────────────────────────────────────────────────────

    private async Task<string> ExecuteActionAsync(AutomationRule rule, TaskItem task, UserCtx ctx, CancellationToken ct) =>
        rule.ActionType switch
        {
            AutomationTriggers.ActionNotifyOwner    => await NotifyOwnersAsync(rule, task, ct),
            AutomationTriggers.ActionNotifyWatchers => await NotifyWatchersAsync(rule, task, ct),
            AutomationTriggers.ActionAddComment     => AddSystemComment(rule, task),
            AutomationTriggers.ActionSetPriority    => SetPriority(rule, task),
            AutomationTriggers.ActionAddLabel       => AddLabel(rule, task),
            _ => "No-op: unknown action.",
        };

    private async Task<string> NotifyOwnersAsync(AutomationRule rule, TaskItem task, CancellationToken ct)
    {
        var ownerNames = SplitCsv(task.Owner);
        if (ownerNames.Count == 0) return "Skipped: task has no owner.";

        var users = await db.AppUsers.Where(u => ownerNames.Contains(u.DisplayName)).ToListAsync(ct);
        if (users.Count == 0) return "Skipped: owner is not a known user.";

        var message = RenderMessage(rule, task);
        foreach (var user in users)
            db.Notifications.Add(new Notification
            {
                UserId = user.Id, TaskId = task.Id, Message = message, CreatedAt = DateTime.Now,
            });
        return $"Notified {users.Count} owner(s).";
    }

    private async Task<string> NotifyWatchersAsync(AutomationRule rule, TaskItem task, CancellationToken ct)
    {
        var watchers = await db.TaskWatchers
            .Where(w => w.TaskId == task.Id)
            .Join(db.AppUsers, w => w.UserId, u => u.Id, (w, u) => u)
            .ToListAsync(ct);
        if (watchers.Count == 0) return "Skipped: task has no watchers.";

        var message = RenderMessage(rule, task);
        foreach (var user in watchers)
            db.Notifications.Add(new Notification
            {
                UserId = user.Id, TaskId = task.Id, Message = message, CreatedAt = DateTime.Now,
            });
        return $"Notified {watchers.Count} watcher(s).";
    }

    private string AddSystemComment(AutomationRule rule, TaskItem task)
    {
        var text = string.IsNullOrWhiteSpace(rule.ActionConfig)
            ? $"Automation '{rule.Name}' ran."
            : RenderMessage(rule, task);
        db.ActivityLog.Add(new ActivityLog
        {
            TaskId = task.Id, TeamId = task.TeamId, TaskGoal = task.Goal ?? "", Project = task.Project ?? "",
            ChangedBy = "automation", Action = "Comment", Category = "Task", Details = text,
            ChangedAt = DateTime.Now,
        });
        return "Added comment.";
    }

    private static string SetPriority(AutomationRule rule, TaskItem task)
    {
        var priority = rule.ActionConfig?.Trim();
        if (string.IsNullOrEmpty(priority)) return "Skipped: no priority configured.";
        if (string.Equals(task.Priority, priority, StringComparison.OrdinalIgnoreCase))
            return $"No change: priority already '{priority}'.";
        task.Priority = priority;
        return $"Priority set to '{priority}'.";
    }

    private static string AddLabel(AutomationRule rule, TaskItem task)
    {
        var label = rule.ActionConfig?.Trim();
        if (string.IsNullOrEmpty(label)) return "Skipped: no label configured.";
        var labels = SplitCsv(task.Labels);
        if (labels.Any(l => string.Equals(l, label, StringComparison.OrdinalIgnoreCase)))
            return $"No change: label '{label}' already present.";
        labels.Add(label);
        task.Labels = string.Join(", ", labels);
        return $"Added label '{label}'.";
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void RecordRun(AutomationRule rule, TaskItem task, string eventType, string status, string? detail, UserCtx ctx) =>
        db.AutomationRuleRuns.Add(new AutomationRuleRun
        {
            RuleId = rule.Id, TaskId = task.Id, EventType = eventType, Status = status,
            Detail = detail is { Length: > 1000 } ? detail[..1000] : detail,
            OccurredAt = DateTime.UtcNow, OccurredBy = ctx.DisplayName,
        });

    private static string RenderMessage(AutomationRule rule, TaskItem task)
    {
        var template = string.IsNullOrWhiteSpace(rule.ActionConfig)
            ? $"Automation '{rule.Name}': {task.Goal}"
            : rule.ActionConfig;
        return template
            .Replace("{goal}",     task.Goal ?? "",     StringComparison.OrdinalIgnoreCase)
            .Replace("{status}",   task.Status ?? "",   StringComparison.OrdinalIgnoreCase)
            .Replace("{priority}", task.Priority ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{owner}",    task.Owner ?? "",    StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> SplitCsv(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private async Task<AutomationRule?> FindOwnedAsync(int id, int? teamId, bool isSysAdmin, CancellationToken ct)
    {
        var rule = await db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule == null) return null;
        if (!isSysAdmin && rule.TeamId != teamId) return null;   // scoped: only touch your team's rules
        return rule;
    }

    private async Task<Dictionary<int, string>> ResolveTeamNamesAsync(IEnumerable<int> teamIds, CancellationToken ct)
    {
        var ids = teamIds.Distinct().ToList();
        return await db.Teams.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
    }
}
