using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TaskTracker.Data;
using TaskTracker.Models;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services;

public class DashboardService(AppDbContext db) : IDashboardService
{
    private readonly AppDbContext _db = db;

    public async Task<PersonalDashboardData> GetPersonalAsync(string displayName, int? teamId)
    {
        var userId = await _db.AppUsers
            .Where(u => u.DisplayName == displayName)
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync();

        var today = DateTime.Today;
        List<TaskItem> mine;
        if (userId is int uid)
        {
            // #7 stage 1: "my tasks" is resolved from the authoritative TaskOwners junction rather than
            // `Owner.Contains(displayName)` — which fixes the substring false positive (e.g. "Sam"
            // matching "Samantha") and stops the personal dashboard depending on the display string.
            var taskQ = _db.Tasks.AsNoTracking()
                .Where(t => !t.IsDeleted && _db.TaskOwners.Any(o => o.TaskId == t.Id && o.UserId == uid));
            if (teamId.HasValue) taskQ = taskQ.Where(t => t.TeamId == teamId);
            mine = await taskQ.ToListAsync();
        }
        else
        {
            mine = [];   // unknown display name resolves to no owned work
        }

        var bestGroup = mine.Where(t => !string.IsNullOrEmpty(t.Property))
            .GroupBy(t => t.Property).OrderByDescending(g => g.Count())
            .Select(g => new { Key = g.Key, Count = g.Count(), Done = g.Count(TaskStatusRules.IsDone) })
            .FirstOrDefault();

        var data = new PersonalDashboardData
        {
            Total        = mine.Count,
            Done         = mine.Count(TaskStatusRules.IsDone),
            InProgress   = mine.Count(TaskStatusRules.IsActive),
            Open         = mine.Count(t => t.Status is "New" or "To Do"),
            Overdue      = mine.Count(t => !TaskStatusRules.IsDone(t) && t.TargetDate.HasValue && t.TargetDate.Value.Date < today),
            Pending      = mine.Where(t => !TaskStatusRules.IsDone(t))
                              .OrderBy(t => t.TargetDate ?? DateTime.MaxValue)
                              .Take(10).ToList(),
            WorkingOn    = mine.Where(TaskStatusRules.IsActive).ToList(),
            ByType       = mine.GroupBy(t => t.ItemType ?? "Task").ToDictionary(g => g.Key, g => g.Count()),
            ByPriority   = mine.Where(t => !string.IsNullOrEmpty(t.Priority))
                              .GroupBy(t => t.Priority!).ToDictionary(g => g.Key, g => g.Count()),
            BestProject      = bestGroup?.Key,
            BestProjectCount  = bestGroup?.Count ?? 0,
            BestProjectDone   = bestGroup?.Done  ?? 0,
        };
        data.CompletionPct = mine.Count > 0 ? (int)Math.Round((double)data.Done / mine.Count * 100) : 0;

        try
        {
            var myTaskIds = mine.Select(t => t.Id).ToHashSet();
            var recent = await _db.ActivityLog.OrderByDescending(a => a.ChangedAt).Take(300).ToListAsync();
            data.Activity   = recent.Where(a => a.TaskId.HasValue && myTaskIds.Contains(a.TaskId.Value)).Take(12).ToList();
            data.LastActLog = data.Activity.FirstOrDefault();
            if (data.LastActLog != null)
                data.LastTask = mine.FirstOrDefault(t => t.Id == data.LastActLog.TaskId!.Value);
        }
        catch { }

        return data;
    }

    public async Task<List<TeamStat>> GetAllTeamsStatsAsync()
    {
        var teams   = await _db.Teams.AsNoTracking().Where(t => t.IsActive).ToListAsync();
        var members = await _db.TeamMembers.AsNoTracking().ToListAsync();
        var tasks   = await _db.Tasks.AsNoTracking().Where(t => !t.IsDeleted).ToListAsync();
        var today   = DateTime.Today;

        return teams.Select(t =>
        {
            var teamTasks = tasks.Where(x => x.TeamId == t.Id).ToList();
            var done      = teamTasks.Count(TaskStatusRules.IsDone);
            return new TeamStat
            {
                Team        = t,
                MemberCount = members.Count(m => m.TeamId == t.Id),
                Total       = teamTasks.Count,
                Done        = done,
                Active      = teamTasks.Count(TaskStatusRules.IsActive),
                Overdue     = teamTasks.Count(x => !TaskStatusRules.IsDone(x) && x.TargetDate.HasValue && x.TargetDate.Value.Date < today),
                CompletePct = teamTasks.Count > 0 ? (int)Math.Round((double)done / teamTasks.Count * 100) : 0,
            };
        }).ToList();
    }

    public async Task<TeamDashboardData> GetTeamAsync(int? teamId)
    {
        var taskQ = _db.Tasks.AsNoTracking().Where(t => !t.IsDeleted);
        if (teamId.HasValue) taskQ = taskQ.Where(t => t.TeamId == teamId);
        var all   = await taskQ.ToListAsync();
        var today = DateTime.Today;

        var statusCounts   = all.GroupBy(t => t.Status ?? "").ToDictionary(g => g.Key, g => g.Count());
        var projectCounts  = all.Where(t => !string.IsNullOrEmpty(t.Property))
            .GroupBy(t => t.Property).OrderByDescending(g => g.Count()).Take(10)
            .Select(g => new { Key = g.Key, Count = g.Count() }).ToList();
        var typeCounts     = all.GroupBy(t => t.ItemType ?? "Task").OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
        var priorityCounts = all.Where(t => !string.IsNullOrEmpty(t.Priority))
            .GroupBy(t => t.Priority!).ToDictionary(g => g.Key, g => g.Count());

        // #7 stage 2: owner attribution comes from the TaskOwners junction, not the Owner string.
        var ownersByTask = await _db.OwnerNamesByTaskAsync(all.Select(t => t.Id));
        var ownerDict = new Dictionary<string, List<TaskItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in all)
            foreach (var o in ownersByTask.GetValueOrDefault(t.Id, []))
            {
                if (!ownerDict.ContainsKey(o)) ownerDict[o] = [];
                ownerDict[o].Add(t);
            }

        var ownerStats = ownerDict.Select(kv => new OwnerStat {
            Name       = kv.Key,
            Total      = kv.Value.Count,
            Done       = kv.Value.Count(TaskStatusRules.IsDone),
            InProgress = kv.Value.Count(TaskStatusRules.IsActive),
            Overdue    = kv.Value.Count(t => !TaskStatusRules.IsDone(t) && t.TargetDate.HasValue && t.TargetDate.Value.Date < today)
        }).OrderByDescending(o => o.Total).ToList();

        var appUsers      = await _db.AppUsers.ToListAsync();
        var ownerColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ownerImageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in appUsers)
        {
            ownerColorMap.TryAdd(u.DisplayName, u.Color ?? "#3b82f6");
            if (!string.IsNullOrEmpty(u.ProfileImagePath))
                ownerImageMap.TryAdd(u.DisplayName, u.ProfileImagePath);
        }

        var data = new TeamDashboardData
        {
            Total         = all.Count,
            Done          = all.Count(TaskStatusRules.IsDone),
            InProgress    = all.Count(TaskStatusRules.IsActive),
            Overdue       = all.Count(t => !TaskStatusRules.IsDone(t) && t.TargetDate.HasValue && t.TargetDate.Value.Date < today),
            StatusLabels  = JsonSerializer.Serialize(statusCounts.Keys.ToArray()),
            StatusValues  = JsonSerializer.Serialize(statusCounts.Values.ToArray()),
            ProjectLabels = JsonSerializer.Serialize(projectCounts.Select(p => p.Key).ToArray()),
            ProjectValues = JsonSerializer.Serialize(projectCounts.Select(p => p.Count).ToArray()),
            TypeLabels    = JsonSerializer.Serialize(typeCounts.Keys.ToArray()),
            TypeValues    = JsonSerializer.Serialize(typeCounts.Values.ToArray()),
            PriorityLabels = JsonSerializer.Serialize(priorityCounts.Keys.ToArray()),
            PriorityValues = JsonSerializer.Serialize(priorityCounts.Values.ToArray()),
            OwnerStats    = ownerStats,
            OwnerColorMap = ownerColorMap,
            OwnerImageMap = ownerImageMap,
            OwnerNames    = JsonSerializer.Serialize(ownerStats.Select(o => o.Name).ToArray()),
            OwnerDone     = JsonSerializer.Serialize(ownerStats.Select(o => o.Done).ToArray()),
            OwnerPending  = JsonSerializer.Serialize(ownerStats.Select(o => o.Total - o.Done).ToArray()),
        };

        try
        {
            var actQ = _db.ActivityLog.AsQueryable();
            if (teamId.HasValue)
                actQ = actQ.Where(a => _db.Tasks.Any(t => t.Id == a.TaskId && t.TeamId == teamId));
            data.RecentActivity = await actQ.OrderByDescending(a => a.ChangedAt).Take(15).ToListAsync();
        }
        catch { }

        return data;
    }
}
