namespace TaskTracker.Services;

/// <summary>
/// Two-phase CSV task import: <see cref="PreviewAsync"/> parses + validates a file into a persisted
/// job (writing no tasks), then <see cref="ApplyAsync"/> creates the valid rows through the governed
/// task command. Idempotent on an optional per-row <c>ExternalKey</c> and auditable via the job/rows.
/// </summary>
public interface IImportService
{
    Task<ImportPreviewResult> PreviewAsync(int teamId, int userId, string createdBy, string fileName, Stream csv, CancellationToken ct = default);
    Task<ImportPreviewResult?> GetJobAsync(int jobId, int teamId, bool isSysAdmin, CancellationToken ct = default);
    Task<ImportApplyResult?> ApplyAsync(int jobId, UserCtx ctx, bool isSysAdmin, CancellationToken ct = default);
    Task<IReadOnlyList<ImportJobSummary>> ListJobsAsync(int? teamId, bool includeGlobal, int maxCount = 25, CancellationToken ct = default);
}

public sealed record ImportPreviewResult(ImportJobSummary Job, IReadOnlyList<ImportRowView> Rows);

public sealed record ImportApplyResult(ImportJobSummary Job, int Created, int Skipped, int Failed);

public sealed record ImportJobSummary(
    int Id, int TeamId, string FileName, string Status,
    int TotalRows, int ValidRows, int ErrorRows, int CreatedRows, int SkippedRows, int FailedRows,
    string? CreatedBy, DateTime CreatedAt, DateTime? AppliedAt);

public sealed record ImportRowView(
    long Id, int RowNumber, string Goal, string ItemType, string Status,
    string? ExternalKey, string RowStatus, string? Message, int? CreatedTaskId);
