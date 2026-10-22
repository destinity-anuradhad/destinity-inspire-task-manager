using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class SprintService(AppDbContext db, IChatService chat) : ISprintService
{
    private readonly AppDbContext _db = db;
    private readonly IChatService _chat = chat;

    public async Task<SprintIndexData> GetIndexDataAsync(int? teamId)
    {
        var sprintQ = _db.Sprints.AsQueryable();
        if (teamId.HasValue) sprintQ = sprintQ.Where(s => s.TeamId == teamId);
        var sprints = await sprintQ
            .OrderByDescending(s => s.IsActive).ThenByDescending(s => s.CreatedAt)
            .ToListAsync();

        var sprintIds = sprints.Select(s => s.Id).ToList();
        var taskCounts = await _db.Tasks
            .Where(t => t.SprintId != null && sprintIds.Contains(t.SprintId!.Value))
            .GroupBy(t => t.SprintId)
            .Select(g => new { SprintId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SprintId!.Value, x => x.Count);

        var ownerNames = await _db.AppUsers
            .OrderBy(u => u.DisplayName).Select(u => u.DisplayName).ToListAsync();

        var teamMemberNames = teamId.HasValue
            ? await _db.TeamMembers
                .Where(m => m.TeamId == teamId.Value)
                .Join(_db.AppUsers, m => m.UserId, u => u.Id, (m, u) => u.DisplayName)
                .OrderBy(n => n).ToListAsync()
            : new List<string>();

        var doneStatuses = new[] { "Done", "Resolved", "Closed" };
        var activeVelocityHours = await _db.Tasks
            .Where(t => t.SprintId.HasValue && sprintIds.Contains(t.SprintId.Value) && !t.IsDeleted
                     && doneStatuses.Contains(t.Status))
            .GroupBy(t => t.SprintId!.Value)
            .Select(g => new { SprintId = g.Key, Weight = g.Sum(t => (decimal?)(t.Weight ?? 0)) ?? 0m })
            .ToDictionaryAsync(x => x.SprintId, x => x.Weight);
        var completionSnapshots = await _db.SprintCompletionSnapshots
            .Where(s => sprintIds.Contains(s.SprintId))
            .ToDictionaryAsync(s => s.SprintId, s => s);
        var velocityHours = sprints.ToDictionary(s => s.Id, s =>
            s.CompletedAt.HasValue && completionSnapshots.TryGetValue(s.Id, out var snapshot)
                ? snapshot.CompletedEstimate
                : activeVelocityHours.GetValueOrDefault(s.Id));
        var spilloverCounts = await _db.SprintWorkItemEvents
            .Where(e => e.EventType == "CarriedForward" && e.FromSprintId.HasValue && sprintIds.Contains(e.FromSprintId.Value))
            .GroupBy(e => e.FromSprintId!.Value)
            .Select(g => new { SprintId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SprintId, x => x.Count);

        // ── per-sprint totals ───────────────────────────────────────────────────
        var actualHoursRaw = await _db.SprintHoursLogs
            .Where(l => sprintIds.Contains(l.SprintId))
            .GroupBy(l => l.SprintId)
            .Select(g => new { SprintId = g.Key, Hours = g.Sum(l => l.Hours) })
            .ToDictionaryAsync(x => x.SprintId, x => x.Hours);

        var taskOwnerRaw = await _db.Tasks
            .Where(t => t.SprintId != null && sprintIds.Contains(t.SprintId!.Value) && !t.IsDeleted)
            .Select(t => new { t.SprintId, t.Id, t.Weight })
            .ToListAsync();

        var estimateHours = taskOwnerRaw
            .GroupBy(t => t.SprintId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(t => (decimal)(t.Weight ?? 0)));

        var sprintCapRaw = await _db.SprintCapacities
            .Where(c => sprintIds.Contains(c.SprintId))
            .ToListAsync();

        var sprintHours = sprintCapRaw
            .GroupBy(c => c.SprintId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.CapacityHours));

        // ── per-sprint, per-user rows ───────────────────────────────────────────
        var hoursLogUserRaw = await _db.SprintHoursLogs
            .Where(l => sprintIds.Contains(l.SprintId))
            .Join(_db.Tasks.IgnoreQueryFilters(), l => l.TaskId, t => t.Id,
                  (l, t) => new { l.SprintId, TaskId = t.Id, l.Hours })
            .ToListAsync();

        var teamIds         = sprints.Where(s => s.TeamId.HasValue).Select(s => s.TeamId!.Value).Distinct().ToList();
        var allTeamMembers  = await _db.TeamMembers
            .Where(m => teamIds.Contains(m.TeamId))
            .Join(_db.AppUsers, m => m.UserId, u => u.Id, (m, u) => new { m.TeamId, u.DisplayName })
            .ToListAsync();

        // #7 stage 2: per-user attribution comes from the TaskOwners junction, not the Owner string.
        var ownersByTask = await _db.OwnerNamesByTaskAsync(
            taskOwnerRaw.Select(t => t.Id).Concat(hoursLogUserRaw.Select(l => l.TaskId)));

        var userHours = new Dictionary<int, List<SprintUserHoursRow>>();
        foreach (var sid in sprintIds)
        {
            var sprint = sprints.First(s => s.Id == sid);

            var estMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in taskOwnerRaw.Where(t => t.SprintId == sid))
                foreach (var name in ownersByTask.GetValueOrDefault(t.Id, []))
                { estMap.TryGetValue(name, out var c); estMap[name] = c + (t.Weight ?? 0); }

            var actMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in hoursLogUserRaw.Where(l => l.SprintId == sid))
                foreach (var name in ownersByTask.GetValueOrDefault(l.TaskId, []))
                { actMap.TryGetValue(name, out var c); actMap[name] = c + l.Hours; }

            var capMap = sprintCapRaw.Where(c => c.SprintId == sid)
                .ToDictionary(c => c.OwnerName, c => (decimal?)c.CapacityHours, StringComparer.OrdinalIgnoreCase);

            var members = sprint.TeamId.HasValue
                ? allTeamMembers.Where(m => m.TeamId == sprint.TeamId.Value).Select(m => m.DisplayName)
                : Enumerable.Empty<string>();

            var names = estMap.Keys.Concat(actMap.Keys).Concat(capMap.Keys).Concat(members)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();

            userHours[sid] = names.Select(n => new SprintUserHoursRow {
                Name     = n,
                Estimate = estMap.GetValueOrDefault(n, 0),
                Actual   = actMap.GetValueOrDefault(n, 0),
                Sprint   = capMap.TryGetValue(n, out var cap) ? cap : null,
            }).ToList();
        }

        return new SprintIndexData {
            Sprints         = sprints,
            TaskCounts      = taskCounts,
            OwnerNames      = ownerNames,
            TeamMemberNames = teamMemberNames,
            VelocityHours   = velocityHours,
            SpilloverCounts = spilloverCounts,
            ActualHours     = actualHoursRaw,
            EstimateHours   = estimateHours,
            SprintHours     = sprintHours,
            UserHours       = userHours,
        };
    }

    public async Task<(bool Success, int SprintId, string? Error)> SaveAsync(Sprint sprint, string createdBy, int? teamId)
    {
        try
        {
            var isNew = sprint.Id == 0;
            if (isNew)
            {
                sprint.CreatedAt = DateTime.Now;
                sprint.CreatedBy = createdBy;
                sprint.TeamId    = teamId;
                _db.Sprints.Add(sprint);
            }
            else
            {
                var existing = await _db.Sprints.FindAsync(sprint.Id);
                if (existing == null) return (false, 0, "Not found");
                if (teamId.HasValue && existing.TeamId != teamId) return (false, 0, "Not found");
                if (existing.CompletedAt.HasValue) return (false, 0, "Completed sprints are read-only.");
                existing.Name      = sprint.Name;
                existing.StartDate = sprint.StartDate;
                existing.EndDate   = sprint.EndDate;
                existing.Goal      = sprint.Goal;
            }
            await _db.SaveChangesAsync();
            if (isNew && sprint.TeamId.HasValue)
            {
                var dates = sprint.StartDate.HasValue && sprint.EndDate.HasValue
                    ? $" ({sprint.StartDate:MMM d} – {sprint.EndDate:MMM d})" : "";
                var body = $"Created sprint “{sprint.Name}”{dates}"
                         + (!string.IsNullOrWhiteSpace(sprint.Goal) ? $"\nGoal: {sprint.Goal}" : "");
                await _chat.PostEventAsync(sprint.TeamId.Value, null, createdBy, body,
                    new { icon = "📅", type = "sprint", url = "/Sprints", link = "Open sprints" });
            }
            return (true, sprint.Id, null);
        }
        catch (Exception ex) { return (false, 0, ex.Message); }
    }

    public async Task SetActiveAsync(int id, int? teamId, string? by = null)
    {
        var sprintQ = _db.Sprints.AsQueryable();
        if (teamId.HasValue) sprintQ = sprintQ.Where(s => s.TeamId == teamId);
        var sprints = await sprintQ.ToListAsync();
        foreach (var s in sprints) s.IsActive = s.Id == id && !s.CompletedAt.HasValue;
        await _db.SaveChangesAsync();

        var activated = sprints.FirstOrDefault(s => s.Id == id && s.IsActive);
        if (activated != null && activated.TeamId.HasValue && !string.IsNullOrWhiteSpace(by))
            await _chat.PostEventAsync(activated.TeamId.Value, null, by, $"Started sprint “{activated.Name}”",
                new { icon = "🚀", type = "sprint", url = "/Sprints", link = "Open sprints" });
    }

    public async Task DeactivateAsync(int? teamId)
    {
        var sprintQ = _db.Sprints.AsQueryable();
        if (teamId.HasValue) sprintQ = sprintQ.Where(s => s.TeamId == teamId);
        var sprints = await sprintQ.ToListAsync();
        foreach (var s in sprints) s.IsActive = false;
        await _db.SaveChangesAsync();
    }

    public async Task<SprintIncompleteResult?> GetIncompleteAsync(int sprintId, int? teamId)
    {
        if (teamId.HasValue && !await _db.Sprints.AnyAsync(s => s.Id == sprintId && s.TeamId == teamId))
            return null;
        var tasks = await _db.Tasks.Where(t => t.SprintId == sprintId && !t.IsDeleted).ToListAsync();
        var skippedCount = tasks.Count(TaskStatusRules.IsExcludedFromSprintCompletion);
        var inScopeTasks = tasks.Where(t => !TaskStatusRules.IsExcludedFromSprintCompletion(t)).ToList();
        var incompleteTasks = inScopeTasks.Where(t => !TaskStatusRules.IsDone(t.ItemType, t.Status)).ToList();
        return new SprintIncompleteResult {
            Total = inScopeTasks.Count, Incomplete = incompleteTasks.Count,
            Done = inScopeTasks.Count - incompleteTasks.Count, Excluded = skippedCount,
            Tasks = incompleteTasks
                .OrderBy(t => t.Priority).ThenBy(t => t.Goal)
                .Select(t => new SprintIncompleteTask {
                    Id       = t.Id,
                    Goal     = t.Goal ?? "",
                    ItemType = t.ItemType ?? "Task",
                    Priority = t.Priority ?? "",
                    Owner    = t.Owner ?? "",
                    Estimate = t.Weight ?? 0,
                    Actual   = t.ActualHours ?? 0,
                    // Auto-fill remaining: use the stored re-estimate, else estimate − actual (never negative).
                    Remaining = t.RemainingWorkHours ?? Math.Max(0m, (t.Weight ?? 0) - (t.ActualHours ?? 0))
                }).ToList()
        };
    }

    public async Task<(bool Found, bool Success, int Moved, string MovedTo, string? Error)> CompleteAsync(
        int id, string moveTo, List<SprintCarryItem>? carryItems, int? teamId, string completedBy)
    {
        var sprint = await _db.Sprints.FindAsync(id);
        if (sprint == null) return (false, false, 0, "", null);
        if (teamId.HasValue && sprint.TeamId != teamId) return (false, false, 0, "", null);
        if (sprint.CompletedAt.HasValue)
            return (true, false, 0, "", "This sprint has already been completed.");

        // Resolve a carry-forward destination string → target sprint id (null = backlog).
        // Target must be an OPEN (not completed) sprint of this team, and not the sprint being closed.
        var destCache = new Dictionary<string, int?>();
        async Task<(bool ok, int? target, string? error)> ResolveDest(string? dest)
        {
            dest = string.IsNullOrWhiteSpace(dest) ? "backlog" : dest.Trim();
            if (dest == "backlog") return (true, null, null);
            if (destCache.TryGetValue(dest, out var cached)) return (true, cached, null);
            if (!int.TryParse(dest, out var tid)) return (true, null, null);   // unknown token → backlog
            var target = await _db.Sprints.FindAsync(tid);
            if (target == null || (teamId.HasValue && target.TeamId != teamId))
                return (false, null, "Carry-forward target sprint not found.");
            if (tid == id) return (false, null, "A sprint cannot carry work forward to itself.");
            if (target.CompletedAt.HasValue)
                return (false, null, "Carry-forward target must be an open (not completed) sprint.");
            destCache[dest] = tid;
            return (true, tid, null);
        }

        // Terminal status a delivered "slice" is stamped with, per item type.
        static string DoneStatusFor(string? itemType) => itemType == "Task" ? "Done" : "Closed";

        var carryMap  = (carryItems ?? []).GroupBy(c => c.TaskId).ToDictionary(g => g.Key, g => g.Last());
        var tasks     = await _db.Tasks.Where(t => t.SprintId == id && !t.IsDeleted).ToListAsync();
        var inScopeTasks = tasks.Where(t => !TaskStatusRules.IsExcludedFromSprintCompletion(t)).ToList();
        var completed = inScopeTasks.Where(t => TaskStatusRules.IsDone(t.ItemType, t.Status)).ToList();
        var carrying  = inScopeTasks.Except(completed).ToList();

        // Validate every destination BEFORE mutating, so a bad target can't leave a half-completed sprint.
        foreach (var t in carrying)
        {
            carryMap.TryGetValue(t.Id, out var ci0);
            var (ok0, _, err0) = await ResolveDest(ci0?.MoveTo ?? moveTo);
            if (!ok0) return (true, false, 0, "", err0);
        }

        // Snapshot metrics use the sprint's ORIGINAL scope, captured before any split re-scopes a Weight.
        int committedCount = inScopeTasks.Count;
        decimal committedEstimate = inScopeTasks.Sum(t => t.Weight ?? 0);
        int completedCount = completed.Count;
        decimal completedEstimate = completed.Sum(t => t.Weight ?? 0);

        int moved = 0;
        var destinations = new HashSet<string>();

        await using var tx = await _db.Database.BeginTransactionAsync();

        foreach (var t in completed)
            _db.SprintWorkItemEvents.Add(new SprintWorkItemEvent {
                TaskId = t.Id, FromSprintId = id, ToSprintId = id, EventType = "Completed",
                Status = t.Status, Estimate = t.Weight, OccurredAt = DateTime.UtcNow, OccurredBy = completedBy
            });

        foreach (var t in carrying)
        {
            carryMap.TryGetValue(t.Id, out var ci);
            var (_, target, _) = await ResolveDest(ci?.MoveTo ?? moveTo);   // cached + validated above

            var hoursSpent  = Math.Max(0m, ci?.HoursSpent ?? 0m);
            var origWeight  = t.Weight ?? 0m;
            var remaining   = Math.Max(0m, ci?.Remaining ?? Math.Max(0m, origWeight - (t.ActualHours ?? 0)));
            var totalActual = (t.ActualHours ?? 0) + hoursSpent;
            // Split only makes sense when the caller asked AND there's both progress and remaining work.
            var wantSplit   = string.Equals(ci?.Mode, "split", StringComparison.OrdinalIgnoreCase)
                              && remaining > 0 && totalActual > 0;

            if (wantSplit)
            {
                // ── Delivered SLICE — a new record left behind in this sprint (marked done). ──
                var deliveredWeight = Math.Max(0m, origWeight - remaining);
                var slice = new TaskItem {
                    Goal = t.Goal, Project = t.Project, ProjectId = t.ProjectId, Property = t.Property,
                    Priority = t.Priority, Status = DoneStatusFor(t.ItemType), Owner = t.Owner, ItemType = t.ItemType,
                    ParentId = t.ParentId, SprintId = id, ModuleId = t.ModuleId, ClientId = t.ClientId,
                    Labels = t.Labels, EstimateUnit = "Hours", Weight = deliveredWeight,
                    ActualHours = totalActual, RemainingWorkHours = 0m,
                    Reporter = t.Reporter, CreatedBy = completedBy, CreatedAt = DateTime.Now, TeamId = t.TeamId,
                    Swimlane = t.Swimlane, StartDate = t.StartDate, EndDate = t.EndDate,
                    ActualStartDate = t.ActualStartDate, ActualEndDate = DateTime.UtcNow
                };
                _db.Tasks.Add(slice);
                await _db.SaveChangesAsync();   // materialise slice.Id

                // The hours invested this sprint belong to the delivered slice — re-point the ledger onto it.
                await _db.TimeEntries.Where(e => e.TaskId == t.Id && e.SprintId == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.TaskId, slice.Id));
                await _db.SprintHoursLogs.Where(l => l.TaskId == t.Id && l.SprintId == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.TaskId, slice.Id));

                // Task tracked hours only in the mutable field (no ledger yet) → open a balance on the slice.
                var sliceHasEntries = await _db.TimeEntries.AnyAsync(e => e.TaskId == slice.Id);
                if (!sliceHasEntries && (t.ActualHours ?? 0) > 0)
                    _db.TimeEntries.Add(new TimeEntry {
                        TaskId = slice.Id, SprintId = id, UserId = null, Hours = t.ActualHours ?? 0,
                        WorkDate = t.CreatedAt.Date, EntryType = "LegacyOpeningBalance",
                        Note = $"Split from #{t.Id} at close of sprint \"{sprint.Name}\".",
                        CreatedAt = DateTime.UtcNow, CreatedBy = "system"
                    });
                if (hoursSpent > 0)
                    _db.TimeEntries.Add(new TimeEntry {
                        TaskId = slice.Id, SprintId = id, UserId = null, Hours = hoursSpent,
                        WorkDate = DateTime.UtcNow.Date, EntryType = "Logged",
                        Note = $"Logged at close of sprint \"{sprint.Name}\".",
                        CreatedAt = DateTime.UtcNow, CreatedBy = completedBy
                    });
                await _db.SaveChangesAsync();

                var sliceActual = await _db.TimeEntries.Where(e => e.TaskId == slice.Id).SumAsync(e => e.Hours);
                slice.ActualHours = sliceActual > 0 ? sliceActual : totalActual;
                await UpsertSprintHoursLogAsync(id, slice.Id, slice.ActualHours ?? 0, completedBy);

                // ── Continuation = the ORIGINAL task (keeps its id, comments, attachments, history),
                //    re-scoped to the remaining work and carried into the destination sprint. ──
                t.Weight             = remaining;
                t.RemainingWorkHours = remaining;
                t.ActualEndDate      = null;
                t.ActualHours        = await _db.TimeEntries.Where(e => e.TaskId == t.Id).SumAsync(e => e.Hours); // residual only
                _db.SprintWorkItemEvents.Add(new SprintWorkItemEvent {
                    TaskId = t.Id, FromSprintId = id, ToSprintId = target, EventType = "CarriedForward",
                    Status = t.Status, Estimate = remaining, OccurredAt = DateTime.UtcNow, OccurredBy = completedBy
                });
                t.SprintId = target;

                // Link the delivered slice → the continuation so both sprints show the connection.
                _db.TaskLinks.Add(new TaskLink {
                    FromTaskId = slice.Id, ToTaskId = t.Id, LinkType = "Split",
                    CreatedAt = DateTime.Now, CreatedBy = completedBy
                });

                completedCount++;
                completedEstimate += deliveredWeight;   // the slice delivered this much in this sprint
                moved++;
                destinations.Add(target.HasValue ? "sprint" : "backlog");
            }
            else
            {
                // ── Move the whole task (log any close-time hours against the OLD sprint first). ──
                if (hoursSpent > 0)
                {
                    var hasEntries = await _db.TimeEntries.AnyAsync(e => e.TaskId == t.Id);
                    if (!hasEntries && t.ActualHours is > 0)
                        _db.TimeEntries.Add(new TimeEntry {
                            TaskId = t.Id, SprintId = id, UserId = null, Hours = t.ActualHours.Value,
                            WorkDate = t.CreatedAt.Date, EntryType = "LegacyOpeningBalance",
                            Note = "Imported from legacy task total.", CreatedAt = DateTime.UtcNow, CreatedBy = "system"
                        });
                    _db.TimeEntries.Add(new TimeEntry {
                        TaskId = t.Id, SprintId = id, UserId = null, Hours = hoursSpent,
                        WorkDate = DateTime.UtcNow.Date, EntryType = "Logged",
                        Note = $"Logged at close of sprint \"{sprint.Name}\".",
                        CreatedAt = DateTime.UtcNow, CreatedBy = completedBy
                    });
                    var newActual = (t.ActualHours ?? 0) + hoursSpent;
                    t.ActualHours = newActual;
                    await _db.SaveChangesAsync();
                    await UpsertSprintHoursLogAsync(id, t.Id, newActual, completedBy);
                }
                t.RemainingWorkHours = remaining;
                _db.SprintWorkItemEvents.Add(new SprintWorkItemEvent {
                    TaskId = t.Id, FromSprintId = id, ToSprintId = target, EventType = "CarriedForward",
                    Status = t.Status, Estimate = remaining, OccurredAt = DateTime.UtcNow, OccurredBy = completedBy
                });
                t.SprintId = target;
                moved++;
                destinations.Add(target.HasValue ? "sprint" : "backlog");
            }
        }

        _db.SprintCompletionSnapshots.Add(new SprintCompletionSnapshot {
            SprintId = id, CompletedAt = DateTime.UtcNow, CompletedBy = completedBy,
            CommittedCount = committedCount, CompletedCount = completedCount, CarriedForwardCount = moved,
            CommittedEstimate = committedEstimate, CompletedEstimate = completedEstimate
        });
        sprint.IsActive = false;
        sprint.CompletedAt = DateTime.UtcNow;
        sprint.CompletedBy = completedBy;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        await PostSprintCompletedChatAsync(sprint, committedCount, completedCount, moved,
            committedEstimate, completedEstimate, completedBy);

        var movedTo = destinations.Count == 0 ? "backlog"
                    : destinations.Count == 1 ? destinations.First()
                    : "mixed";
        return (true, true, moved, movedTo, null);
    }

    // Auto-posts the sprint's capacity + burndown summary card to the team chat on close.
    private async Task PostSprintCompletedChatAsync(Sprint sprint, int committedCount, int completedCount,
        int carried, decimal plannedHours, decimal deliveredHours, string completedBy)
    {
        try
        {
            if (!sprint.TeamId.HasValue) return;
            var actualHours = await _db.SprintHoursLogs.Where(l => l.SprintId == sprint.Id).SumAsync(l => l.Hours);
            var prev = await _db.SprintCompletionSnapshots
                .Where(s => s.SprintId != sprint.Id)
                .Join(_db.Sprints, s => s.SprintId, sp => sp.Id, (s, sp) => new { s.CompletedEstimate, sp.TeamId, s.CompletedAt })
                .Where(x => x.TeamId == sprint.TeamId)
                .OrderByDescending(x => x.CompletedAt)
                .Select(x => (decimal?)x.CompletedEstimate)
                .FirstOrDefaultAsync();

            var pct = plannedHours > 0 ? (int)Math.Round(deliveredHours / plannedHours * 100m)
                    : committedCount > 0 ? (int)Math.Round(100m * completedCount / committedCount) : 0;
            var arrow = prev.HasValue ? (deliveredHours > prev ? " ▲" : deliveredHours < prev ? " ▼" : "") : "";

            var lines = new List<string> {
                $"Completed sprint “{sprint.Name}”",
                $"Capacity   {plannedHours:0.#}h planned · {actualHours:0.#}h done ({pct}%)",
                $"Tasks      {completedCount} done · {carried} carried over",
                $"Velocity   {deliveredHours:0.#}h{arrow}" + (prev.HasValue ? $" (prev {prev:0.#}h)" : "")
            };
            if (sprint.EndDate.HasValue && sprint.CompletedAt.HasValue)
            {
                var days = (sprint.EndDate.Value.Date - sprint.CompletedAt.Value.Date).Days;
                lines.Add(days > 0 ? $"Closed {days} day{(days == 1 ? "" : "s")} early"
                        : days < 0 ? $"Closed {-days} day{(days == -1 ? "" : "s")} late"
                        : "Closed on the due date");
            }
            await _chat.PostEventAsync(sprint.TeamId.Value, null, completedBy, string.Join("\n", lines),
                new { icon = "🏁", type = "sprint", url = "/Forecast", link = "View full report" });
        }
        catch { /* report post is best-effort — the sprint is already completed and committed */ }
    }

    // Books a task's per-sprint hours into SprintHoursLogs (the actual-velocity source), never
    // double-counting hours already booked against other sprints.
    private async Task UpsertSprintHoursLogAsync(int sprintId, int taskId, decimal totalActual, string by)
    {
        var bookedElsewhere = await _db.SprintHoursLogs
            .Where(l => l.TaskId == taskId && l.SprintId != sprintId).SumAsync(l => l.Hours);
        var sprintHours = Math.Max(0m, totalActual - bookedElsewhere);
        var existing = await _db.SprintHoursLogs.FirstOrDefaultAsync(l => l.SprintId == sprintId && l.TaskId == taskId);
        if (existing == null)
            _db.SprintHoursLogs.Add(new SprintHoursLog {
                SprintId = sprintId, TaskId = taskId, Hours = sprintHours, LoggedBy = by, LoggedAt = DateTime.Now
            });
        else { existing.Hours = sprintHours; existing.LoggedBy = by; existing.LoggedAt = DateTime.Now; }
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int id, int? teamId)
    {
        var sprint = await _db.Sprints.FindAsync(id);
        if (sprint == null) return (false, "Not found");
        if (teamId.HasValue && sprint.TeamId != teamId) return (false, "Not found");
        if (sprint.CompletedAt.HasValue) return (false, "Completed sprints are retained as immutable history.");
        var hasTasks = await _db.Tasks.AnyAsync(t => t.SprintId == id);
        if (hasTasks) return (false, "Cannot delete sprint with assigned tasks. Move or unassign them first.");
        _db.Sprints.Remove(sprint);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<SprintCapacityData?> GetCapacityAsync(int sprintId, int? teamId)
    {
        var sprint = await _db.Sprints.FindAsync(sprintId);
        if (sprint == null) return null;
        if (teamId.HasValue && sprint.TeamId != teamId) return null;

        // Load sprint tasks; owners come from the TaskOwners junction (#7 stage 2), not the string.
        var sprintTasksRaw = await _db.Tasks
            .Where(t => t.SprintId == sprintId && !t.IsDeleted)
            .Select(t => new { t.Id, t.Weight, t.ItemType, t.Status })
            .ToListAsync();

        // Per-sprint actual hours — join hours log to its task (deleted tasks included for history).
        var hoursLogRaw = await _db.SprintHoursLogs
            .Where(l => l.SprintId == sprintId)
            .Join(_db.Tasks.IgnoreQueryFilters(), l => l.TaskId, t => t.Id, (l, t) => new { l.Hours, TaskId = t.Id })
            .ToListAsync();

        var ownerNames = await _db.OwnerNamesByTaskAsync(
            sprintTasksRaw.Select(t => t.Id).Concat(hoursLogRaw.Select(l => l.TaskId)));

        // Split work evenly between co-owners. Task count remains per assigned owner, but estimates
        // and actual hours must not be double-counted.
        var sprintTasks = sprintTasksRaw
            .SelectMany(t => {
                var owners = ownerNames.GetValueOrDefault(t.Id, []);
                return owners.Select(name => new {
                    DisplayName = name, t.Id, Estimate = (t.Weight ?? 0) / owners.Count,
                    t.ItemType, t.Status
                });
            })
            .ToList();

        var hoursLog = hoursLogRaw
            .SelectMany(l => {
                var owners = ownerNames.GetValueOrDefault(l.TaskId, []);
                return owners.Select(name => new { DisplayName = name, Hours = l.Hours / owners.Count });
            })
            .ToList();

        var actualMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in hoursLog.Where(h => !string.IsNullOrWhiteSpace(h.DisplayName)))
        {
            actualMap.TryGetValue(h.DisplayName!, out var cur);
            actualMap[h.DisplayName!] = cur + h.Hours;
        }

        // Aggregate estimated hours and task completion per owner.
        var ownerMap = new Dictionary<string, (decimal hrs, int total, int done)>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in sprintTasks.Where(a => !string.IsNullOrWhiteSpace(a.DisplayName)))
        {
            ownerMap.TryGetValue(a.DisplayName!, out var cur);
            var isDone = TaskStatusRules.IsDone(a.ItemType, a.Status);
            ownerMap[a.DisplayName!] = (
                cur.hrs   + a.Estimate,
                cur.total + 1,
                cur.done  + (isDone ? 1 : 0)
            );
        }

        var capacities = await _db.SprintCapacities.Where(c => c.SprintId == sprintId).ToListAsync();
        var capMap     = capacities.ToDictionary(c => c.OwnerName, c => c.CapacityHours, StringComparer.OrdinalIgnoreCase);

        // Always include every team member, even those with no tasks yet
        var teamMembers = sprint.TeamId.HasValue
            ? await _db.TeamMembers
                .Where(m => m.TeamId == sprint.TeamId.Value)
                .Join(_db.AppUsers, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.DisplayName, u.DailyCapacityHours, u.WorkingDays })
                .ToListAsync()
            : [];
        var teamMemberNames = teamMembers.Select(m => m.DisplayName).ToList();
        var sprintStart = sprint.StartDate?.Date;
        var sprintEnd = sprint.EndDate?.Date;
        var leaveByUser = sprintStart.HasValue && sprintEnd.HasValue
            ? (await _db.UserTimeOff.Where(l => l.StartDate <= sprintEnd && l.EndDate >= sprintStart)
                .ToListAsync()).GroupBy(l => l.UserId).ToDictionary(g => g.Key, g => g.ToList())
            : new Dictionary<int, List<UserTimeOff>>();

        var allOwners  = ownerMap.Keys.Concat(capMap.Keys).Concat(teamMemberNames)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(o => o).ToList();

        var rows = allOwners.Select(owner => {
            ownerMap.TryGetValue(owner, out var agg);
            actualMap.TryGetValue(owner, out var sprintHours);
            var member = teamMembers.FirstOrDefault(m => string.Equals(m.DisplayName, owner, StringComparison.OrdinalIgnoreCase));
            decimal? derivedCapacity = member != null && sprintStart.HasValue && sprintEnd.HasValue && member.DailyCapacityHours is > 0
                ? CalculateEffectiveCapacity(member.DailyCapacityHours.Value, member.WorkingDays, sprintStart.Value, sprintEnd.Value,
                    leaveByUser.GetValueOrDefault(member.Id, []))
                : null;
            decimal? capacity = capMap.TryGetValue(owner, out var cap)
                ? derivedCapacity.HasValue ? Math.Min(cap, derivedCapacity.Value) : cap
                : derivedCapacity;
            return new SprintCapacityRow {
                OwnerName       = owner,
                CapacityHours   = capacity,
                AllocatedHours  = agg.hrs,
                ActualHours     = sprintHours,
                TaskCount       = agg.total,
                DoneCount       = agg.done
            };
        }).ToList();

        return new SprintCapacityData {
            SprintId         = sprintId,
            SprintName       = sprint.Name,
            StartDate        = sprint.StartDate?.ToString("yyyy-MM-dd"),
            EndDate          = sprint.EndDate?.ToString("yyyy-MM-dd"),
            IsCompleted      = sprint.CompletedAt.HasValue,
            TotalCapacity    = rows.Sum(r => r.CapacityHours ?? 0),
            TotalAllocated   = rows.Sum(r => r.AllocatedHours),
            TotalActualHours = rows.Sum(r => r.ActualHours),
            Rows             = rows
        };
    }

    private static decimal CalculateEffectiveCapacity(decimal dailyHours, string? workingDays, DateTime start, DateTime end,
        IReadOnlyCollection<UserTimeOff> timeOff)
    {
        var days = (workingDays ?? "Mon,Tue,Wed,Thu,Fri").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var leaveDates = timeOff.SelectMany(l => Enumerable.Range(0, (l.EndDate.Date - l.StartDate.Date).Days + 1)
            .Select(offset => l.StartDate.Date.AddDays(offset))).ToHashSet();
        decimal capacity = 0;
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var key = day.DayOfWeek.ToString()[..3];
            if (days.Contains(key) && !leaveDates.Contains(day.Date)) capacity += dailyHours;
        }
        return capacity;
    }

    public async Task<bool> SaveCapacityAsync(SprintCapacityRequest req, int? teamId)
    {
        var sprint = await _db.Sprints.FirstOrDefaultAsync(s => s.Id == req.SprintId
            && (!teamId.HasValue || s.TeamId == teamId.Value));
        if (sprint == null || sprint.CompletedAt.HasValue)
            return false;
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.DisplayName == req.OwnerName);
        if (user == null) return false;

        var existing = await _db.SprintCapacities
            .FirstOrDefaultAsync(c => c.SprintId == req.SprintId && c.UserId == user.Id);
        if (existing == null)
            _db.SprintCapacities.Add(new SprintCapacity {
                SprintId = req.SprintId, OwnerName = req.OwnerName, UserId = user.Id, CapacityHours = req.CapacityHours
            });
        else
            existing.CapacityHours = req.CapacityHours;
        await _db.SaveChangesAsync();
        return true;
    }
}
