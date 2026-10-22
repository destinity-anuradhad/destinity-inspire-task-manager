namespace TaskTracker.Services;

/// <summary>
/// Velocity-based delivery forecasting (Phase 4). Projects how many sprints the team's remaining
/// hour-estimated backlog will take, from its historical completed velocity (estimate hours delivered
/// per sprint). Actual hours logged per sprint are reported alongside as a reality check. Read-only.
/// </summary>
public interface IForecastService
{
    /// <summary>Forecast for a team, using the most recent <paramref name="recentSprints"/> completed
    /// sprints for the velocity baseline.</summary>
    Task<VelocityForecast> GetForecastAsync(int teamId, int recentSprints = 6, CancellationToken ct = default);
}

public sealed record SprintVelocityPoint(
    int SprintId, string SprintName, DateTime? CompletedAt,
    decimal CompletedEstimate,   // estimate hours delivered — the velocity
    decimal ActualHours);        // actual hours logged in the sprint — reality check

public sealed record VelocityForecast(
    bool HasEnoughData,
    int CompletedSprintsUsed,
    decimal AverageVelocity,            // avg estimate hours delivered / sprint — drives the projection
    decimal MinVelocity,
    decimal MaxVelocity,
    decimal AverageActualVelocity,      // avg actual hours logged / sprint — shown alongside
    decimal RemainingHours,             // remaining estimate hours (open backlog)
    int? ProjectedSprintsExpected,      // remaining / average
    int? ProjectedSprintsOptimistic,    // remaining / max velocity (fewest sprints)
    int? ProjectedSprintsPessimistic,   // remaining / min velocity (most sprints)
    IReadOnlyList<SprintVelocityPoint> History);
