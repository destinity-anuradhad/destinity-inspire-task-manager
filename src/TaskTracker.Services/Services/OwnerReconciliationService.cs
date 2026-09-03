using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;

namespace TaskTracker.Services;

public sealed record OwnerReconciliationRow(
    int TaskId, string? OwnerString, string JunctionUserIds, string ResolvedStringUserIds, string Issue);

public sealed record OwnerReconciliationReport(
    int TasksChecked, int Mismatches, int TasksWithUnresolvableNames, IReadOnlyList<OwnerReconciliationRow> Rows)
{
    /// <summary>True when the junction and the legacy string agree for every task and every owner name
    /// resolves to exactly one user — i.e. the string can be safely retired.</summary>
    public bool IsClean => Mismatches == 0 && TasksWithUnresolvableNames == 0;
}

/// <summary>
/// Backlog #7 verification gate. For every (non-deleted) task, compares the owner **user-id set** from
/// the <c>TaskOwners</c> junction against the ids the legacy <c>Owner</c> string resolves to by display
/// name. Reports any disagreement, plus any owner name that resolves to zero or multiple users. This is
/// entirely read-only — it changes nothing — and a clean result (zero mismatches, zero unresolvable
/// names) is the evidence needed before the Stage-2 readers migrate and the column is dropped.
/// </summary>
public sealed class OwnerReconciliationService(AppDbContext db)
{
    private static readonly char[] Separators = [','];

    public async Task<OwnerReconciliationReport> ReconcileAsync(CancellationToken ct = default)
    {
        var tasks = await db.Tasks.AsNoTracking()
            .Select(t => new { t.Id, t.Owner })
            .ToListAsync(ct);

        var ownersByTask = (await db.TaskOwners.AsNoTracking()
                .Select(o => new { o.TaskId, o.UserId }).ToListAsync(ct))
            .GroupBy(o => o.TaskId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).ToHashSet());

        // A display name is "resolvable" only if it maps to exactly one user; 0 or >1 is a problem.
        var idsByName = (await db.AppUsers.AsNoTracking()
                .Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct))
            .Where(u => !string.IsNullOrWhiteSpace(u.DisplayName))
            .GroupBy(u => u.DisplayName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).Distinct().ToList(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<OwnerReconciliationRow>();
        var mismatches = 0;
        var unresolvable = 0;

        foreach (var t in tasks)
        {
            var junction = ownersByTask.GetValueOrDefault(t.Id, []);

            var names = (t.Owner ?? "")
                .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var resolved = new HashSet<int>();
            var badNames = new List<string>();
            foreach (var name in names)
            {
                if (idsByName.TryGetValue(name, out var ids) && ids.Count == 1)
                    resolved.Add(ids[0]);
                else
                    badNames.Add(name);   // unknown or ambiguous
            }

            var hasUnresolvable = badNames.Count > 0;
            var setsAgree = junction.SetEquals(resolved);
            if (hasUnresolvable) unresolvable++;
            if (!setsAgree) mismatches++;

            if (hasUnresolvable || !setsAgree)
            {
                var issue = string.Join("; ", new[]
                {
                    setsAgree ? null : "junction != string",
                    badNames.Count > 0 ? $"unresolvable: {string.Join("|", badNames)}" : null,
                }.Where(s => s is not null));

                rows.Add(new OwnerReconciliationRow(
                    t.Id, t.Owner,
                    string.Join(",", junction.OrderBy(x => x)),
                    string.Join(",", resolved.OrderBy(x => x)),
                    issue));
            }
        }

        return new OwnerReconciliationReport(tasks.Count, mismatches, unresolvable, rows);
    }
}
