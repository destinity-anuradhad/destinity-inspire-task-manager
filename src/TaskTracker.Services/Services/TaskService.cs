using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text;
using System.Text.Json;
using TaskTracker.Data;
using TaskTracker.Hubs;
using TaskTracker.Models;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services;

public partial class TaskService(AppDbContext db, IConfiguration config, IHubContext<TaskHub> hub, IEmailQueue emailQueue,
    ILogger<TaskService> logger, IMemoryCache cache, IWebhookService webhooks, IAutomationService automations) : ITaskService
{
    private readonly AppDbContext         _db          = db;
    private readonly IConfiguration      _config      = config;
    private readonly IHubContext<TaskHub> _hub         = hub;
    private readonly IEmailQueue         _emailQueue  = emailQueue;
    private readonly ILogger<TaskService> _log        = logger;
    private readonly IMemoryCache        _cache       = cache;
    private readonly IWebhookService     _webhooks    = webhooks;
    private readonly IAutomationService  _automations = automations;

    private static readonly TimeSpan _lookupTtl = TimeSpan.FromMinutes(5);

    // ── Permission / scope helpers ─────────────────────────────────────────────

    private static IEnumerable<string> SplitOwners(string? owner) =>
        (owner ?? "").Split(',').Select(o => o.Trim()).Where(o => o.Length > 0);

    private async Task<bool> CanModifyAsync(TaskItem task, UserCtx ctx)
    {
        if (ctx.IsTeamAdmin) return true;
        if (string.Equals(task.CreatedBy, ctx.DisplayName, StringComparison.OrdinalIgnoreCase)) return true;
        return ctx.UserId > 0 && await _db.TaskOwners.AnyAsync(o =>
            o.TaskId == task.Id && o.UserId == ctx.UserId);
    }

    private async Task<List<TaskItem>> GetModifiableTasksAsync(IEnumerable<TaskItem> tasks, UserCtx ctx)
    {
        var candidates = tasks.ToList();
        if (ctx.IsTeamAdmin) return candidates;
        if (ctx.UserId <= 0)
            return candidates.Where(t => string.Equals(t.CreatedBy, ctx.DisplayName, StringComparison.OrdinalIgnoreCase)).ToList();

        var taskIds = candidates.Select(t => t.Id).ToList();
        var ownedTaskIds = await _db.TaskOwners
            .Where(o => o.UserId == ctx.UserId && taskIds.Contains(o.TaskId))
            .Select(o => o.TaskId)
            .ToListAsync();
        var owned = ownedTaskIds.ToHashSet();
        return candidates.Where(t => owned.Contains(t.Id)
            || string.Equals(t.CreatedBy, ctx.DisplayName, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static bool CanAssign(UserCtx ctx) => ctx.IsTeamLead;  // SysAdmin, TeamAdmin, TeamLead only

    // Resolves owner display names to users, preserving input order and requiring each name to match
    // exactly one active application user. The single point where an owner string becomes authoritative
    // ids — everything downstream uses these, not the raw string.
    private async Task<List<(int Id, string DisplayName)>> ResolveTaskOwnersAsync(string? ownerNames)
    {
        var requestedNames = SplitOwners(ownerNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedNames.Count == 0) return [];

        var matches = await _db.AppUsers
                .Where(u => requestedNames.Contains(u.DisplayName))
                .Select(u => new { u.Id, u.DisplayName })
                .ToListAsync();

        var resolved = new List<(int, string)>();
        foreach (var requestedName in requestedNames)   // preserve the caller's owner order
        {
            var found = matches
                .Where(u => string.Equals(u.DisplayName, requestedName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (found.Count != 1)
                throw new InvalidOperationException(
                    $"Owner '{requestedName}' must match exactly one active application user.");
            resolved.Add((found[0].Id, found[0].DisplayName));
        }
        return resolved;
    }

    private async Task SyncTaskOwnersAsync(int taskId, string? ownerNames)
    {
        var resolved = await ResolveTaskOwnersAsync(ownerNames);
        var requestedUserIds = resolved.Select(r => r.Id).ToList();

        var existing = await _db.TaskOwners
            .Where(o => o.TaskId == taskId)
            .ToListAsync();

        _db.TaskOwners.RemoveRange(existing.Where(o => !requestedUserIds.Contains(o.UserId)));
        var existingUserIds = existing.Select(o => o.UserId).ToHashSet();
        _db.TaskOwners.AddRange(requestedUserIds
            .Where(userId => !existingUserIds.Contains(userId))
            .Select(userId => new TaskOwner { TaskId = taskId, UserId = userId }));

        // #7 stage 2: the Owner display string is now *derived* from the resolved owners — canonical
        // display names, in the caller's order, de-duplicated. The junction is authoritative and the
        // string is a cache kept in lock-step, so the two can never drift (reconciliation-clean by
        // construction).
        var task = await _db.Tasks.FindAsync(taskId);
        if (task != null)
            task.Owner = resolved.Count == 0 ? null : string.Join(", ", resolved.Select(r => r.DisplayName));
    }

    private IQueryable<TaskItem> TeamTasks(int? teamId)
    {
        var q = _db.Tasks.AsQueryable();
        if (teamId.HasValue) q = q.Where(t => t.TeamId == teamId);
        return q;
    }

    private async Task<TaskItem?> FindTaskInTeam(int id, int? teamId)
    {
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task == null) return null;
        return teamId.HasValue && task.TeamId != teamId ? null : task;
    }

    private async Task<Sprint?> FindSprintInTeam(int id, int? teamId)
    {
        var sprint = await _db.Sprints.FindAsync(id);
        if (sprint == null) return null;
        return teamId.HasValue && sprint.TeamId != teamId ? null : sprint;
    }

    private IQueryable<Sprint> TeamSprints(int? teamId)
    {
        var q = _db.Sprints.AsQueryable();
        if (teamId.HasValue) q = q.Where(s => s.TeamId == teamId);
        return q;
    }

    private async Task<string?> GetTeamNameAsync(int? teamId) =>
        teamId.HasValue
            ? await _db.Teams.Where(t => t.Id == teamId.Value).Select(t => t.Name).FirstOrDefaultAsync()
            : null;

    private async Task LogAsync(UserCtx ctx, string action, string details,
        int? taskId = null, string? taskGoal = null, string? project = null)
    {
        try
        {
            int? teamId = null;
            if (taskId.HasValue)
                teamId = await _db.Tasks.Where(t => t.Id == taskId.Value)
                    .Select(t => (int?)t.TeamId).FirstOrDefaultAsync();
            _db.ActivityLog.Add(new ActivityLog {
                TaskId    = taskId,
                TeamId    = teamId,
                TaskGoal  = taskGoal  ?? "",
                Project   = project   ?? "",
                ChangedBy = ctx.DisplayName,
                Action    = action,
                Category  = "Task",
                Details   = details,
                IpAddress = ctx.IpAddress,
                UserAgent = ctx.UserAgent,
                ChangedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex) { _log.LogError(ex, "Activity log write failed: {Action} on task {TaskId}", action, taskId); }
    }

    // ── Cascade helpers ────────────────────────────────────────────────────────

    private static List<TaskItem> GetAllDescendants(List<TaskItem> all, int parentId)
    {
        var result  = new List<TaskItem>();
        var queue   = new Queue<int>();
        var visited = new HashSet<int> { parentId };
        queue.Enqueue(parentId);
        while (queue.Count > 0)
        {
            var current  = queue.Dequeue();
            var children = all.Where(t => t.ParentId == current).ToList();
            result.AddRange(children);
            foreach (var child in children)
            {
                if (visited.Add(child.Id)) queue.Enqueue(child.Id);
            }
        }
        return result;
    }

    private async Task<bool> WouldCreateCycleAsync(int taskId, int? parentId)
    {
        if (parentId == null) return false;
        var current = parentId.Value;
        var visited = new HashSet<int>();
        while (true)
        {
            if (current == taskId) return true;
            if (!visited.Add(current)) return true; // already-seen node = existing cycle
            var ancestor = await _db.Tasks.Where(t => t.Id == current).Select(t => t.ParentId).FirstOrDefaultAsync();
            if (ancestor == null) return false;
            current = ancestor.Value;
        }
    }

    private async Task<int> CountOpenChildrenAsync(int parentId)
    {
        var children = await _db.Tasks.Where(c => c.ParentId == parentId && !c.IsDeleted).ToListAsync();
        var open = 0;
        foreach (var child in children)
            if (!await IsDoneAsync(child)) open++;
        return open;
    }

    private async Task<bool> IsDoneAsync(TaskItem task)
    {
        var workflow = await ResolveWorkflowAsync(task);
        if (workflow != null)
        {
            var state = workflow.States.FirstOrDefault(s =>
                string.Equals(s.Name, task.Status, StringComparison.OrdinalIgnoreCase));
            if (state != null) return state.IsTerminal;
        }
        return TaskStatusRules.IsDone(task.ItemType, task.Status);
    }

    private static readonly HashSet<string> SupportedLinkTypes = new(StringComparer.OrdinalIgnoreCase) {
        "Related", "Blocks", "Blocked By", "Duplicate", "Predecessor", "Successor"
    };

    private static bool TryGetBlockingDirection(TaskLink link, out int blockerId, out int blockedId)
    {
        switch (link.LinkType)
        {
            case "Blocks":
            case "Predecessor":
                blockerId = link.FromTaskId;
                blockedId = link.ToTaskId;
                return true;
            case "Blocked By":
            case "Successor":
                blockerId = link.ToTaskId;
                blockedId = link.FromTaskId;
                return true;
            default:
                blockerId = blockedId = 0;
                return false;
        }
    }

    private async Task<bool> HasOpenBlockingDependenciesAsync(TaskItem task)
    {
        var links = await _db.TaskLinks
            .Where(l => l.FromTaskId == task.Id || l.ToTaskId == task.Id)
            .ToListAsync();
        var blockerIds = links
            .Where(l => TryGetBlockingDirection(l, out _, out var blockedId) && blockedId == task.Id)
            .Select(l => { TryGetBlockingDirection(l, out var blockerId, out _); return blockerId; })
            .Distinct()
            .ToList();
        if (blockerIds.Count == 0) return false;

        var blockers = await TeamTasks(task.TeamId).Where(t => blockerIds.Contains(t.Id)).ToListAsync();
        foreach (var blocker in blockers)
            if (!await IsDoneAsync(blocker)) return true;
        return false;
    }

    private async Task<bool> WouldCreateDependencyCycleAsync(int blockerId, int blockedId, int? teamId)
    {
        if (blockerId == blockedId) return true;
        var teamTaskIds = await TeamTasks(teamId).Select(t => t.Id).ToListAsync();
        var teamIds = teamTaskIds.ToHashSet();
        var links = await _db.TaskLinks.ToListAsync();
        var adjacency = new Dictionary<int, List<int>>();
        foreach (var link in links)
        {
            if (!TryGetBlockingDirection(link, out var from, out var to)
                || !teamIds.Contains(from) || !teamIds.Contains(to)) continue;
            if (!adjacency.TryGetValue(from, out var next)) adjacency[from] = next = [];
            next.Add(to);
        }

        var seen = new HashSet<int> { blockedId };
        var queue = new Queue<int>();
        queue.Enqueue(blockedId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == blockerId) return true;
            if (!adjacency.TryGetValue(current, out var next)) continue;
            foreach (var id in next)
                if (seen.Add(id)) queue.Enqueue(id);
        }
        return false;
    }

    private async Task<string?> ValidateParentAssignmentAsync(
        int taskId, int? parentId, string? childType, int? teamId)
    {
        if (!parentId.HasValue) return null;
        if (parentId.Value == taskId)
            return "A work item cannot be its own parent.";

        var parent = await FindTaskInTeam(parentId.Value, teamId);
        if (parent == null)
            return "Parent work item was not found in the active team.";
        if (!TaskHierarchyRules.CanParentContain(parent.ItemType, childType))
            return $"A {parent.ItemType ?? "parent"} cannot contain a {childType ?? "work item"}.";
        if (taskId != 0 && await WouldCreateCycleAsync(taskId, parentId))
            return "Cannot set parent: it would create a circular task hierarchy.";
        return null;
    }

    private async Task CascadeParentStatusAsync(int? parentId, UserCtx ctx)
    {
        if (parentId == null) return;
        var parent = await _db.Tasks.FindAsync(parentId.Value);
        if (parent == null) return;

        var children = await _db.Tasks.Where(t => t.ParentId == parentId && !t.IsDeleted).ToListAsync();
        if (!children.Any()) return;

        string? autoStatus = null;
        var allChildrenDone = true;
        foreach (var child in children)
        {
            if (await IsDoneAsync(child)) continue;
            allChildrenDone = false;
            break;
        }

        if (allChildrenDone)
        {
            autoStatus = TaskStatusRules.CascadeAutoStatus(parent.ItemType);
        }
        else if (TaskStatusRules.IsDone(parent.ItemType, parent.Status))
        {
            // A reopened child means the parent is no longer complete.  This is
            // a system-derived transition and intentionally clears completion time.
            autoStatus = "Active";
        }
        else if (parent.Status == "New" && children.Any(c => TaskStatusRules.IsStarted(c.Status)))
        {
            autoStatus = "Active";
        }

        if (autoStatus == null || autoStatus == parent.Status) return;

        var prevStatus = parent.Status;
        parent.Status = autoStatus;

        if (TaskStatusRules.IsTerminal(parent.ItemType, autoStatus) && !TaskStatusRules.IsTerminal(parent.ItemType, prevStatus))
            parent.ActualEndDate = DateTime.Now;
        else if (TaskStatusRules.IsDone(parent.ItemType, prevStatus) && !TaskStatusRules.IsDone(parent.ItemType, autoStatus))
            parent.ActualEndDate = null;

        await _db.SaveChangesAsync();

        await LogAsync(ctx, "Auto-Updated",
            autoStatus == "Active"
                ? "Status auto-set to 'Active' — a child item was started or reopened"
                : $"Status auto-set to '{autoStatus}' via child completion cascade",
            parent.Id, parent.Goal, parent.Project);

        await _hub.Clients.Group("team-" + (parent.TeamId?.ToString() ?? "0")).SendAsync("TaskUpdated", new {
            id        = parent.Id,
            goal      = parent.Goal,
            status    = parent.Status,
            priority  = parent.Priority,
            owner     = parent.Owner,
            itemType  = parent.ItemType,
            sprintId  = parent.SprintId,
            updatedBy = "auto-cascade"
        });

        await CascadeParentStatusAsync(parent.ParentId, ctx);
    }
}
