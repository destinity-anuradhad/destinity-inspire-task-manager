using TaskTracker.Models;

namespace TaskTracker.Services;

public interface IActivityService
{
    Task LogAsync(
        string changedBy, string action, string category, string details,
        string? ip = null, string? ua = null,
        int? taskId = null, int? teamId = null,
        string? taskGoal = null, string? project = null);

    Task<ActivityLogResult> GetLogsAsync(
        bool isAdmin, int? teamId, int page,
        string? project, string? user,
        string? dateFrom, string? dateTo, string? category);
}

public class ActivityLogResult
{
    public List<ActivityLog>          Logs          { get; set; } = [];
    public int                        TotalCount    { get; set; }
    public List<string>               Projects      { get; set; } = [];
    public List<string>               Users         { get; set; } = [];
    public Dictionary<int, string>?   TeamNames     { get; set; }
    public Dictionary<string, string> UserColors    { get; set; } = [];
    public Dictionary<string, string> UserImages    { get; set; } = [];
    public bool                       SetupRequired { get; set; }
}
