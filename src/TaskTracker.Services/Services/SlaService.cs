using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

/// <summary>
/// Team SLA policies and compliance reporting. A policy says "work of this kind should be done within
/// N hours of creation"; evaluation compares that target to a task's creation/completion times. All
/// evaluation is read-only — nothing here writes to a task.
/// </summary>
public sealed class SlaService(AppDbContext db) : ISlaService
{
    // Fraction of the target still remaining at or below which an open task is flagged "at risk".
    private const double AtRiskFraction = 0.25;

    // ── Management ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SlaPolicySummary>> ListAsync(int? teamId, bool includeGlobal, CancellationToken ct = default)
    {
        var query = db.SlaPolicies.AsNoTracking();
        if (!includeGlobal)
        {
            if (teamId is null) return [];
            query = query.Where(p => p.TeamId == teamId);
        }

        var policies = await query
            .OrderByDescending(p => p.IsEnabled)
            .ThenBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync(ct);
        var teamNames = await ResolveTeamNamesAsync(policies.Select(p => p.TeamId), ct);

        return policies.Select(p => new SlaPolicySummary(
            p.Id, p.Name, p.TeamId, teamNames.GetValueOrDefault(p.TeamId), p.IsEnabled,
            p.AppliesToItemType, p.AppliesToPriority, p.TargetHours, p.CreatedAt, p.CreatedBy)).ToList();
    }

    public async Task<SlaPolicy> CreateAsync(SlaPolicyInput input, CancellationToken ct = default)
    {
        var name = (input.Name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("A policy name is required.", nameof(input));
        if (input.TargetHours <= 0) throw new ArgumentException("Target hours must be greater than zero.", nameof(input));

        var nextOrder = (await db.SlaPolicies
            .Where(p => p.TeamId == input.TeamId)
            .MaxAsync(p => (int?)p.SortOrder, ct) ?? 0) + 1;

        var policy = new SlaPolicy
        {
            TeamId = input.TeamId, Name = name, IsEnabled = true,
            AppliesToItemType = Blank(input.AppliesToItemType),
            AppliesToPriority = Blank(input.AppliesToPriority),
            TargetHours = input.TargetHours, SortOrder = nextOrder,
            CreatedAt = DateTime.UtcNow, CreatedBy = input.CreatedBy,
        };
        db.SlaPolicies.Add(policy);
        await db.SaveChangesAsync(ct);
        return policy;
    }

    public async Task<bool> SetEnabledAsync(int id, bool enabled, int? teamId, bool isSysAdmin, CancellationToken ct = default)
    {
        var policy = await FindOwnedAsync(id, teamId, isSysAdmin, ct);
        if (policy == null) return false;
        policy.IsEnabled = enabled;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, int? teamId, bool isSysAdmin, CancellationToken ct = default)
    {
        var policy = await FindOwnedAsync(id, teamId, isSysAdmin, ct);
        if (policy == null) return false;
        db.SlaPolicies.Remove(policy);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Reporting ───────────────────────────────────────────────────────────

    public async Task<SlaReport> GetReportAsync(int teamId, CancellationToken ct = default)
    {
        var policies = await db.SlaPolicies.AsNoTracking()
            .Where(p => p.TeamId == teamId && p.IsEnabled)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync(ct);
        if (policies.Count == 0)
            return new SlaReport(teamId, 0, 0, 0, 0, []);

        // Only open work is actionable for an SLA dashboard; done tasks have already met or missed.
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var items = new List<SlaReportItem>();
        foreach (var task in tasks)
        {
            if (TaskStatusRules.IsDone(task.ItemType, task.Status)) continue;   // open work only
            var policy = FirstMatching(policies, task);
            if (policy == null) continue;

            var eval = Evaluate(policy, task, now);
            items.Add(new SlaReportItem(
                task.Id, task.Goal, task.ItemType, task.Priority, task.Status, task.Owner,
                policy.Name, policy.TargetHours, eval.Status, eval.Deadline, eval.HoursRemaining));
        }

        // Most urgent first: breaches, then soonest deadline.
        items.Sort((a, b) => a.HoursRemaining.CompareTo(b.HoursRemaining));

        return new SlaReport(
            teamId, items.Count,
            items.Count(i => i.SlaStatus == SlaStatuses.OnTrack),
            items.Count(i => i.SlaStatus == SlaStatuses.AtRisk),
            items.Count(i => i.SlaStatus == SlaStatuses.Breached),
            items);
    }

    // ── Evaluation (pure) ─────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates a task against a policy at <paramref name="nowUtc"/>. For open work the status is
    /// On-track / At-risk / Breached against the deadline (created + target). For done work it's Met or
    /// Breached depending on whether completion beat the deadline.
    /// </summary>
    public static SlaEvaluation Evaluate(SlaPolicy policy, TaskItem task, DateTime nowUtc)
    {
        var deadline = task.CreatedAt.AddHours(policy.TargetHours);
        var isOpen = !TaskStatusRules.IsDone(task.ItemType, task.Status);

        if (!isOpen)
        {
            var completedAt = task.ActualEndDate ?? nowUtc;
            var metStatus = completedAt <= deadline ? SlaStatuses.Met : SlaStatuses.Breached;
            return new SlaEvaluation(metStatus, deadline, (deadline - completedAt).TotalHours, IsOpen: false);
        }

        var hoursRemaining = (deadline - nowUtc).TotalHours;
        string status;
        if (hoursRemaining <= 0)
            status = SlaStatuses.Breached;
        else if (hoursRemaining <= policy.TargetHours * AtRiskFraction)
            status = SlaStatuses.AtRisk;
        else
            status = SlaStatuses.OnTrack;

        return new SlaEvaluation(status, deadline, hoursRemaining, IsOpen: true);
    }

    /// <summary>The first enabled policy whose scope matches the task, or null.</summary>
    public static SlaPolicy? FirstMatching(IEnumerable<SlaPolicy> policies, TaskItem task) =>
        policies.FirstOrDefault(p =>
            (p.AppliesToItemType == null || string.Equals(p.AppliesToItemType, task.ItemType, StringComparison.OrdinalIgnoreCase))
         && (p.AppliesToPriority == null || string.Equals(p.AppliesToPriority, task.Priority, StringComparison.OrdinalIgnoreCase)));

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string? Blank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private async Task<SlaPolicy?> FindOwnedAsync(int id, int? teamId, bool isSysAdmin, CancellationToken ct)
    {
        var policy = await db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy == null) return null;
        if (!isSysAdmin && policy.TeamId != teamId) return null;
        return policy;
    }

    private async Task<Dictionary<int, string>> ResolveTeamNamesAsync(IEnumerable<int> teamIds, CancellationToken ct)
    {
        var ids = teamIds.Distinct().ToList();
        return await db.Teams.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
    }
}
