namespace TaskTracker.Services;

/// <summary>
/// System-administrator audit-log export and retention controls (Phase 4 / compliance). Export is
/// read-only; retention purge is destructive but guarded, explicit, and itself audited.
/// </summary>
public interface IAuditExportService
{
    /// <summary>Filtered audit rows for export (newest first), capped at <see cref="AuditExportService.MaxRows"/>.</summary>
    Task<AuditExportResult> ExportAsync(AuditExportFilter filter, CancellationToken ct = default);

    /// <summary>How many audit entries are strictly older than <paramref name="cutoffUtc"/>. Read-only.</summary>
    Task<int> CountOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes audit entries older than <paramref name="cutoffUtc"/> and writes a single
    /// audit entry recording the purge (who, cutoff, count). Returns the number deleted.
    /// </summary>
    Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, string performedBy, CancellationToken ct = default);
}

public sealed record AuditExportFilter(
    DateTime? FromUtc = null, DateTime? ToUtc = null,
    string? Category = null, string? User = null, int? TeamId = null);

public sealed record AuditExportRow(
    int Id, DateTime ChangedAt, string ChangedBy, string Category, string Action,
    string Details, int? TaskId, int? TeamId, string Project, string? IpAddress);

public sealed record AuditExportResult(IReadOnlyList<AuditExportRow> Rows, bool Truncated, int TotalMatched);
