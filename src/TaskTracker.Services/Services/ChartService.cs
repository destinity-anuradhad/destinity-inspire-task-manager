using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TaskTracker.Data;

namespace TaskTracker.Services;

public class ChartService(AppDbContext db) : IChartService
{
    private readonly AppDbContext _db = db;

    public async Task<ChartData> GetChartDataAsync(int? teamId)
    {
        var taskQ = _db.Tasks.AsQueryable();
        if (teamId.HasValue) taskQ = taskQ.Where(t => t.TeamId == teamId);
        var tasks = await taskQ.ToListAsync();

        var byStatus = tasks
            .GroupBy(t => string.IsNullOrEmpty(t.Status) ? "Unknown" : t.Status)
            .ToDictionary(g => g.Key, g => g.Count());
        var byPriority = tasks
            .GroupBy(t => string.IsNullOrEmpty(t.Priority) ? "None" : t.Priority)
            .ToDictionary(g => g.Key, g => g.Count());

        var ownerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tasks.Where(t => !string.IsNullOrEmpty(t.Owner)))
            foreach (var o in t.Owner!.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
                ownerCounts[o] = ownerCounts.GetValueOrDefault(o) + 1;
        var byOwner = ownerCounts.OrderByDescending(kv => kv.Value).Take(15)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var byProperty = tasks.Where(t => !string.IsNullOrEmpty(t.Property))
            .GroupBy(t => t.Property!).OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
        var byProject = tasks.Where(t => !string.IsNullOrEmpty(t.Project))
            .GroupBy(t => t.Project!).OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        var sprintQ = _db.Sprints.AsQueryable();
        if (teamId.HasValue) sprintQ = sprintQ.Where(s => s.TeamId == teamId);
        var sprints = await sprintQ.OrderBy(s => s.StartDate ?? s.CreatedAt).ToListAsync();
        var doneStatuses = new[] { "Done", "Resolved", "Closed" };
        var velocity = sprints.Select(s => new {
            name      = s.Name,
            committed = tasks.Where(t => t.SprintId == s.Id && !t.IsDeleted).Sum(t => t.Weight ?? 0),
            completed = tasks.Where(t => t.SprintId == s.Id && !t.IsDeleted && doneStatuses.Contains(t.Status)).Sum(t => t.Weight ?? 0)
        }).ToList();

        return new ChartData
        {
            TotalTasks   = tasks.Count,
            VelocityJson = JsonSerializer.Serialize(velocity),
            StatusJson   = JsonSerializer.Serialize(byStatus),
            PriorityJson = JsonSerializer.Serialize(byPriority),
            OwnerJson    = JsonSerializer.Serialize(byOwner),
            PropertyJson = JsonSerializer.Serialize(byProperty),
            ProjectJson  = JsonSerializer.Serialize(byProject),
        };
    }
}
