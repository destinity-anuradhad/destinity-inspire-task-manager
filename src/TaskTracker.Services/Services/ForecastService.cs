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
        var completed = await db.Sprints.AsNoTracking()
            .Where(s => s.TeamId == teamId && s.CompletedAt != null)
            .Join(db.SprintCompletionSnapshots.AsNoTracking(), s => s.Id, snap => snap.SprintId,
                  (s, snap) => new SprintVelocityPoint(s.Id, s.Name, s.CompletedAt, snap.CompletedEstimate))
            .OrderByDescending(p => p.CompletedAt)
            .Take(recentSprints)
            .ToListAsync(ct);

        // Remaining backlog: open (not-done) point-estimated tasks in the team. IsDone is item-type
        // aware, so filter in memory over a minimal projection.
        var pointTasks = await db.Tasks.AsNoTracking()
            .Where(t => t.TeamId == teamId && t.EstimateUnit == "Points")
            .Select(t => new { t.Weight, t.Status, t.ItemType })
            .ToListAsync(ct);
        var remaining = pointTasks
            .Where(t => !TaskStatusRules.IsDone(t.ItemType, t.Status))
            .Sum(t => t.Weight ?? 0);

        if (completed.Count == 0)
            return new VelocityForecast(false, 0, 0, 0, 0, remaining, null, null, null, completed);

        var velocities = completed.Select(p => p.CompletedEstimate).ToList();
        var avg = velocities.Average();
        var min = velocities.Min();
        var max = velocities.Max();

        // Ordered oldest→newest for display.
        var history = completed.OrderBy(p => p.CompletedAt).ToList();

        if (avg <= 0)
            return new VelocityForecast(false, completed.Count, 0, min, max, remaining, null, null, null, history);

        return new VelocityForecast(
            HasEnoughData: true,
            CompletedSprintsUsed: completed.Count,
            AverageVelocity: Math.Round(avg, 2),
            MinVelocity: min,
            MaxVelocity: max,
            RemainingPoints: remaining,
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
