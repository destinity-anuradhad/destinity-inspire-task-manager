using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class ActivityService(AppDbContext db) : IActivityService
{
    private readonly AppDbContext _db = db;

    public async Task LogAsync(
        string changedBy, string action, string category, string details,
        string? ip = null, string? ua = null,
        int? taskId = null, int? teamId = null,
        string? taskGoal = null, string? project = null)
    {
        try
        {
            int? resolvedTeamId = teamId;
            if (taskId.HasValue && resolvedTeamId == null)
                resolvedTeamId = await _db.Tasks
                    .Where(t => t.Id == taskId.Value)
                    .Select(t => t.TeamId)
                    .FirstOrDefaultAsync();

            _db.ActivityLog.Add(new ActivityLog
            {
                TaskId    = taskId,
                TeamId    = resolvedTeamId,
                TaskGoal  = taskGoal  ?? "",
                Project   = project   ?? "",
                ChangedBy = changedBy,
                Action    = action,
                Category  = category,
                Details   = details,
                IpAddress = ip,
                UserAgent = ua,
                ChangedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }
        catch { }
    }

    public async Task<ActivityLogResult> GetLogsAsync(
        bool isAdmin, int? teamId, int page,
        string? project, string? user,
        string? dateFrom, string? dateTo, string? category,
        int? filterTeamId = null)
    {
        const int pageSize = 50;
        var result = new ActivityLogResult();
        try
        {
            var query = _db.ActivityLog.AsQueryable();
            if (!isAdmin && teamId.HasValue)
                query = query.Where(a =>
                    a.Category == "Task"
                        ? _db.Tasks.Any(t => t.Id == a.TaskId && t.TeamId == teamId)
                        : a.TeamId == teamId);
            if (isAdmin && filterTeamId.HasValue)
                query = query.Where(a => a.TeamId == filterTeamId.Value);

            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(a => a.Category == category);
            if (!string.IsNullOrWhiteSpace(project))  query = query.Where(a => a.Project   == project);
            if (!string.IsNullOrWhiteSpace(user))     query = query.Where(a => a.ChangedBy == user);
            if (DateTime.TryParse(dateFrom, out var from)) query = query.Where(a => a.ChangedAt >= from);
            if (DateTime.TryParse(dateTo,   out var to))   query = query.Where(a => a.ChangedAt < to.AddDays(1));

            result.TotalCount = await query.CountAsync();
            result.Logs = await query
                .OrderByDescending(a => a.ChangedAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync();

            var baseQ = _db.ActivityLog.AsQueryable();
            if (!isAdmin && teamId.HasValue)
                baseQ = baseQ.Where(a =>
                    a.Category == "Task"
                        ? _db.Tasks.Any(t => t.Id == a.TaskId && t.TeamId == teamId)
                        : a.TeamId == teamId);
            if (isAdmin && filterTeamId.HasValue)
                baseQ = baseQ.Where(a => a.TeamId == filterTeamId.Value);

            result.Projects = await baseQ
                .Where(a => a.Project != null && a.Project != "")
                .Select(a => a.Project!).Distinct().OrderBy(p => p).ToListAsync();
            result.Users = await baseQ
                .Where(a => a.ChangedBy != null && a.ChangedBy != "")
                .Select(a => a.ChangedBy!).Distinct().OrderBy(u => u).ToListAsync();

            if (isAdmin)
            {
                var teams = await _db.Teams.Select(t => new { t.Id, t.Name }).ToListAsync();
                result.TeamNames = teams.ToDictionary(t => t.Id, t => t.Name);
            }

            var actUsers = await _db.AppUsers.ToListAsync();
            result.UserColors = actUsers.ToDictionary(
                u => u.DisplayName, u => u.Color ?? "#094f70",
                StringComparer.OrdinalIgnoreCase);
            result.UserImages = actUsers
                .Where(u => !string.IsNullOrEmpty(u.ProfileImagePath))
                .ToDictionary(u => u.DisplayName, u => u.ProfileImagePath!,
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            result.SetupRequired = true;
            if (isAdmin) result.TeamNames = [];
        }
        return result;
    }
}
