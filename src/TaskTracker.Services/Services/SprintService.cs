using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class SprintService(AppDbContext db) : ISprintService
{
    private readonly AppDbContext _db = db;

    private static bool IsDoneStatus(string? itemType, string? status) => itemType switch {
        "Task"    => status == "Done",
        "Bug"     => status is "Resolved" or "Closed",
        "Story"   => status is "Resolved" or "Closed",
        "Feature" => status is "Resolved" or "Closed",
        _         => status is "Done" or "Resolved" or "Closed"
    };

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

        return new SprintIndexData { Sprints = sprints, TaskCounts = taskCounts, OwnerNames = ownerNames };
    }

    public async Task<(bool Success, string? Error)> SaveAsync(Sprint sprint, string createdBy, int? teamId)
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
                if (existing == null) return (false, "Not found");
                if (teamId.HasValue && existing.TeamId != teamId) return (false, "Not found");
                existing.Name      = sprint.Name;
                existing.StartDate = sprint.StartDate;
                existing.EndDate   = sprint.EndDate;
                existing.Goal      = sprint.Goal;
            }
            await _db.SaveChangesAsync();
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task SetActiveAsync(int id, int? teamId)
    {
        var sprintQ = _db.Sprints.AsQueryable();
        if (teamId.HasValue) sprintQ = sprintQ.Where(s => s.TeamId == teamId);
        var sprints = await sprintQ.ToListAsync();
        foreach (var s in sprints) s.IsActive = s.Id == id;
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
        var incomplete = tasks.Count(t => !IsDoneStatus(t.ItemType, t.Status));
        return new SprintIncompleteResult { Total = tasks.Count, Incomplete = incomplete, Done = tasks.Count - incomplete };
    }

    public async Task<(bool Found, bool Success, int Moved, string MovedTo, string? Error)> CompleteAsync(int id, string moveTo, int? teamId)
    {
        var sprint = await _db.Sprints.FindAsync(id);
        if (sprint == null) return (false, false, 0, "", null);
        if (teamId.HasValue && sprint.TeamId != teamId) return (false, false, 0, "", null);

        int? targetSprintId = null;
        if (moveTo != "backlog" && int.TryParse(moveTo, out var tid))
        {
            var targetSprint = await _db.Sprints.FindAsync(tid);
            if (targetSprint == null)
                return (true, false, 0, "", "Target sprint not found.");
            if (teamId.HasValue && targetSprint.TeamId != teamId)
                return (true, false, 0, "", "Target sprint not found.");
            targetSprintId = tid;
        }

        var tasks = await _db.Tasks.Where(t => t.SprintId == id && !t.IsDeleted).ToListAsync();
        int moved = 0;
        foreach (var t in tasks.Where(t => !IsDoneStatus(t.ItemType, t.Status)))
        { t.SprintId = targetSprintId; moved++; }
        sprint.IsActive = false;
        await _db.SaveChangesAsync();

        return (true, true, moved, targetSprintId.HasValue ? "sprint" : "backlog", null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int id, int? teamId)
    {
        var sprint = await _db.Sprints.FindAsync(id);
        if (sprint == null) return (false, "Not found");
        if (teamId.HasValue && sprint.TeamId != teamId) return (false, "Not found");
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

        var tasks = await _db.Tasks.Where(t => t.SprintId == sprintId)
            .Select(t => new { t.Owner, t.Weight }).ToListAsync();
        var allocationMap = new Dictionary<string, (decimal pts, int cnt)>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tasks)
            foreach (var owner in (t.Owner ?? "").Split(',').Select(o => o.Trim()).Where(o => o.Length > 0))
            {
                allocationMap.TryGetValue(owner, out var cur);
                allocationMap[owner] = (cur.pts + (t.Weight ?? 0), cur.cnt + 1);
            }

        var capacities = await _db.SprintCapacities.Where(c => c.SprintId == sprintId).ToListAsync();
        var capMap = capacities.ToDictionary(c => c.OwnerName, c => c.CapacityHours, StringComparer.OrdinalIgnoreCase);
        var allOwners = allocationMap.Keys.Concat(capMap.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(o => o).ToList();

        var rows = allOwners.Select(owner => {
            allocationMap.TryGetValue(owner, out var alloc);
            return new SprintCapacityRow {
                OwnerName       = owner,
                CapacityHours   = capMap.TryGetValue(owner, out var cap) ? (decimal?)cap : null,
                AllocatedPoints = alloc.pts,
                TaskCount       = alloc.cnt
            };
        }).ToList();

        return new SprintCapacityData {
            SprintId       = sprintId,
            SprintName     = sprint.Name,
            StartDate      = sprint.StartDate?.ToString("yyyy-MM-dd"),
            EndDate        = sprint.EndDate?.ToString("yyyy-MM-dd"),
            TotalCapacity  = rows.Sum(r => r.CapacityHours ?? 0),
            TotalAllocated = rows.Sum(r => r.AllocatedPoints),
            Rows           = rows
        };
    }

    public async Task<bool> SaveCapacityAsync(SprintCapacityRequest req, int? teamId)
    {
        if (teamId.HasValue && !await _db.Sprints.AnyAsync(s => s.Id == req.SprintId && s.TeamId == teamId))
            return false;
        var existing = await _db.SprintCapacities
            .FirstOrDefaultAsync(c => c.SprintId == req.SprintId && c.OwnerName == req.OwnerName);
        if (existing == null)
            _db.SprintCapacities.Add(new SprintCapacity {
                SprintId = req.SprintId, OwnerName = req.OwnerName, CapacityHours = req.CapacityHours
            });
        else
            existing.CapacityHours = req.CapacityHours;
        await _db.SaveChangesAsync();
        return true;
    }
}
