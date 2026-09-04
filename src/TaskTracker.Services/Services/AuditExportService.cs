using System.Text;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

/// <summary>
/// Exports and prunes the <see cref="ActivityLog"/> audit trail. All access is System-Admin gated at
/// the controller; this service is tenant-wide by design (compliance export spans all teams).
/// </summary>
public sealed class AuditExportService(AppDbContext db) : IAuditExportService
{
    /// <summary>Upper bound on a single export so a huge trail can't exhaust memory; the caller is told
    /// when the result was truncated.</summary>
    public const int MaxRows = 100_000;

    public async Task<AuditExportResult> ExportAsync(AuditExportFilter filter, CancellationToken ct = default)
    {
        var query = Filtered(filter);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(a => a.ChangedAt).ThenByDescending(a => a.Id)
            .Take(MaxRows)
            .Select(a => new AuditExportRow(
                a.Id, a.ChangedAt, a.ChangedBy, a.Category, a.Action, a.Details,
                a.TaskId, a.TeamId, a.Project, a.IpAddress))
            .ToListAsync(ct);

        return new AuditExportResult(rows, Truncated: total > MaxRows, TotalMatched: total);
    }

    public Task<int> CountOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
        db.ActivityLog.Where(a => a.ChangedAt < cutoffUtc).CountAsync(ct);

    public async Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, string performedBy, CancellationToken ct = default)
    {
        // Load-and-remove (matches EmptyBinAsync) so retention works on every provider; a retention
        // purge is an infrequent admin action, so materializing the doomed rows is acceptable.
        var doomed = await db.ActivityLog.Where(a => a.ChangedAt < cutoffUtc).ToListAsync(ct);
        db.ActivityLog.RemoveRange(doomed);
        var deleted = doomed.Count;

        // The deletion is itself an audited event — a compliance requirement.
        db.ActivityLog.Add(new ActivityLog
        {
            ChangedBy = performedBy, Action = "Audit Retention Purge", Category = "System",
            Details = $"Deleted {deleted} audit entrie(s) older than {cutoffUtc:yyyy-MM-dd HH:mm} UTC.",
            TaskGoal = "", Project = "", ChangedAt = DateTime.Now,
        });
        await db.SaveChangesAsync(ct);
        return deleted;
    }

    private IQueryable<ActivityLog> Filtered(AuditExportFilter f)
    {
        var q = db.ActivityLog.AsNoTracking().AsQueryable();
        if (f.FromUtc is { } from) q = q.Where(a => a.ChangedAt >= from);
        if (f.ToUtc   is { } to)   q = q.Where(a => a.ChangedAt < to);
        if (!string.IsNullOrWhiteSpace(f.Category)) q = q.Where(a => a.Category == f.Category);
        if (!string.IsNullOrWhiteSpace(f.User))     q = q.Where(a => a.ChangedBy == f.User);
        if (f.TeamId is { } tid) q = q.Where(a => a.TeamId == tid);
        return q;
    }

    // ── CSV serialization (pure) ──────────────────────────────────────────────

    private static readonly string[] Header =
        ["Id", "ChangedAtUtc", "ChangedBy", "Category", "Action", "Details", "TaskId", "TeamId", "Project", "IpAddress"];

    public static byte[] ToCsv(IEnumerable<AuditExportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Header));
        foreach (var r in rows)
            sb.AppendLine(string.Join(",",
                r.Id, Csv(r.ChangedAt.ToString("yyyy-MM-dd HH:mm:ss")), Csv(r.ChangedBy), Csv(r.Category),
                Csv(r.Action), Csv(r.Details), r.TaskId?.ToString() ?? "", r.TeamId?.ToString() ?? "",
                Csv(r.Project), Csv(r.IpAddress)));
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Csv(string? s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
}
