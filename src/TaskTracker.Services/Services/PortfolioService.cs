using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;

namespace TaskTracker.Services;

/// <summary>
/// Builds cross-team dependency views from the task-link graph. A "cross-team dependency" is a blocking
/// link whose blocker and blocked tasks belong to different teams; the actionable subset is where the
/// blocker is still open (the blocked team is genuinely waiting). Read-only.
/// </summary>
public sealed class PortfolioService(AppDbContext db) : IPortfolioService
{
    public async Task<CrossTeamDependencyReport> GetCrossTeamDependenciesAsync(
        int? teamId, bool includeAllTeams, CancellationToken ct = default)
    {
        var links = await db.TaskLinks.AsNoTracking().ToListAsync(ct);

        // Reduce links to blocker→blocked edges using the shared link semantics.
        var edges = new List<(int Blocker, int Blocked)>();
        foreach (var link in links)
            if (TaskLinkRules.TryGetBlockingDirection(link, out var b, out var bd))
                edges.Add((b, bd));
        if (edges.Count == 0) return new CrossTeamDependencyReport(0, 0, []);

        // Load every task referenced by an edge (ignore soft-delete filter so a deleted blocker still
        // shows — a dangling dependency is itself worth surfacing).
        var ids = edges.SelectMany(e => new[] { e.Blocker, e.Blocked }).Distinct().ToList();
        var tasks = await db.Tasks.IgnoreQueryFilters()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Goal, t.TeamId, t.Status, t.ItemType })
            .ToDictionaryAsync(t => t.Id, ct);

        var teamNames = await db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var result = new List<CrossTeamDependencyEdge>();
        var seen = new HashSet<(int, int)>();
        foreach (var (blockerId, blockedId) in edges)
        {
            if (!seen.Add((blockerId, blockedId))) continue;   // dedupe reciprocal Blocks/Blocked-By pairs
            if (!tasks.TryGetValue(blockerId, out var blocker) || !tasks.TryGetValue(blockedId, out var blocked))
                continue;

            // Cross-team only: both tasks have a team and the teams differ.
            if (blocker.TeamId is not int blockerTeam || blocked.TeamId is not int blockedTeam) continue;
            if (blockerTeam == blockedTeam) continue;

            // Scope: non-sysadmin callers only see edges touching their own team.
            if (!includeAllTeams && blockerTeam != teamId && blockedTeam != teamId) continue;

            result.Add(new CrossTeamDependencyEdge(
                blockerId, blocker.Goal, blockerTeam, teamNames.GetValueOrDefault(blockerTeam), blocker.Status, blocker.ItemType,
                blockedId, blocked.Goal, blockedTeam, teamNames.GetValueOrDefault(blockedTeam), blocked.Status,
                IsBlockerOpen: !TaskStatusRules.IsDone(blocker.ItemType, blocker.Status)));
        }

        // Most urgent first: at-risk (open blocker) edges lead.
        result = result.OrderByDescending(e => e.IsAtRisk).ThenBy(e => e.BlockedTeamName).ToList();
        return new CrossTeamDependencyReport(result.Count, result.Count(e => e.IsAtRisk), result);
    }
}
