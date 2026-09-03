using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class SprintService(AppDbContext db) : ISprintService
{
    private readonly AppDbContext _db = db;

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
        var activeVelocityPoints = await _db.Tasks
            .Where(t => t.SprintId.HasValue && sprintIds.Contains(t.SprintId.Value) && !t.IsDeleted
                     && doneStatuses.Contains(t.Status) && t.EstimateUnit == "Points")
            .GroupBy(t => t.SprintId!.Value)
            .Select(g => new { SprintId = g.Key, Weight = g.Sum(t => (decimal?)(t.Weight ?? 0)) ?? 0m })
            .ToDictionaryAsync(x => x.SprintId, x => x.Weight);
        var completionSnapshots = await _db.SprintCompletionSnapshots
            .Where(s => sprintIds.Contains(s.SprintId))
            .ToDictionaryAsync(s => s.SprintId, s => s);
        var velocityPoints = sprints.ToDictionary(s => s.Id, s =>
            s.CompletedAt.HasValue && completionSnapshots.TryGetValue(s.Id, out var snapshot)
                ? snapshot.CompletedEstimate
                : activeVelocityPoints.GetValueOrDefault(s.Id));
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
            .Select(t => new { t.SprintId, t.Id, t.Weight, t.EstimateUnit })
            .ToListAsync();

        var estimateHours = taskOwnerRaw
            .Where(t => t.EstimateUnit == "Hours")
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
        var ownersByTask = await OwnerNamesByTaskAsync(
            taskOwnerRaw.Select(t => t.Id).Concat(hoursLogUserRaw.Select(l => l.TaskId)));

        var userHours = new Dictionary<int, List<SprintUserHoursRow>>();
        foreach (var sid in sprintIds)
        {
            var sprint = sprints.First(s => s.Id == sid);

            var estMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in taskOwnerRaw.Where(t => t.SprintId == sid && t.EstimateUnit == "Hours"))
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
            VelocityPoints  = velocityPoints,
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
            if (sprint.Id == 0)
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
            return (true, sprint.Id, null);
        }
        catch (Exception ex) { return (false, 0, ex.Message); }
    }

    public async Task SetActiveAsync(int id, int? teamId)
    {
        var sprintQ = _db.Sprints.AsQueryable();
        if (teamId.HasValue) sprintQ = sprintQ.Where(s => s.TeamId == teamId);
        var sprints = await sprintQ.ToListAsync();
        foreach (var s in sprints) s.IsActive = s.Id == id && !s.CompletedAt.HasValue;
        await _db.SaveChangesAsync();
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
        var incomplete = tasks.Count(t => !TaskStatusRules.IsDone(t.ItemType, t.Status));
        return new SprintIncompleteResult { Total = tasks.Count, Incomplete = incomplete, Done = tasks.Count - incomplete };
    }

    public async Task<(bool Found, bool Success, int Moved, string MovedTo, string? Error)> CompleteAsync(int id, string moveTo, int? teamId, string completedBy)
    {
        var sprint = await _db.Sprints.FindAsync(id);
        if (sprint == null) return (false, false, 0, "", null);
        if (teamId.HasValue && sprint.TeamId != teamId) return (false, false, 0, "", null);
        if (sprint.CompletedAt.HasValue)
            return (true, false, 0, "", "This sprint has already been completed.");

        int? targetSprintId = null;
        if (moveTo != "backlog" && int.TryParse(moveTo, out var tid))
        {
            var targetSprint = await _db.Sprints.FindAsync(tid);
            if (targetSprint == null)
                return (true, false, 0, "", "Target sprint not found.");
            if (teamId.HasValue && targetSprint.TeamId != teamId)
                return (true, false, 0, "", "Target sprint not found.");
            if (tid == id) return (true, false, 0, "", "A sprint cannot carry work forward to itself.");
            if (targetSprint.CompletedAt.HasValue || !targetSprint.IsActive)
                return (true, false, 0, "", "Carry-forward target must be an active, incomplete sprint.");
            targetSprintId = tid;
        }

        var tasks = await _db.Tasks.Where(t => t.SprintId == id && !t.IsDeleted).ToListAsync();
        int moved = 0;
        var completed = tasks.Where(t => TaskStatusRules.IsDone(t.ItemType, t.Status)).ToList();
        foreach (var t in completed)
        {
            _db.SprintWorkItemEvents.Add(new SprintWorkItemEvent {
                TaskId = t.Id, FromSprintId = id, ToSprintId = id, EventType = "Completed",
                Status = t.Status, Estimate = t.Weight, OccurredAt = DateTime.UtcNow, OccurredBy = completedBy
            });
        }
        foreach (var t in tasks.Except(completed))
        {
            _db.SprintWorkItemEvents.Add(new SprintWorkItemEvent {
                TaskId = t.Id, FromSprintId = id, ToSprintId = targetSprintId, EventType = "CarriedForward",
                Status = t.Status, Estimate = t.Weight, OccurredAt = DateTime.UtcNow, OccurredBy = completedBy
            });
            t.SprintId = targetSprintId;
            moved++;
        }
        _db.SprintCompletionSnapshots.Add(new SprintCompletionSnapshot {
            SprintId = id, CompletedAt = DateTime.UtcNow, CompletedBy = completedBy,
            CommittedCount = tasks.Count, CompletedCount = completed.Count, CarriedForwardCount = moved,
            CommittedEstimate = tasks.Where(t => t.EstimateUnit == "Points").Sum(t => t.Weight ?? 0),
            CompletedEstimate = completed.Where(t => t.EstimateUnit == "Points").Sum(t => t.Weight ?? 0)
        });
        sprint.IsActive = false;
        sprint.CompletedAt = DateTime.UtcNow;
        sprint.CompletedBy = completedBy;
        await _db.SaveChangesAsync();

        return (true, true, moved, targetSprintId.HasValue ? "sprint" : "backlog", null);
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

    // #7 stage 2: a task's owner display names resolved from the TaskOwners junction (not the Owner
    // string). Soft-deleted tasks' owners are included (junction rows aren't removed on soft delete),
    // matching the previous behaviour of reading the string off history rows.
    private async Task<Dictionary<int, List<string>>> OwnerNamesByTaskAsync(IEnumerable<int> taskIds)
    {
        var ids = taskIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return (await _db.TaskOwners
                .Where(o => ids.Contains(o.TaskId))
                .Join(_db.AppUsers, o => o.UserId, u => u.Id, (o, u) => new { o.TaskId, u.DisplayName })
                .ToListAsync())
            .GroupBy(x => x.TaskId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public async Task<SprintCapacityData?> GetCapacityAsync(int sprintId, int? teamId)
    {
        var sprint = await _db.Sprints.FindAsync(sprintId);
        if (sprint == null) return null;
        if (teamId.HasValue && sprint.TeamId != teamId) return null;

        // Load sprint tasks; owners come from the TaskOwners junction (#7 stage 2), not the string.
        var sprintTasksRaw = await _db.Tasks
            .Where(t => t.SprintId == sprintId && !t.IsDeleted)
            .Select(t => new { t.Id, t.Weight, t.EstimateUnit, t.ItemType, t.Status })
            .ToListAsync();

        // Per-sprint actual hours — join hours log to its task (deleted tasks included for history).
        var hoursLogRaw = await _db.SprintHoursLogs
            .Where(l => l.SprintId == sprintId)
            .Join(_db.Tasks.IgnoreQueryFilters(), l => l.TaskId, t => t.Id, (l, t) => new { l.Hours, TaskId = t.Id })
            .ToListAsync();

        var ownerNames = await OwnerNamesByTaskAsync(
            sprintTasksRaw.Select(t => t.Id).Concat(hoursLogRaw.Select(l => l.TaskId)));

        // Split work evenly between co-owners. Task count remains per assigned owner, but estimates
        // and actual hours must not be double-counted.
        var sprintTasks = sprintTasksRaw
            .SelectMany(t => {
                var owners = ownerNames.GetValueOrDefault(t.Id, []);
                return owners.Select(name => new {
                    DisplayName = name, t.Id, Estimate = (t.Weight ?? 0) / owners.Count,
                    t.EstimateUnit, t.ItemType, t.Status
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

        // Aggregate per owner: total tasks, done tasks, story points
        var ownerMap = new Dictionary<string, (decimal pts, decimal hrs, int total, int done)>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in sprintTasks.Where(a => !string.IsNullOrWhiteSpace(a.DisplayName)))
        {
            ownerMap.TryGetValue(a.DisplayName!, out var cur);
            var isDone = TaskStatusRules.IsDone(a.ItemType, a.Status);
            ownerMap[a.DisplayName!] = (
                cur.pts   + (a.EstimateUnit == "Points" ? a.Estimate : 0),
                cur.hrs   + (a.EstimateUnit == "Hours" ? a.Estimate : 0),
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
                AllocatedPoints = agg.pts,
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
        if (teamId.HasValue && !await _db.Sprints.AnyAsync(s => s.Id == req.SprintId && s.TeamId == teamId))
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
