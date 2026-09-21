using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;

namespace TaskTracker.Services;

/// <summary>
/// Forecasts delivery from historical velocity. The velocity baseline is the immutable
/// <c>SprintCompletionSnapshot.CompletedEstimate</c> of the team's completed sprints (the same
/// authoritative facts the historical burndown uses), and the remaining work is the sum of open
/// point-estimated backlog. All read-only.
/// </summary>
public sealed class ForecastService(AppDbContext db) : IForecastService
{
    public async Task<VelocityForecast> GetForecastAsync(int teamId, int recentSprints = 6, CancellationToken ct = default)
    {
        recentSprints = Math.Clamp(recentSprints, 1, 50);

        // Completed sprints for the team, newest completion first, joined to their immutable snapshot.
        // Project to a plain shape (EF can order/take this in SQL); build the record in memory so EF
        // never has to translate an OrderBy over the SprintVelocityPoint constructor projection.
        var completedRaw = await db.Sprints.AsNoTracking()
            .Where(s => s.TeamId == teamId && s.CompletedAt != null)
            .Join(db.SprintCompletionSnapshots.AsNoTracking(), s => s.Id, snap => snap.SprintId,
                  (s, snap) => new { s.Id, s.Name, s.CompletedAt, snap.CompletedEstimate })
            .OrderByDescending(x => x.CompletedAt)
            .Take(recentSprints)
            .ToListAsync(ct);

        // Actual hours logged per sprint (SprintHoursLogs is the per-sprint truth), for the reality-check
        // velocity shown alongside the estimate-based projection.
        var sprintIds = completedRaw.Select(x => x.Id).ToList();
        var actualBySprint = await db.SprintHoursLogs.AsNoTracking()
            .Where(l => sprintIds.Contains(l.SprintId))
            .GroupBy(l => l.SprintId)
            .Select(g => new { SprintId = g.Key, Hours = g.Sum(x => x.Hours) })
            .ToDictionaryAsync(x => x.SprintId, x => x.Hours, ct);

        var completed = completedRaw
            .Select(x => new SprintVelocityPoint(
                x.Id, x.Name, x.CompletedAt, x.CompletedEstimate,
                actualBySprint.TryGetValue(x.Id, out var h) ? h : 0m))
            .ToList();

        // Remaining backlog: open (not-done) estimate hours in the team. Weight is the estimate (this
        // project estimates in hours); IsDone is item-type aware, so filter in memory.
        var openTasks = await db.Tasks.AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => new { t.Weight, t.Status, t.ItemType })
            .ToListAsync(ct);
        var remaining = openTasks
            .Where(t => !TaskStatusRules.IsDone(t.ItemType, t.Status))
            .Sum(t => t.Weight ?? 0);

        var avgActual = completed.Count == 0 ? 0m : Math.Round(completed.Average(p => p.ActualHours), 2);

        if (completed.Count == 0)
            return new VelocityForecast(false, 0, 0, 0, 0, avgActual, remaining, null, null, null, completed);

        var velocities = completed.Select(p => p.CompletedEstimate).ToList();
        var avg = velocities.Average();
        var min = velocities.Min();
        var max = velocities.Max();

        // Ordered oldest→newest for display.
        var history = completed.OrderBy(p => p.CompletedAt).ToList();

        if (avg <= 0)
            return new VelocityForecast(false, completed.Count, 0, min, max, avgActual, remaining, null, null, null, history);

        return new VelocityForecast(
            HasEnoughData: true,
            CompletedSprintsUsed: completed.Count,
            AverageVelocity: Math.Round(avg, 2),
            MinVelocity: min,
            MaxVelocity: max,
            AverageActualVelocity: avgActual,
            RemainingHours: remaining,
            ProjectedSprintsExpected: Sprints(remaining, avg),
            ProjectedSprintsOptimistic: Sprints(remaining, max),
            ProjectedSprintsPessimistic: Sprints(remaining, min),
            History: history);
    }

    // Sprints needed to burn down `remaining` at `velocity` per sprint (0 when nothing remains).
    private static int? Sprints(decimal remaining, decimal velocity)
    {
        if (remaining <= 0) return 0;
        if (velocity <= 0) return null;
        return (int)Math.Ceiling(remaining / velocity);
    }
}
