using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Hubs;
using TaskTracker.Models;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services;

public partial class TaskService
{
    // ── ITaskService: write methods ────────────────────────────────────────────

    public async Task<TaskSaveResult> SaveAsync(TaskItem task, UserCtx ctx, bool forceClose)
    {
        try
        {
            if (!TryNormalizeEstimateUnit(task.EstimateUnit, out var estimateUnit))
                return new TaskSaveResult { Error = "Estimate unit must be Points or Hours." };
            if (task.RemainingWorkHours < 0)
                return new TaskSaveResult { Error = "Remaining work cannot be negative." };
            task.EstimateUnit = estimateUnit;
            var projectError = await ResolveGovernedProjectAsync(task, ctx.TeamId);
            if (projectError != null) return new TaskSaveResult { Error = projectError };
            string action, details;
            string? capacityWarning = null;

            if (task.Id == 0)
            {
                // Auto-assign member as owner BEFORE the status guard so a member
                // can create a task directly in a started status.
                if (ctx.IsMember) task.Owner = ctx.DisplayName;

                var createStatus = await ApplyStatusChangeAsync(task, task.Status, ctx, forceClose, task.ActualHours,
                    isNewTask: true);
                if (!createStatus.Success)
                    return ToSaveResult(createStatus);

                task.CreatedAt = DateTime.Now;
                task.CreatedBy = ctx.DisplayName;
                task.Reporter  = ctx.DisplayName;
                task.TeamId    = ctx.TeamId;
                if (task.ItemType is "Epic" or "Feature") task.SprintId = null;
                var parentError = await ValidateParentAssignmentAsync(0, task.ParentId, task.ItemType, task.TeamId);
                if (parentError != null) return new TaskSaveResult { Error = parentError };
                await ResolveTaskOwnerUserIdsAsync(task.Owner);
                _db.Tasks.Add(task);
                await _db.SaveChangesAsync();
                RecordSprintMembershipEvent(task, null, task.SprintId, "Assigned", ctx.DisplayName);
                await SyncTaskOwnersAsync(task.Id, task.Owner);
                await _db.SaveChangesAsync();
                await CascadeParentStatusAsync(task.ParentId, ctx);
                action  = "Created";
                details = $"Goal: {task.Goal}";
                await NotifyAssignedAsync(task, null, ctx);
            }
            else
            {
                var existing = await FindTaskInTeam(task.Id, ctx.TeamId);
                if (existing == null) return new TaskSaveResult { Error = "Task not found" };
                if (!await CanModifyAsync(existing, ctx))
                    return new TaskSaveResult { Error = "You don't have permission to edit this task." };

                // Members cannot change the owner — resolve it before the status guard
                // so the guard sees the correct effective owner, not the empty form value.
                if (ctx.IsMember) task.Owner = existing.Owner;

                var changes    = new List<string>();
                var oldOwner   = existing.Owner;
                var oldStatus  = existing.Status;
                var oldGoal    = existing.Goal;
                var oldProject = existing.Project;
                if (existing.Goal     != task.Goal)     changes.Add("Goal");
                if (existing.Status   != task.Status)   changes.Add($"Status: '{existing.Status}' → '{task.Status}'");
                if (existing.Priority != task.Priority) changes.Add($"Priority: '{existing.Priority}' → '{task.Priority}'");
                if (existing.Owner    != task.Owner)    changes.Add($"Owner: '{existing.Owner}' → '{task.Owner}'");

                // The requested owner is part of the same form command, so use it
                // when deciding whether a started/completed status is valid.
                existing.Owner = task.Owner;
                var statusChange = await ApplyStatusChangeAsync(existing, task.Status, ctx, forceClose, task.ActualHours);
                if (!statusChange.Success)
                {
                    existing.Owner = oldOwner;
                    return ToSaveResult(statusChange);
                }

                existing.Goal = task.Goal; existing.Project = task.Project;
                if (task.ProjectId.HasValue) existing.ProjectId = task.ProjectId;
                else if (!string.Equals(oldProject, task.Project, StringComparison.Ordinal)) existing.ProjectId = null;
                existing.Property = task.Property; existing.Priority = task.Priority;
                existing.Owner = task.Owner;
                existing.EndDate = task.EndDate; existing.Notes = task.Notes;
                existing.Weight = task.Weight; existing.Labels = task.Labels;
                existing.EstimateUnit = task.EstimateUnit;
                existing.RemainingWorkHours = task.RemainingWorkHours;
                existing.AcceptanceCriteria = task.AcceptanceCriteria;
                existing.Swimlane = task.Swimlane;
                existing.ModuleId = task.ModuleId; existing.ClientId = task.ClientId;
                existing.ItemType = task.ItemType;
                var parentError = await ValidateParentAssignmentAsync(existing.Id, task.ParentId, task.ItemType, ctx.TeamId);
                if (parentError != null) return new TaskSaveResult { Error = parentError };
                existing.ParentId = task.ParentId;
                var oldSprintId = existing.SprintId;
                existing.SprintId = task.ItemType is "Epic" or "Feature" ? null : task.SprintId;
                if (existing.SprintId.HasValue && existing.SprintId != oldSprintId
                    && !existing.StartDate.HasValue)
                {
                    var sp = await _db.Sprints.FindAsync(existing.SprintId.Value);
                    if (sp?.StartDate != null)
                        existing.StartDate = sp.StartDate.Value;
                }
                if (existing.SprintId != oldSprintId)
                    RecordSprintMembershipEvent(existing, oldSprintId, existing.SprintId, "Reassigned", ctx.DisplayName);
                if (!string.IsNullOrWhiteSpace(task.Reporter)) existing.Reporter = task.Reporter;

                await SyncTaskOwnersAsync(existing.Id, existing.Owner);

                // Sync actual hours to per-sprint log when canvas form saves them
                if (task.ActualHours.HasValue && task.ActualHours > 0)
                    existing.ActualHours = task.ActualHours;
                if (existing.ActualHours.HasValue && existing.ActualHours > 0 && existing.SprintId.HasValue)
                    await UpsertSprintHoursLogAsync(existing.SprintId.Value, existing.Id,
                        existing.ActualHours.Value, ctx.DisplayName);

                await _db.SaveChangesAsync();

                // Keep ActivityLog denormalized names fresh when goal/project changes
                if (existing.Goal != oldGoal || existing.Project != oldProject)
                    await _db.ActivityLog
                        .Where(a => a.TaskId == existing.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(a => a.TaskGoal, existing.Goal ?? "")
                            .SetProperty(a => a.Project,  existing.Project ?? ""));

                await CascadeParentStatusAsync(existing.ParentId, ctx);
                action  = "Updated";
                details = changes.Count > 0 ? string.Join("; ", changes) : "Minor update";
                await NotifyAssignedAsync(existing, oldOwner, ctx);
                if (oldStatus != existing.Status)
                {
                    await NotifyWatchersStatusAsync(existing, oldStatus, existing.Status, ctx);
                    // Log a canonical "Status Changed" entry so GetBurndownAsync can count completions.
                    await LogAsync(ctx, "Status Changed", $"'{oldStatus}' → '{existing.Status}'",
                        existing.Id, existing.Goal, existing.Project);
                }

                if (existing.SprintId.HasValue && existing.SprintId != oldSprintId
                    && !string.IsNullOrWhiteSpace(existing.Owner))
                {
                    var used = await _db.Tasks
                        .Where(t => t.SprintId == existing.SprintId && !t.IsDeleted && t.Owner == existing.Owner
                            && t.EstimateUnit == "Hours")
                        .SumAsync(t => t.Weight ?? 0);
                    var cap = await _db.SprintCapacities
                        .Where(c => c.SprintId == existing.SprintId.Value && c.OwnerName == existing.Owner)
                        .Select(c => (decimal?)c.CapacityHours).FirstOrDefaultAsync();
                    if (cap.HasValue && cap.Value > 0 && used > cap.Value)
                        capacityWarning = $"{existing.Owner} is over capacity ({used:0.##}/{cap.Value:0.##}h)";
                }

                task = existing;
            }

            await LogAsync(ctx, action, details, task.Id, task.Goal, task.Project);
            await _hub.Clients.Group("team-" + (task.TeamId?.ToString() ?? "0")).SendAsync("TaskUpdated", new {
                id        = task.Id,
                goal      = task.Goal,
                status    = task.Status,
                priority  = task.Priority,
                owner     = task.Owner,
                itemType  = task.ItemType,
                sprintId  = task.SprintId,
                updatedBy = ctx.DisplayName
            });
            return new TaskSaveResult { Success = true, TaskId = task.Id, CapacityWarning = capacityWarning };
        }
        catch (DbUpdateConcurrencyException)
        {
            return new TaskSaveResult { Error = "This task was modified by someone else. Please reload and try again." };
        }
        catch (Exception ex) { return new TaskSaveResult { Error = ex.Message }; }
    }

    public async Task<TaskUpdateResult> UpdateStatusAsync(int id, string status, string? swimlane, UserCtx ctx, bool forceClose, decimal? actualHours = null)
    {
        var task = await FindTaskInTeam(id, ctx.TeamId);
        if (task == null) return new TaskUpdateResult { Error = "Not found" };
        if (!await CanModifyAsync(task, ctx))
            return new TaskUpdateResult { Error = "You don't have permission to change this task's status." };
        var old = task.Status;
        var statusChange = await ApplyStatusChangeAsync(task, status, ctx, forceClose, actualHours);
        if (!statusChange.Success)
            return ToUpdateResult(statusChange);
        if (task.ActualHours.HasValue && task.ActualHours > 0 && task.SprintId.HasValue)
            await UpsertSprintHoursLogAsync(task.SprintId.Value, task.Id, task.ActualHours.Value, ctx.DisplayName);
        if (swimlane != null)
            task.Swimlane = swimlane is "Default" or "" ? null : swimlane;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return new TaskUpdateResult { Error = "This task was modified by someone else. Please reload and try again." };
        }
        await CascadeParentStatusAsync(task.ParentId, ctx);
        if (old != task.Status) await NotifyWatchersStatusAsync(task, old, task.Status, ctx);
        var logDetails = $"'{old}' → '{task.Status}'" + (swimlane != null ? $"; Swimlane: {swimlane}" : "");
        await LogAsync(ctx, "Status Changed", logDetails, id, task.Goal, task.Project);
        await _hub.Clients.Group("team-" + (task.TeamId?.ToString() ?? "0")).SendAsync("TaskUpdated", new {
            id        = task.Id,
            goal      = task.Goal,
            status    = task.Status,
            swimlane  = task.Swimlane,
            priority  = task.Priority,
            owner     = task.Owner,
            itemType  = task.ItemType,
            sprintId  = task.SprintId,
            updatedBy = ctx.DisplayName
        });
        return new TaskUpdateResult { Success = true };
    }

    // All status mutations must pass through this command.  Keeping the policy and
    // side effects here prevents the full-edit, board, and bulk endpoints from
    // silently establishing different definitions of a completed work item.
    private async Task<StatusChangeResult> ApplyStatusChangeAsync(
        TaskItem task, string? requestedStatus, UserCtx ctx, bool forceClose,
        decimal? actualHours, bool isNewTask = false)
    {
        var status = requestedStatus?.Trim();
        if (string.IsNullOrEmpty(status))
            return new StatusChangeResult { Error = "A status is required." };

        var oldStatus = task.Status;
        var changesStatus = !string.Equals(oldStatus, status, StringComparison.Ordinal);
        var workflow = await ResolveWorkflowAsync(task);

        WorkflowState? oldWorkflowState = null;
        WorkflowState? targetWorkflowState = null;
        if (workflow != null)
        {
            targetWorkflowState = workflow.States.FirstOrDefault(s =>
                string.Equals(s.Name, status, StringComparison.OrdinalIgnoreCase));
            if (targetWorkflowState == null)
                return new StatusChangeResult {
                    Error = $"'{status}' is not a state in workflow '{workflow.Definition.Name}' v{workflow.Definition.Version}."
                };

            if (!isNewTask)
            {
                oldWorkflowState = workflow.States.FirstOrDefault(s =>
                    string.Equals(s.Name, oldStatus, StringComparison.OrdinalIgnoreCase));
                if (oldWorkflowState == null)
                    return new StatusChangeResult {
                        Error = $"Current status '{oldStatus}' is not in the task's pinned workflow. Migrate the task before changing its status."
                    };
                if (changesStatus && !ctx.IsTeamLead
                    && !workflow.AllowsMemberTransition(oldWorkflowState.Id, targetWorkflowState.Id))
                    return new StatusChangeResult {
                        Error = $"Cannot move '{oldStatus}' → '{status}' in workflow '{workflow.Definition.Name}' v{workflow.Definition.Version}."
                    };
            }
            else if (!targetWorkflowState.IsInitial)
            {
                var initial = workflow.States.SingleOrDefault(s => s.IsInitial);
                if (initial == null)
                    return new StatusChangeResult { Error = "The published workflow has no initial state." };
                return new StatusChangeResult {
                    Error = $"New work items must start in '{initial.Name}'."
                };
            }
        }

        if (workflow == null && !isNewTask && changesStatus && !ctx.IsTeamLead
            && !TaskStatusRules.IsTransitionAllowed(task.ItemType, oldStatus, status))
        {
            var allowed = TaskStatusRules.AllowedTransitions(task.ItemType, oldStatus);
            var hint = allowed.Count > 0 ? $" Allowed next: {string.Join(", ", allowed)}." : "";
            return new StatusChangeResult { Error = $"Cannot move '{oldStatus}' → '{status}'.{hint}" };
        }

        var requiresOwner = targetWorkflowState?.RequiresOwner ?? RequiresOwner(status);
        if (requiresOwner && string.IsNullOrWhiteSpace(task.Owner))
            return new StatusChangeResult {
                NeedOwner = true,
                Error = $"Assign an owner before moving this to {status}."
            };

        var movesToDone = targetWorkflowState?.IsTerminal
            ?? TaskStatusRules.IsDone(task.ItemType, status);
        var wasDone = oldWorkflowState?.IsTerminal
            ?? TaskStatusRules.IsDone(task.ItemType, oldStatus);
        var requiresActualHours = targetWorkflowState?.RequiresActualHours ?? movesToDone;
        if (movesToDone && !wasDone && requiresActualHours && (actualHours ?? task.ActualHours) is not > 0)
            return new StatusChangeResult {
                NeedActualHours = true,
                Error = "Enter actual hours before marking this as done."
            };

        if (movesToDone && !wasDone && !isNewTask && !forceClose)
        {
            var openCount = await CountOpenChildrenAsync(task.Id);
            if (openCount > 0)
                return new StatusChangeResult {
                    OpenChildren = openCount,
                    Error = $"Cannot close: {openCount} child item(s) are still open."
                };
        }
        if (movesToDone && !wasDone && !isNewTask && await HasOpenBlockingDependenciesAsync(task))
            return new StatusChangeResult {
                Error = "Cannot complete this work item while one or more blocking dependencies remain open."
            };

        task.Status = status;
        if (workflow != null)
            task.WorkflowDefinitionId = workflow.Definition.Id;
        if (actualHours is > 0)
            task.ActualHours = actualHours;

        if (changesStatus || isNewTask)
        {
            var now = DateTime.UtcNow;
            var isStarted = targetWorkflowState != null
                ? targetWorkflowState.Category is not "NotStarted"
                : TaskStatusRules.IsStarted(status);
            var wasStarted = oldWorkflowState != null
                ? oldWorkflowState.Category is not "NotStarted"
                : TaskStatusRules.IsStarted(oldStatus);
            if (isStarted && !wasStarted
                && !task.ActualStartDate.HasValue)
                task.ActualStartDate = now;

            if (movesToDone && !wasDone)
                task.ActualEndDate = now;
            else if (wasDone && !movesToDone)
                task.ActualEndDate = null;
        }

        return new StatusChangeResult { Success = true };
    }

    private static bool RequiresOwner(string status) =>
        status is "Active" or "In Progress" or "Resolved" or "Done" or "Closed";

    private async Task<WorkflowPolicy?> ResolveWorkflowAsync(TaskItem task)
    {
        WorkflowDefinition? definition;
        if (task.WorkflowDefinitionId.HasValue)
        {
            definition = await _db.WorkflowDefinitions
                .SingleOrDefaultAsync(w => w.Id == task.WorkflowDefinitionId.Value);
        }
        else
        {
            definition = await _db.WorkflowDefinitions
                .Where(w => w.IsPublished && w.ItemType == (task.ItemType ?? "Task")
                    && (w.TeamId == task.TeamId || w.TeamId == null))
                .OrderByDescending(w => w.TeamId == task.TeamId)
                .ThenByDescending(w => w.Version)
                .FirstOrDefaultAsync();
        }

        if (definition == null) return null;
        var states = await _db.WorkflowStates
            .Where(s => s.WorkflowDefinitionId == definition.Id)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();
        var transitions = await _db.WorkflowTransitions
            .Where(t => t.WorkflowDefinitionId == definition.Id)
            .ToListAsync();
        return new WorkflowPolicy(definition, states, transitions);
    }

    private static TaskSaveResult ToSaveResult(StatusChangeResult result) => new() {
        Error = result.Error,
        OpenChildren = result.OpenChildren
    };

    private static TaskUpdateResult ToUpdateResult(StatusChangeResult result) => new() {
        Error = result.Error,
        NeedOwner = result.NeedOwner,
        NeedActualHours = result.NeedActualHours,
        OpenChildren = result.OpenChildren
    };

    private sealed class StatusChangeResult
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public bool NeedOwner { get; init; }
        public bool NeedActualHours { get; init; }
        public int? OpenChildren { get; init; }
    }

    private sealed class WorkflowPolicy(
        WorkflowDefinition definition,
        List<WorkflowState> states,
        List<WorkflowTransition> transitions)
    {
        public WorkflowDefinition Definition { get; } = definition;
        public List<WorkflowState> States { get; } = states;
        public bool AllowsMemberTransition(int fromStateId, int toStateId) =>
            transitions.Any(t => t.FromStateId == fromStateId && t.ToStateId == toStateId && t.MemberCanTransition);
    }

    public async Task<TaskSprintResult> AssignSprintAsync(int taskId, int? sprintId, UserCtx ctx)
    {
        var task = await FindTaskInTeam(taskId, ctx.TeamId);
        if (task == null) return new TaskSprintResult { Error = "Task not found" };
        if (!await CanModifyAsync(task, ctx)) return new TaskSprintResult { Error = "You don't have permission to move this task." };
        if (task.ItemType is "Epic" or "Feature")
            return new TaskSprintResult { Error = "Epics and Features cannot be assigned to a sprint" };
        if (sprintId.HasValue && string.IsNullOrWhiteSpace(task.Owner))
            return new TaskSprintResult { Error = "Assign an owner to this task before adding it to a sprint." };

        var oldSprintId = task.SprintId;
        task.SprintId   = sprintId;
        Sprint? sprint  = null;
        if (sprintId.HasValue)
        {
            sprint = await FindSprintInTeam(sprintId.Value, ctx.TeamId);
            if (sprint == null) return new TaskSprintResult { Error = "Sprint not found." };
            if (sprint.StartDate != null && !task.StartDate.HasValue)
                task.StartDate = sprint.StartDate.Value;
        }

        if (oldSprintId != task.SprintId)
            RecordSprintMembershipEvent(task, oldSprintId, task.SprintId, "Reassigned", ctx.DisplayName);

        await _db.SaveChangesAsync();

        var oldName = oldSprintId.HasValue
            ? (await _db.Sprints.FindAsync(oldSprintId))?.Name ?? $"Sprint {oldSprintId}"
            : "Backlog";
        var newName = sprint?.Name ?? (sprintId.HasValue ? $"Sprint {sprintId}" : "Backlog");
        await LogAsync(ctx, "Sprint Changed", $"'{oldName}' → '{newName}'", taskId, task.Goal, task.Project);

        string? capacityWarning = null;
        if (sprintId.HasValue && !string.IsNullOrWhiteSpace(task.Owner))
        {
            var used = await _db.Tasks
                .Where(t => t.SprintId == sprintId && !t.IsDeleted && t.Owner == task.Owner
                    && t.EstimateUnit == "Hours")
                .SumAsync(t => t.Weight ?? 0);
            var cap = await _db.SprintCapacities
                .Where(c => c.SprintId == sprintId.Value && c.OwnerName == task.Owner)
                .Select(c => (decimal?)c.CapacityHours).FirstOrDefaultAsync();
            if (cap.HasValue && cap.Value > 0 && used > cap.Value)
                capacityWarning = $"{task.Owner} is over capacity ({used:0.##}/{cap.Value:0.##}h)";
        }
        return new TaskSprintResult { Success = true, SprintId = task.SprintId, CapacityWarning = capacityWarning };
    }

    public async Task<TimeEntryResult> LogTimeAsync(
        int taskId, decimal hours, DateTime workDate, string? note, decimal? remainingWorkHours, UserCtx ctx)
    {
        if (hours <= 0) return new TimeEntryResult { Error = "Logged hours must be greater than zero." };
        if (remainingWorkHours < 0) return new TimeEntryResult { Error = "Remaining work cannot be negative." };
        if (workDate.Date > DateTime.UtcNow.Date.AddDays(1))
            return new TimeEntryResult { Error = "Work date cannot be in the future." };

        var task = await FindTaskInTeam(taskId, ctx.TeamId);
        if (task == null) return new TimeEntryResult { Error = "Task not found." };
        if (!await CanModifyAsync(task, ctx))
            return new TimeEntryResult { Error = "You do not have permission to log time against this task." };

        var existingEntries = await _db.TimeEntries
            .Where(e => e.TaskId == task.Id)
            .ToListAsync();
        // Preserve an old mutable total as an explicit opening balance before
        // the first append-only entry is added to the task.
        if (existingEntries.Count == 0 && task.ActualHours is > 0)
        {
            _db.TimeEntries.Add(new TimeEntry {
                TaskId = task.Id, SprintId = task.SprintId, UserId = null,
                Hours = task.ActualHours.Value, WorkDate = task.CreatedAt.Date,
                EntryType = "LegacyOpeningBalance", Note = "Imported from legacy task total.",
                CreatedAt = DateTime.UtcNow, CreatedBy = "system"
            });
        }

        _db.TimeEntries.Add(new TimeEntry {
            TaskId = task.Id, SprintId = task.SprintId, UserId = ctx.UserId > 0 ? ctx.UserId : null,
            Hours = hours, WorkDate = workDate.Date, EntryType = "Logged", Note = note?.Trim(),
            CreatedAt = DateTime.UtcNow, CreatedBy = ctx.DisplayName
        });
        if (remainingWorkHours.HasValue) task.RemainingWorkHours = remainingWorkHours;

        await _db.SaveChangesAsync();
        var actualHours = await _db.TimeEntries.Where(e => e.TaskId == task.Id)
            .SumAsync(e => e.Hours);
        task.ActualHours = actualHours;
        if (task.SprintId.HasValue)
            await UpsertSprintHoursLogAsync(task.SprintId.Value, task.Id, actualHours, ctx.DisplayName);
        await _db.SaveChangesAsync();

        await LogAsync(ctx, "Time Logged", $"{hours:0.##}h on {workDate:yyyy-MM-dd}", task.Id, task.Goal, task.Project);
        return new TimeEntryResult {
            Success = true, ActualHours = actualHours, RemainingWorkHours = task.RemainingWorkHours
        };
    }

    public async Task<TimeEntryResult> CorrectTimeEntryAsync(long entryId, string? note, UserCtx ctx)
    {
        var entry = await _db.TimeEntries.FindAsync(entryId);
        if (entry == null) return new TimeEntryResult { Error = "Time entry not found." };
        if (entry.EntryType == "Correction" || entry.ReversesEntryId.HasValue
            || await _db.TimeEntries.AnyAsync(e => e.ReversesEntryId == entry.Id))
            return new TimeEntryResult { Error = "This time entry cannot be corrected again." };
        var task = await FindTaskInTeam(entry.TaskId, ctx.TeamId);
        if (task == null) return new TimeEntryResult { Error = "Task not found." };
        var canCorrect = ctx.IsTeamAdmin
            || (ctx.UserId > 0 && entry.UserId == ctx.UserId)
            || string.Equals(entry.CreatedBy, ctx.DisplayName, StringComparison.OrdinalIgnoreCase);
        if (!canCorrect) return new TimeEntryResult { Error = "You do not have permission to correct this time entry." };

        _db.TimeEntries.Add(new TimeEntry {
            TaskId = entry.TaskId, SprintId = entry.SprintId, UserId = ctx.UserId > 0 ? ctx.UserId : null,
            Hours = -entry.Hours, WorkDate = DateTime.UtcNow.Date, EntryType = "Correction",
            Note = note?.Trim() ?? $"Correction of entry {entry.Id}.", ReversesEntryId = entry.Id,
            CreatedAt = DateTime.UtcNow, CreatedBy = ctx.DisplayName
        });
        await _db.SaveChangesAsync();
        var actualHours = await _db.TimeEntries.Where(e => e.TaskId == task.Id).SumAsync(e => e.Hours);
        task.ActualHours = actualHours;
        if (task.SprintId.HasValue)
            await UpsertSprintHoursLogAsync(task.SprintId.Value, task.Id, actualHours, ctx.DisplayName);
        await _db.SaveChangesAsync();
        await LogAsync(ctx, "Time Corrected", $"Reversed time entry {entry.Id}.", task.Id, task.Goal, task.Project);
        return new TimeEntryResult {
            Success = true, ActualHours = actualHours, RemainingWorkHours = task.RemainingWorkHours
        };
    }

    public async Task<(bool Success, string? Error)> AddCommentAsync(int taskId, string comment, UserCtx ctx)
    {
        if (string.IsNullOrWhiteSpace(comment)) return (false, "Comment cannot be empty.");
        var task = await FindTaskInTeam(taskId, ctx.TeamId);
        if (task == null) return (false, "Task not found.");

        await LogAsync(ctx, "Comment", comment.Trim(), taskId, task.Goal, task.Project);
        await _hub.Clients.Group("task-" + taskId).SendAsync("CommentAdded", taskId);

        var commenter = ctx.DisplayName;
        var allUsers  = await _db.AppUsers.ToListAsync();
        var mentioned = allUsers
            .Where(u => !string.IsNullOrWhiteSpace(u.DisplayName)
                     && comment.Contains("@" + u.DisplayName, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(u.DisplayName, commenter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var user in mentioned)
            _db.Notifications.Add(new Notification {
                UserId = user.Id, TaskId = taskId,
                Message = $"{commenter} mentioned you on: {task.Goal}", CreatedAt = DateTime.Now
            });

        var mentionedIds   = mentioned.Select(m => m.Id).ToHashSet();
        var watcherTargets = (await GetWatcherUsersAsync(taskId))
            .Where(u => !mentionedIds.Contains(u.Id)
                     && !string.Equals(u.DisplayName, commenter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var user in watcherTargets)
            _db.Notifications.Add(new Notification {
                UserId = user.Id, TaskId = taskId,
                Message = $"{commenter} commented on watched task: {task.Goal}", CreatedAt = DateTime.Now
            });

        var notifiedIds = mentionedIds.Union(watcherTargets.Select(u => u.Id)).ToHashSet();
        foreach (var ownerName in SplitOwners(task.Owner))
        {
            if (string.Equals(ownerName, commenter, StringComparison.OrdinalIgnoreCase)) continue;
            var owner = allUsers.FirstOrDefault(u => string.Equals(u.DisplayName, ownerName, StringComparison.OrdinalIgnoreCase));
            if (owner == null || notifiedIds.Contains(owner.Id) || !owner.NotifyOnComment) continue;
            _db.Notifications.Add(new Notification {
                UserId = owner.Id, TaskId = taskId,
                Message = $"{commenter} commented on: {task.Goal}", CreatedAt = DateTime.Now
            });
        }

        var commentNotifIds = _db.ChangeTracker.Entries<Notification>()
            .Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added)
            .Select(e => e.Entity.UserId).ToList();
        await _db.SaveChangesAsync();
        await PushNotifBadgeAsync(commentNotifIds);
        var teamName = await GetTeamNameAsync(task.TeamId);
        await NotifyCommentAsync(task, comment.Trim(), commenter, allUsers, mentioned, watcherTargets, teamName);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> AddChecklistItemAsync(int taskId, string text, UserCtx ctx)
    {
        var task = await FindTaskInTeam(taskId, ctx.TeamId);
        if (task == null) return (false, "Task not found.");
        if (!await CanModifyAsync(task, ctx)) return (false, "You don't have permission to edit this task.");
        var value = text?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return (false, "Checklist item cannot be empty.");
        if (value.Length > 500) return (false, "Checklist item cannot exceed 500 characters.");
        var order = (await _db.TaskChecklistItems.Where(i => i.TaskId == taskId).MaxAsync(i => (int?)i.SortOrder) ?? -1) + 1;
        _db.TaskChecklistItems.Add(new TaskChecklistItem { TaskId = taskId, Text = value, SortOrder = order, CreatedBy = ctx.DisplayName });
        await _db.SaveChangesAsync();
        await LogAsync(ctx, "Checklist Item Added", value, taskId, task.Goal, task.Project);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleChecklistItemAsync(int id, bool isCompleted, UserCtx ctx)
    {
        var item = await _db.TaskChecklistItems.FindAsync(id);
        if (item == null) return (false, "Checklist item not found.");
        var task = await FindTaskInTeam(item.TaskId, ctx.TeamId);
        if (task == null) return (false, "Task not found.");
        if (!await CanModifyAsync(task, ctx)) return (false, "You don't have permission to edit this task.");
        item.IsCompleted = isCompleted;
        item.CompletedAt = isCompleted ? DateTime.UtcNow : null;
        item.CompletedBy = isCompleted ? ctx.DisplayName : null;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteChecklistItemAsync(int id, UserCtx ctx)
    {
        var item = await _db.TaskChecklistItems.FindAsync(id);
        if (item == null) return (false, "Checklist item not found.");
        var task = await FindTaskInTeam(item.TaskId, ctx.TeamId);
        if (task == null) return (false, "Task not found.");
        if (!await CanModifyAsync(task, ctx)) return (false, "You don't have permission to edit this task.");
        _db.TaskChecklistItems.Remove(item);
        await _db.SaveChangesAsync();
        await LogAsync(ctx, "Checklist Item Removed", item.Text, task.Id, task.Goal, task.Project);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> AddLinkAsync(int fromTaskId, int toTaskId, string linkType, UserCtx ctx)
    {
        linkType = linkType?.Trim() ?? "";
        if (!SupportedLinkTypes.Contains(linkType)) return (false, "Unsupported link type.");
        linkType = SupportedLinkTypes.First(type => string.Equals(type, linkType, StringComparison.OrdinalIgnoreCase));
        if (fromTaskId == toTaskId) return (false, "Cannot link a task to itself.");
        var fromTask = await FindTaskInTeam(fromTaskId, ctx.TeamId);
        if (fromTask == null) return (false, "Task not found.");
        var toTask = await FindTaskInTeam(toTaskId, ctx.TeamId);
        if (toTask == null || toTask.TeamId != fromTask.TeamId)
            return (false, "Both tasks must belong to the active team.");
        if (!await CanModifyAsync(fromTask, ctx)) return (false, "You don't have permission to link this task.");
        var candidate = new TaskLink { FromTaskId = fromTaskId, ToTaskId = toTaskId, LinkType = linkType };
        if (TryGetBlockingDirection(candidate, out var blockerId, out var blockedId)
            && await WouldCreateDependencyCycleAsync(blockerId, blockedId, fromTask.TeamId))
            return (false, "This dependency would create a blocking cycle.");
        var exists = await _db.TaskLinks.AnyAsync(l =>
            (l.FromTaskId == fromTaskId && l.ToTaskId == toTaskId && l.LinkType == linkType) ||
            (l.FromTaskId == toTaskId   && l.ToTaskId == fromTaskId && l.LinkType == linkType));
        if (exists) return (false, "This link already exists.");
        _db.TaskLinks.Add(new TaskLink {
            FromTaskId = fromTaskId, ToTaskId = toTaskId, LinkType = linkType,
            CreatedAt = DateTime.Now, CreatedBy = ctx.DisplayName
        });
        await _db.SaveChangesAsync();
        await _hub.Clients.Group("task-" + fromTaskId).SendAsync("LinkUpdated", fromTaskId);
        await _hub.Clients.Group("task-" + toTaskId).SendAsync("LinkUpdated", toTaskId);
        if (TryGetBlockingDirection(candidate, out blockerId, out blockedId))
            await NotifyBlockedAsync(blockerId, blockedId);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RemoveLinkAsync(int id, UserCtx ctx)
    {
        var link = await _db.TaskLinks.FindAsync(id);
        if (link == null) return (false, "Not found.");
        var fromTask = await FindTaskInTeam(link.FromTaskId, ctx.TeamId);
        var toTask = await FindTaskInTeam(link.ToTaskId, ctx.TeamId);
        if (fromTask == null || toTask == null)
            return (false, "Not found.");
        if (!await CanModifyAsync(fromTask, ctx))
            return (false, "You don't have permission to remove this link.");
        int from = link.FromTaskId, to = link.ToTaskId;
        _db.TaskLinks.Remove(link);
        await _db.SaveChangesAsync();
        await _hub.Clients.Group("task-" + from).SendAsync("LinkUpdated", from);
        await _hub.Clients.Group("task-" + to).SendAsync("LinkUpdated", to);
        return (true, null);
    }

    public async Task<WatchToggleResult> ToggleWatchAsync(int taskId, string username, int? teamId)
    {
        if (teamId.HasValue && !await _db.Tasks.AnyAsync(t => t.Id == taskId && t.TeamId == teamId))
            return new WatchToggleResult { Error = "Task not found" };
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null) return new WatchToggleResult { Error = "User not found" };
        var existing = await _db.TaskWatchers.FirstOrDefaultAsync(w => w.TaskId == taskId && w.UserId == user.Id);
        bool watching;
        if (existing != null) { _db.TaskWatchers.Remove(existing); watching = false; }
        else { _db.TaskWatchers.Add(new TaskWatcher { TaskId = taskId, UserId = user.Id, CreatedAt = DateTime.Now }); watching = true; }
        await _db.SaveChangesAsync();
        var count = await _db.TaskWatchers.CountAsync(w => w.TaskId == taskId);
        return new WatchToggleResult { Success = true, Watching = watching, Count = count };
    }

    public async Task<(bool Success, string? Error)> QuickSetOwnerAsync(int id, string owner, UserCtx ctx)
    {
        var task = await FindTaskInTeam(id, ctx.TeamId);
        if (task == null) return (false, "Task not found");
        if (!await CanModifyAsync(task, ctx)) return (false, "You don't have permission to change this task's owner.");
        var old = task.Owner;
        task.Owner = owner?.Trim();
        await SyncTaskOwnersAsync(task.Id, task.Owner);
        await _db.SaveChangesAsync();
        await LogAsync(ctx, "Owner Changed", $"'{old}' → '{task.Owner}'", id, task.Goal, task.Project);
        return (true, null);
    }

    public async Task ReorderBacklogAsync(int[] ids, int? teamId)
    {
        if (ids == null || ids.Length == 0) return;
        var tasks    = await TeamTasks(teamId).Where(t => ids.Contains(t.Id)).ToListAsync();
        var orderMap = ids.Select((id, idx) => new { id, ord = idx + 1 }).ToDictionary(x => x.id, x => x.ord);
        foreach (var t in tasks)
            if (orderMap.TryGetValue(t.Id, out var ord)) t.BacklogOrder = ord;
        await _db.SaveChangesAsync();
    }

    public async Task<(bool Success, string? Error)> SaveAttachmentRecordAsync(
        int taskId, string fileName, string storedName, long fileSize, string contentType, string uploadedBy)
    {
        try
        {
            _db.TaskAttachments.Add(new TaskAttachment {
                TaskId = taskId, FileName = fileName, StoredName = storedName,
                FileSize = fileSize, ContentType = contentType,
                UploadedBy = uploadedBy, UploadedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool Success, string? Error)> DeleteAttachmentAsync(int id, string displayName, bool isAdmin)
    {
        var att = await _db.TaskAttachments.FindAsync(id);
        if (att == null) return (false, "Not found.");
        if (!isAdmin && att.UploadedBy != displayName) return (false, "Permission denied.");
        var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "attachments",
            att.TaskId.ToString(), att.StoredName);
        if (File.Exists(path)) File.Delete(path);
        _db.TaskAttachments.Remove(att);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ClaimTaskAsync(int taskId, UserCtx ctx)
    {
        var task = await FindTaskInTeam(taskId, ctx.TeamId);
        if (task == null) return (false, "Task not found.");
        if (ctx.UserId > 0 && await _db.TaskOwners.AnyAsync(o => o.TaskId == taskId && o.UserId == ctx.UserId))
            return (false, "You are already an owner of this task.");
        task.Owner = string.IsNullOrWhiteSpace(task.Owner)
            ? ctx.DisplayName
            : task.Owner.TrimEnd() + ", " + ctx.DisplayName;
        await SyncTaskOwnersAsync(task.Id, task.Owner);
        await _db.SaveChangesAsync();
        await LogAsync(ctx, "Claimed", $"{ctx.DisplayName} claimed this task", taskId, task.Goal, task.Project);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int id, UserCtx ctx)
    {
        var task = await FindTaskInTeam(id, ctx.TeamId);
        if (task == null) return (true, null); // nothing to delete — treat as success (redirect to index)
        if (!await CanModifyAsync(task, ctx))
            return (false, "You don't have permission to delete this task.");
        task.IsDeleted = true;
        task.DeletedAt = DateTime.Now;
        task.DeletedBy = ctx.DisplayName;
        await _db.SaveChangesAsync();
        await LogAsync(ctx, "Deleted", "Moved to Recycle Bin", id, task.Goal, task.Project);
        return (true, null);
    }

    public async Task<List<TaskItem>> GetRecycleBinAsync(int? teamId)
    {
        var q = _db.Tasks.IgnoreQueryFilters().Where(t => t.IsDeleted);
        if (teamId.HasValue) q = q.Where(t => t.TeamId == teamId);
        return await q.OrderByDescending(t => t.DeletedAt).ToListAsync();
    }

    public async Task<(bool Success, string? Error)> RestoreAsync(int id)
    {
        var task = await _db.Tasks.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id && t.IsDeleted);
        if (task == null) return (false, "Task not found");
        task.IsDeleted = false; task.DeletedAt = null; task.DeletedBy = null;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> PermanentDeleteAsync(int id)
    {
        var task = await _db.Tasks.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id && t.IsDeleted);
        if (task == null) return (false, "Not found");
        _db.Tasks.Remove(task);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, int Count)> EmptyBinAsync(int? teamId)
    {
        var q = _db.Tasks.IgnoreQueryFilters().Where(t => t.IsDeleted);
        if (teamId.HasValue) q = q.Where(t => t.TeamId == teamId);
        var tasks = await q.ToListAsync();
        _db.Tasks.RemoveRange(tasks);
        await _db.SaveChangesAsync();
        return (true, tasks.Count);
    }

    public async Task<BulkOpResult> BulkDeleteAsync(int[] ids, UserCtx ctx)
    {
        if (ids == null || ids.Length == 0) return new BulkOpResult { Error = "No tasks selected" };
        var now   = DateTime.Now;
        var all   = await TeamTasks(ctx.TeamId).Where(t => ids.Contains(t.Id)).ToListAsync();
        var tasks = await GetModifiableTasksAsync(all, ctx);
        var denied = all.Count - tasks.Count;
        foreach (var t in tasks) { t.IsDeleted = true; t.DeletedAt = now; t.DeletedBy = ctx.DisplayName; }
        await _db.SaveChangesAsync();
        return new BulkOpResult { Success = true, Count = tasks.Count, Denied = denied };
    }

    public async Task<BulkStatusOpResult> BulkChangeStatusAsync(int[] ids, string status, UserCtx ctx)
    {
        if (ids == null || ids.Length == 0 || string.IsNullOrEmpty(status))
            return new BulkStatusOpResult { Error = "Invalid request" };
        var all    = await TeamTasks(ctx.TeamId).Where(t => ids.Contains(t.Id)).ToListAsync();
        var tasks  = await GetModifiableTasksAsync(all, ctx);
        var denied = all.Count - tasks.Count;
        var noOwner = 0;
        var invalidTransition = 0;
        var needActualHours = 0;
        var openChildren = 0;
        var changed = new List<TaskItem>();
        foreach (var task in tasks)
        {
            var result = await ApplyStatusChangeAsync(task, status, ctx, forceClose: false, actualHours: null);
            if (result.Success) { changed.Add(task); continue; }
            if (result.NeedOwner) noOwner++;
            else if (result.NeedActualHours) needActualHours++;
            else if (result.OpenChildren.HasValue) openChildren++;
            else invalidTransition++;
        }
        await _db.SaveChangesAsync();
        foreach (var pid in changed.Select(t => t.ParentId).Where(p => p != null).Distinct())
            await CascadeParentStatusAsync(pid, ctx);
        return new BulkStatusOpResult {
            Success = true, Count = changed.Count, Denied = denied, NoOwner = noOwner,
            InvalidTransition = invalidTransition, NeedActualHours = needActualHours, OpenChildren = openChildren
        };
    }

    public async Task<BulkOpResult> BulkChangeOwnerAsync(int[] ids, string? owner, UserCtx ctx)
    {
        if (ids == null || ids.Length == 0) return new BulkOpResult { Error = "Invalid request" };
        var all   = await TeamTasks(ctx.TeamId).Where(t => ids.Contains(t.Id)).ToListAsync();
        var tasks = await GetModifiableTasksAsync(all, ctx);
        var denied = all.Count - tasks.Count;
        foreach (var t in tasks)
        {
            t.Owner = owner;
            await SyncTaskOwnersAsync(t.Id, owner);
        }
        await _db.SaveChangesAsync();
        return new BulkOpResult { Success = true, Count = tasks.Count, Denied = denied };
    }

    public async Task<BulkSprintOpResult> BulkAssignSprintAsync(int[] ids, int? sprintId, UserCtx ctx)
    {
        if (ids == null || ids.Length == 0) return new BulkSprintOpResult { Error = "Invalid request" };
        var all = await TeamTasks(ctx.TeamId)
            .Where(t => ids.Contains(t.Id) && t.ItemType != "Epic" && t.ItemType != "Feature")
            .ToListAsync();
        var tasks  = await GetModifiableTasksAsync(all, ctx);
        var denied = all.Count - tasks.Count;
        var skipped = 0;
        foreach (var t in tasks)
        {
            if (sprintId.HasValue && string.IsNullOrWhiteSpace(t.Owner)) { skipped++; continue; }
            var oldSprintId = t.SprintId;
            t.SprintId = sprintId;
            if (oldSprintId != t.SprintId)
                RecordSprintMembershipEvent(t, oldSprintId, t.SprintId, "Reassigned", ctx.DisplayName);
        }
        await _db.SaveChangesAsync();
        return new BulkSprintOpResult { Success = true, Count = tasks.Count - skipped, Skipped = skipped, Denied = denied };
    }

    public async Task<BulkOpResult> BulkChangePriorityAsync(int[] ids, string? priority, UserCtx ctx)
    {
        if (ids == null || ids.Length == 0) return new BulkOpResult { Error = "Invalid request" };
        var all   = await TeamTasks(ctx.TeamId).Where(t => ids.Contains(t.Id)).ToListAsync();
        var tasks = await GetModifiableTasksAsync(all, ctx);
        var denied = all.Count - tasks.Count;
        foreach (var t in tasks) t.Priority = priority;
        await _db.SaveChangesAsync();
        return new BulkOpResult { Success = true, Count = tasks.Count, Denied = denied };
    }

    private void RecordSprintMembershipEvent(
        TaskItem task, int? fromSprintId, int? toSprintId, string eventType, string occurredBy)
    {
        if (fromSprintId == toSprintId) return;
        _db.SprintWorkItemEvents.Add(new SprintWorkItemEvent {
            TaskId = task.Id,
            FromSprintId = fromSprintId,
            ToSprintId = toSprintId,
            EventType = eventType,
            Status = task.Status,
            Estimate = task.Weight,
            OccurredAt = DateTime.UtcNow,
            OccurredBy = occurredBy
        });
    }

    private async Task<string?> ResolveGovernedProjectAsync(TaskItem task, int? teamId)
    {
        if (!task.ProjectId.HasValue) return null;
        if (!teamId.HasValue) return "Select a team before assigning a governed project.";
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p =>
            p.Id == task.ProjectId.Value && p.TeamId == teamId && p.Status == "Active");
        if (project == null) return "Project not found or is not active for the current team.";
        task.Project = project.Name;
        return null;
    }

    private static bool TryNormalizeEstimateUnit(string? value, out string estimateUnit)
    {
        estimateUnit = value?.Trim() ?? "Points";
        if (string.Equals(estimateUnit, "points", StringComparison.OrdinalIgnoreCase))
        {
            estimateUnit = "Points";
            return true;
        }
        if (string.Equals(estimateUnit, "hours", StringComparison.OrdinalIgnoreCase))
        {
            estimateUnit = "Hours";
            return true;
        }
        return false;
    }

    // Upsert one row per (sprint, task) — hours replace the previous value for this sprint.
    private async Task UpsertSprintHoursLogAsync(int sprintId, int taskId, decimal hours, string loggedBy)
    {
        var existing = await _db.SprintHoursLogs
            .FirstOrDefaultAsync(l => l.SprintId == sprintId && l.TaskId == taskId);
        if (existing == null)
            _db.SprintHoursLogs.Add(new SprintHoursLog {
                SprintId = sprintId, TaskId = taskId,
                Hours = hours, LoggedBy = loggedBy, LoggedAt = DateTime.Now
            });
        else
        {
            existing.Hours    = hours;
            existing.LoggedBy = loggedBy;
            existing.LoggedAt = DateTime.Now;
        }
    }
}
