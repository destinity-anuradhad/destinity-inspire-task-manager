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

        // Owner counts — attribute each task to its owners via the TaskOwners junction (#7 stage 2).
        var ownerTaskIds = await taskQ.Select(t => t.Id).ToListAsync();
        var ownersByTask = await _db.OwnerNamesByTaskAsync(ownerTaskIds);
        var byOwner = ownersByTask.Values
            .SelectMany(names => names)
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).Take(15)
            .ToDictionary(g => g.Key, g => g.Count());

        // Velocity — pushed to SQL via GROUP BY on SprintId
        var sprintQ = _db.Sprints.AsNoTracking().AsQueryable();
        if (teamId.HasValue) sprintQ = sprintQ.Where(s => s.TeamId == teamId);
        var sprints = await sprintQ.OrderBy(s => s.StartDate ?? s.CreatedAt).ToListAsync();

        var sprintIds    = sprints.Select(s => s.Id).ToList();
        var activeSprintStats  = await taskQ
            .Where(t => t.SprintId.HasValue && sprintIds.Contains(t.SprintId.Value))
            .GroupBy(t => t.SprintId!.Value)
            .Select(g => new {
                SprintId  = g.Key,
                Committed = g.Where(t => t.EstimateUnit == "Points").Sum(t => (decimal?)(t.Weight ?? 0)) ?? 0m,
                Completed = g.Where(t => t.EstimateUnit == "Points" && (t.Status == "Done" || t.Status == "Resolved" || t.Status == "Closed"))
                             .Sum(t => (decimal?)(t.Weight ?? 0)) ?? 0m
            })
            .ToDictionaryAsync(x => x.SprintId);
        var snapshots = await _db.SprintCompletionSnapshots.AsNoTracking()
            .Where(s => sprintIds.Contains(s.SprintId))
            .ToDictionaryAsync(s => s.SprintId);

        var velocity = sprints.Select(s => {
            activeSprintStats.TryGetValue(s.Id, out var active);
            snapshots.TryGetValue(s.Id, out var snapshot);
            var committed = s.CompletedAt.HasValue && snapshot != null ? snapshot.CommittedEstimate : active?.Committed ?? 0m;
            var completed = s.CompletedAt.HasValue && snapshot != null ? snapshot.CompletedEstimate : active?.Completed ?? 0m;
            return new { name = s.Name, committed, completed };
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
