using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public sealed class ImportService(AppDbContext db, ITaskService tasks) : IImportService
{
    private const int MaxRows = 2000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ItemTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Epic", "Feature", "Story", "Task", "Bug" };

    private sealed record RowFields(
        string Goal, string ItemType, string Status, string? Priority, string? Owner,
        DateTime? TargetDate, decimal? Weight, string? EstimateUnit, string? Labels, string? ExternalKey);

    // ── Preview ─────────────────────────────────────────────────────────────

    public async Task<ImportPreviewResult> PreviewAsync(
        int teamId, int userId, string createdBy, string fileName, Stream csv, CancellationToken ct = default)
    {
        using var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);
        var records = ParseCsv(text);

        if (records.Count == 0)
            throw new ArgumentException("The file is empty.");

        var header = BuildHeaderMap(records[0]);
        if (!header.ContainsKey("goal"))
            throw new ArgumentException("The header row must include a 'Goal' column.");

        var statuses = await db.WorkStates.AsNoTracking()
            .OrderBy(w => w.SortOrder).Select(w => w.Name).ToListAsync(ct);
        var statusSet = new HashSet<string>(statuses, StringComparer.OrdinalIgnoreCase);
        var defaultStatus = statuses.FirstOrDefault() ?? "New";

        var dataRows = records.Skip(1).Take(MaxRows).ToList();
        var job = new ImportJob
        {
            TeamId = teamId, FileName = fileName, Status = "Preview",
            CreatedByUserId = userId, CreatedBy = createdBy, CreatedAt = DateTime.UtcNow,
            TotalRows = dataRows.Count,
        };
        db.ImportJobs.Add(job);
        await db.SaveChangesAsync(ct);

        var views = new List<ImportRowView>();
        var rowNumber = 1;   // 1-based data row number (header excluded)
        foreach (var cells in dataRows)
        {
            var (fields, error) = Validate(cells, header, statusSet, defaultStatus);
            var status = error == null ? "Valid" : "Invalid";
            if (error == null) job.ValidRows++; else job.ErrorRows++;

            var entity = new ImportRow
            {
                ImportJobId = job.Id, RowNumber = rowNumber, Status = status, Message = error,
                ExternalKey = string.IsNullOrWhiteSpace(fields.ExternalKey) ? null : fields.ExternalKey.Trim(),
                Payload = JsonSerializer.Serialize(fields, JsonOptions),
            };
            db.ImportRows.Add(entity);
            views.Add(new ImportRowView(0, rowNumber, fields.Goal, fields.ItemType, fields.Status,
                entity.ExternalKey, status, error, null));
            rowNumber++;
        }
        await db.SaveChangesAsync(ct);

        return new ImportPreviewResult(ToSummary(job), views);
    }

    // ── Apply ───────────────────────────────────────────────────────────────

    public async Task<ImportApplyResult?> ApplyAsync(int jobId, UserCtx ctx, bool isSysAdmin, CancellationToken ct = default)
    {
        var job = await db.ImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null) return null;
        if (!isSysAdmin && job.TeamId != ctx.TeamId) return null;
        if (job.Status == "Applied")
            return new ImportApplyResult(ToSummary(job), job.CreatedRows, job.SkippedRows, job.FailedRows);

        var rows = await db.ImportRows.Where(r => r.ImportJobId == jobId).OrderBy(r => r.RowNumber).ToListAsync(ct);

        // Idempotency: external keys already turned into tasks for this team are skipped.
        var priorKeys = await (
            from r in db.ImportRows
            join j in db.ImportJobs on r.ImportJobId equals j.Id
            where j.TeamId == job.TeamId && r.CreatedTaskId != null && r.ExternalKey != null && r.ExternalKey != ""
            select r.ExternalKey!).ToListAsync(ct);
        var seen = new HashSet<string>(priorKeys, StringComparer.OrdinalIgnoreCase);

        int created = 0, skipped = 0, failed = 0;
        foreach (var row in rows)
        {
            if (row.Status != "Valid") continue;
            var f = JsonSerializer.Deserialize<RowFields>(row.Payload, JsonOptions);
            if (f == null) { row.Status = "Failed"; row.Message = "Corrupt row payload."; failed++; continue; }

            if (!string.IsNullOrWhiteSpace(f.ExternalKey) && seen.Contains(f.ExternalKey))
            {
                row.Status = "Skipped";
                row.Message = "Duplicate external key — already imported.";
                skipped++;
                continue;
            }

            var task = new TaskItem
            {
                Goal = f.Goal, ItemType = f.ItemType, Status = f.Status, Priority = f.Priority,
                Owner = string.IsNullOrWhiteSpace(f.Owner) ? null : f.Owner,
                TargetDate = f.TargetDate, Weight = f.Weight,
                EstimateUnit = string.IsNullOrWhiteSpace(f.EstimateUnit) ? "Points" : f.EstimateUnit!,
                Labels = f.Labels,
                RowVersion = [],   // store-generated on SQL Server; set so the in-memory provider accepts the insert
            };
            var result = await tasks.SaveAsync(task, ctx, forceClose: false);
            if (result.Success)
            {
                row.Status = "Created"; row.CreatedTaskId = result.TaskId; row.Message = null;
                created++;
                if (!string.IsNullOrWhiteSpace(f.ExternalKey)) seen.Add(f.ExternalKey);
            }
            else
            {
                row.Status = "Failed";
                row.Message = result.Error?[..Math.Min(result.Error?.Length ?? 0, 500)];
                failed++;
            }
        }

        job.CreatedRows = created;
        job.SkippedRows = skipped;
        job.FailedRows = failed;
        job.Status = "Applied";
        job.AppliedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new ImportApplyResult(ToSummary(job), created, skipped, failed);
    }

    // ── Views ───────────────────────────────────────────────────────────────

    public async Task<ImportPreviewResult?> GetJobAsync(int jobId, int teamId, bool isSysAdmin, CancellationToken ct = default)
    {
        var job = await db.ImportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null) return null;
        if (!isSysAdmin && job.TeamId != teamId) return null;

        var rows = await db.ImportRows.AsNoTracking()
            .Where(r => r.ImportJobId == jobId).OrderBy(r => r.RowNumber).ToListAsync(ct);

        var views = rows.Select(r =>
        {
            var f = JsonSerializer.Deserialize<RowFields>(r.Payload, JsonOptions);
            return new ImportRowView(r.Id, r.RowNumber, f?.Goal ?? "", f?.ItemType ?? "", f?.Status ?? "",
                r.ExternalKey, r.Status, r.Message, r.CreatedTaskId);
        }).ToList();

        return new ImportPreviewResult(ToSummary(job), views);
    }

    public async Task<IReadOnlyList<ImportJobSummary>> ListJobsAsync(int? teamId, bool includeGlobal, int maxCount = 25, CancellationToken ct = default)
    {
        var query = db.ImportJobs.AsNoTracking();
        if (!includeGlobal)
        {
            if (teamId is null) return [];
            query = query.Where(j => j.TeamId == teamId);
        }
        var jobs = await query.OrderByDescending(j => j.Id).Take(Math.Clamp(maxCount, 1, 200)).ToListAsync(ct);
        return jobs.Select(ToSummary).ToList();
    }

    // ── Parsing & validation ─────────────────────────────────────────────────

    private static (RowFields Fields, string? Error) Validate(
        string[] cells, IReadOnlyDictionary<string, int> header, HashSet<string> statusSet, string defaultStatus)
    {
        string Get(string name) =>
            header.TryGetValue(name, out var i) && i < cells.Length ? cells[i].Trim() : "";

        var errors = new List<string>();

        var goal = Get("goal");
        if (goal.Length == 0) errors.Add("Goal is required");

        var itemType = Get("itemtype");
        if (itemType.Length == 0) itemType = "Task";
        else if (!ItemTypes.Contains(itemType)) errors.Add($"Unknown item type '{itemType}'");

        var status = Get("status");
        if (status.Length == 0) status = defaultStatus;
        else if (!statusSet.Contains(status)) errors.Add($"Unknown status '{status}'");

        DateTime? targetDate = null;
        var rawDate = Get("targetdate");
        if (rawDate.Length > 0)
        {
            if (DateTime.TryParse(rawDate, out var d)) targetDate = d.Date;
            else errors.Add($"Invalid date '{rawDate}'");
        }

        decimal? weight = null;
        var rawWeight = Get("weight");
        if (rawWeight.Length > 0)
        {
            if (decimal.TryParse(rawWeight, out var w)) weight = w;
            else errors.Add($"Invalid weight '{rawWeight}'");
        }

        var fields = new RowFields(
            goal, itemType, status,
            NullIfEmpty(Get("priority")), NullIfEmpty(Get("owner")),
            targetDate, weight, NullIfEmpty(Get("estimateunit")), NullIfEmpty(Get("labels")),
            NullIfEmpty(Get("externalkey")));

        return (fields, errors.Count == 0 ? null : string.Join("; ", errors));
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static Dictionary<string, int> BuildHeaderMap(string[] headerCells)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerCells.Length; i++)
        {
            var key = headerCells[i].Trim().Replace(" ", "").ToLowerInvariant();
            if (key.Length > 0 && !map.ContainsKey(key)) map[key] = i;
        }
        return map;
    }

    /// <summary>Minimal RFC-4180-style CSV reader: quoted fields, escaped quotes, embedded commas/newlines.</summary>
    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var current = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': current.Add(field.ToString()); field.Clear(); break;
                    case '\r': break;
                    case '\n':
                        current.Add(field.ToString()); field.Clear();
                        rows.Add(current.ToArray()); current = [];
                        break;
                    default: field.Append(c); break;
                }
            }
        }
        if (field.Length > 0 || current.Count > 0)
        {
            current.Add(field.ToString());
            rows.Add(current.ToArray());
        }
        // Drop trailing fully-empty rows.
        return rows.Where(r => r.Any(c => c.Trim().Length > 0)).ToList();
    }

    private static ImportJobSummary ToSummary(ImportJob j) => new(
        j.Id, j.TeamId, j.FileName, j.Status, j.TotalRows, j.ValidRows, j.ErrorRows,
        j.CreatedRows, j.SkippedRows, j.FailedRows, j.CreatedBy, j.CreatedAt, j.AppliedAt);
}
