namespace TaskTracker.Services;

/// <summary>
/// Cross-team portfolio views (Phase 4). Surfaces blocking dependencies that cross team boundaries —
/// where one team's open work is holding up another team's — from the existing task-link graph.
/// Read-only.
/// </summary>
public interface IPortfolioService
{
    /// <summary>
    /// Cross-team blocking dependencies. When <paramref name="includeAllTeams"/> is false, only edges
    /// involving <paramref name="teamId"/> (as blocker or blocked) are returned.
    /// </summary>
    Task<CrossTeamDependencyReport> GetCrossTeamDependenciesAsync(
        int? teamId, bool includeAllTeams, CancellationToken ct = default);
}

public sealed record CrossTeamDependencyEdge(
    int BlockerTaskId, string? BlockerGoal, int BlockerTeamId, string? BlockerTeamName, string? BlockerStatus, string? BlockerItemType,
    int BlockedTaskId, string? BlockedGoal, int BlockedTeamId, string? BlockedTeamName, string? BlockedStatus,
    bool IsBlockerOpen)
{
    /// <summary>The blocked task is genuinely held up: the blocker is still open.</summary>
    public bool IsAtRisk => IsBlockerOpen;
}

public sealed record CrossTeamDependencyReport(
    int Total, int AtRisk, IReadOnlyList<CrossTeamDependencyEdge> Edges);
