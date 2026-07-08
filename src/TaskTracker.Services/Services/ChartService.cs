using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TaskTracker.Data;

namespace TaskTracker.Services;

public class ChartService(AppDbContext db) : IChartService
{
    private readonly AppDbContext _db = db;

    public async Task<ChartData> GetChartDataAsync(int? teamId)
    {
        var taskQ = _db.Tasks.AsNoTracking().AsQueryable();
        if (teamId.HasValue) taskQ = taskQ.Where(t => t.TeamId == teamId);

        // Aggregations pushed to SQL via GROUP BY
        var byStatus = (await taskQ
            .GroupBy(t => t.Status ?? "Unknown")
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.Key, x => x.Count);

        var byPriority = (await taskQ
            .GroupBy(t => t.Priority ?? "None")
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.Key, x => x.Count);

        var byProperty = (await taskQ
            .Where(t => t.Property != null && t.Property != "")
            .GroupBy(t => t.Property!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync())
            .ToDictionary(x => x.Key, x => x.Count);

        var byProject = (await taskQ
            .Where(t => t.Project != null && t.Project != "")
            .GroupBy(t => t.Project!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync())
            .ToDictionary(x => x.Key, x => x.Count);

        // Owner CSV splitting cannot be pushed to SQL — load only the Owner column
        var ownerStrings = await taskQ
            .Where(t => t.Owner != null && t.Owner != "")
            .Select(t => t.Owner!)
            .ToListAsync();
        var ownerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var ownerStr in ownerStrings)
            foreach (var o in ownerStr.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
                ownerCounts[o] = ownerCounts.GetValueOrDefault(o) + 1;
        var byOwner = ownerCounts.OrderByDescending(kv => kv.Value).Take(15)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Velocity — pushed to SQL via GROUP BY on SprintId
        var sprintQ = _db.Sprints.AsNoTracking().AsQueryable();
        if (teamId.HasValue) sprintQ = sprintQ.Where(s => s.TeamId == teamId);
        var sprints = await sprintQ.OrderBy(s => s.StartDate ?? s.CreatedAt).ToListAsync();

        var doneStatuses = new[] { "Done", "Resolved", "Closed" };
        var sprintIds    = sprints.Select(s => s.Id).ToList();
        var sprintStats  = await taskQ
            .Where(t => t.SprintId.HasValue && sprintIds.Contains(t.SprintId.Value))
            .GroupBy(t => t.SprintId!.Value)
            .Select(g => new {
                SprintId  = g.Key,
                Committed = g.Sum(t => (decimal?)(t.Weight ?? 0)) ?? 0m,
                Completed = g.Where(t => doneStatuses.Contains(t.Status))
                             .Sum(t => (decimal?)(t.Weight ?? 0)) ?? 0m
            })
            .ToDictionaryAsync(x => x.SprintId);

        var velocity = sprints.Select(s => {
            sprintStats.TryGetValue(s.Id, out var st);
            return new { name = s.Name, committed = st?.Committed ?? 0m, completed = st?.Completed ?? 0m };
        }).ToList();

        return new ChartData
        {
            TotalTasks   = await taskQ.CountAsync(),
            VelocityJson = JsonSerializer.Serialize(velocity),
            StatusJson   = JsonSerializer.Serialize(byStatus),
            PriorityJson = JsonSerializer.Serialize(byPriority),
            OwnerJson    = JsonSerializer.Serialize(byOwner),
            PropertyJson = JsonSerializer.Serialize(byProperty),
            ProjectJson  = JsonSerializer.Serialize(byProject),
        };
    }
}
