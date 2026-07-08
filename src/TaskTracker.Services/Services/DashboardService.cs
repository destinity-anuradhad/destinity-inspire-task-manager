using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TaskTracker.Data;
using TaskTracker.Models;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services;

public class DashboardService(AppDbContext db) : IDashboardService
{
    private readonly AppDbContext _db = db;

    private static bool IsDone(TaskItem t) => t.ItemType switch {
        "Bug" or "Story" or "Feature" => t.Status is "Resolved" or "Closed",
        "Epic"                          => t.Status == "Closed",
        _                               => t.Status == "Done"
    };
    private static bool IsActive(TaskItem t) => t.Status is "In Progress" or "Active";

    public async Task<PersonalDashboardData> GetPersonalAsync(string displayName, int? teamId)
    {
        var taskQ = _db.Tasks.Where(t => !t.IsDeleted);
        if (teamId.HasValue) taskQ = taskQ.Where(t => t.TeamId == teamId);
        var all   = await taskQ.ToListAsync();
        var today = DateTime.Today;

        var mine = all.Where(t => !string.IsNullOrEmpty(t.Owner) &&
            t.Owner.Split(',').Select(o => o.Trim())
                   .Any(o => string.Equals(o, displayName, StringComparison.OrdinalIgnoreCase))).ToList();

        var bestGroup = mine.Where(t => !string.IsNullOrEmpty(t.Property))
            .GroupBy(t => t.Property).OrderByDescending(g => g.Count())
            .Select(g => new { Key = g.Key, Count = g.Count(), Done = g.Count(IsDone) })
            .FirstOrDefault();

        var data = new PersonalDashboardData
        {
            Total        = mine.Count,
            Done         = mine.Count(IsDone),
            InProgress   = mine.Count(IsActive),
            Open         = mine.Count(t => t.Status is "New" or "To Do"),
            Overdue      = mine.Count(t => !IsDone(t) && !string.IsNullOrEmpty(t.TargetDate) &&
                              DateTime.TryParse(t.TargetDate, out var d) && d.Date < today),
            Pending      = mine.Where(t => !IsDone(t))
                              .OrderBy(t => string.IsNullOrEmpty(t.TargetDate) ? DateTime.MaxValue :
                                  DateTime.TryParse(t.TargetDate, out var d) ? d : DateTime.MaxValue)
                              .Take(10).ToList(),
            WorkingOn    = mine.Where(IsActive).ToList(),
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
        var teams = await _db.Teams.Where(t => t.IsActive).ToListAsync();
        var members = await _db.TeamMembers.ToListAsync();
        var tasks   = await _db.Tasks.Where(t => !t.IsDeleted).ToListAsync();
        var today   = DateTime.Today;

        return teams.Select(t =>
        {
            var teamTasks = tasks.Where(x => x.TeamId == t.Id).ToList();
            var done      = teamTasks.Count(IsDone);
            return new TeamStat
            {
                Team        = t,
                MemberCount = members.Count(m => m.TeamId == t.Id),
                Total       = teamTasks.Count,
                Done        = done,
                Active      = teamTasks.Count(IsActive),
                Overdue     = teamTasks.Count(x => !IsDone(x) && !string.IsNullOrEmpty(x.TargetDate) &&
                                  DateTime.TryParse(x.TargetDate, out var d) && d.Date < today),
                CompletePct = teamTasks.Count > 0 ? (int)Math.Round((double)done / teamTasks.Count * 100) : 0,
            };
        }).ToList();
    }

    public async Task<TeamDashboardData> GetTeamAsync(int? teamId)
    {
        var taskQ = _db.Tasks.Where(t => !t.IsDeleted);
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

        var ownerDict = new Dictionary<string, List<TaskItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in all.Where(t => !string.IsNullOrEmpty(t.Owner)))
            foreach (var o in t.Owner!.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                if (!ownerDict.ContainsKey(o)) ownerDict[o] = [];
                ownerDict[o].Add(t);
            }

        var ownerStats = ownerDict.Select(kv => new OwnerStat {
            Name       = kv.Key,
            Total      = kv.Value.Count,
            Done       = kv.Value.Count(IsDone),
            InProgress = kv.Value.Count(IsActive),
            Overdue    = kv.Value.Count(t => !IsDone(t) && !string.IsNullOrEmpty(t.TargetDate) &&
                             DateTime.TryParse(t.TargetDate, out var d) && d.Date < today)
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
            Done          = all.Count(IsDone),
            InProgress    = all.Count(IsActive),
            Overdue       = all.Count(t => !IsDone(t) && !string.IsNullOrEmpty(t.TargetDate) &&
                               DateTime.TryParse(t.TargetDate, out var d) && d.Date < today),
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
