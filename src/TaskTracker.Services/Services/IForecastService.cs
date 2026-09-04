namespace TaskTracker.Services;

/// <summary>
/// Velocity-based delivery forecasting (Phase 4). Projects how many sprints the team's remaining
/// point-estimated backlog will take, from its historical completed velocity. Read-only.
/// </summary>
public interface IForecastService
{
    /// <summary>Forecast for a team, using the most recent <paramref name="recentSprints"/> completed
    /// sprints for the velocity baseline.</summary>
    Task<VelocityForecast> GetForecastAsync(int teamId, int recentSprints = 6, CancellationToken ct = default);
}

public sealed record SprintVelocityPoint(int SprintId, string SprintName, DateTime? CompletedAt, decimal CompletedEstimate);

public sealed record VelocityForecast(
    bool HasEnoughData,
    int CompletedSprintsUsed,
    decimal AverageVelocity,
    decimal MinVelocity,
    decimal MaxVelocity,
    decimal RemainingPoints,
    int? ProjectedSprintsExpected,      // remaining / average
    int? ProjectedSprintsOptimistic,    // remaining / max velocity (fewest sprints)
    int? ProjectedSprintsPessimistic,   // remaining / min velocity (most sprints)
    IReadOnlyList<SprintVelocityPoint> History);
