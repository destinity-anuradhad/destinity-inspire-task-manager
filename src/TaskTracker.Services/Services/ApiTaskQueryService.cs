using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public sealed class ApiTaskQueryService(AppDbContext db, IConfiguration config) : IApiTaskQueryService
{
    private static readonly string[] KnownItemTypes = ["Task", "Bug", "Story", "Feature", "Epic"];

    private string AppUrl => (config["AppUrl"] ?? "https://teams.destinity.lk").TrimEnd('/');

    public async Task<ApiMe?> MeAsync(ApiCaller caller, CancellationToken ct = default)
    {
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == caller.UserId, ct);
        if (user == null) return null;
        var teamName = caller.TeamId is int teamId
            ? await db.Teams.AsNoTracking().Where(t => t.Id == teamId).Select(t => t.Name).FirstOrDefaultAsync(ct)
            : null;
        return new ApiMe(user.Id, user.Username, user.DisplayName, user.Email, user.Role,
            caller.TeamId, teamName, ApiTime.ServerTimeZone, ApiTime.ServerUtcOffset);
    }

    public async Task<ApiTaskPage> ListAsync(ApiTaskQuery query, ApiCaller caller, CancellationToken ct = default)
    {
        var serverTime = DateTimeOffset.UtcNow;   // before the read: changes during it show up next time
        var filtered = Filter(query, caller);
        var total = await filtered.CountAsync(ct);

        var sort = query.Sort?.Trim().ToLowerInvariant() ?? (query.UpdatedSinceUtc != null ? "updated" : "created");
        var descending = query.Sort == null && query.UpdatedSinceUtc != null ? false : query.Descending;
        var ordered = (sort, descending) switch
        {
            ("updated", false) => filtered.OrderBy(t => t.UpdatedAt).ThenBy(t => t.Id),
            ("updated", true) => filtered.OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.Id),
            ("due", false) => filtered.OrderBy(t => t.EndDate == null).ThenBy(t => t.EndDate).ThenBy(t => t.Id),
            ("due", true) => filtered.OrderByDescending(t => t.EndDate).ThenByDescending(t => t.Id),
            ("id", false) => filtered.OrderBy(t => t.Id),
            ("id", true) => filtered.OrderByDescending(t => t.Id),
            (_, false) => filtered.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id),
            _ => filtered.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id),
        };
        var pageSize = Math.Clamp(query.PageSize, 1, ApiTaskQuery.MaxPageSize);
        var page = Math.Max(query.Page, 1);
        var tasks = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var mapped = await MapAsync(tasks, ct);
        return new ApiTaskPage(mapped.Select(m => m.Task).ToList(), page, pageSize, total, serverTime);
    }

    public async Task<string> FingerprintAsync(ApiTaskQuery query, ApiCaller caller, CancellationToken ct = default)
    {
        var summary = await Filter(query, caller)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), MaxUpdated = g.Max(t => (DateTime?)t.UpdatedAt), MaxId = g.Max(t => (int?)t.Id) })
            .FirstOrDefaultAsync(ct);
        var text = $"{summary?.Count ?? 0}|{summary?.MaxUpdated:O}|{summary?.MaxId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }

    public async Task<ApiTaskDetail?> GetAsync(int id, ApiCaller caller, CancellationToken ct = default)
    {
        var task = await Visible(db.Tasks.AsNoTracking(), caller).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task == null) return null;

        var summary = (await MapAsync([task], ct))[0].Task;
        return new ApiTaskDetail
        {
            Id = summary.Id,
            Goal = summary.Goal,
            Status = summary.Status,
            StatusCategory = summary.StatusCategory,
            ItemType = summary.ItemType,
            Priority = summary.Priority,
            Owner = summary.Owner,
            OwnerIds = summary.OwnerIds,
            Owners = summary.Owners,
            Project = summary.Project,
            ModuleId = summary.ModuleId,
            ProjectId = summary.ProjectId,
            Property = summary.Property,
            PropertyId = summary.PropertyId,
            SprintId = summary.SprintId,
            Sprint = summary.Sprint,
            DueDate = summary.DueDate,
            StartDate = summary.StartDate,
            EndDate = summary.EndDate,
            TargetDate = summary.TargetDate,
            ActualStartDate = summary.ActualStartDate,
            ActualEndDate = summary.ActualEndDate,
            Weight = summary.Weight,
            EstimateUnit = summary.EstimateUnit,
            ActualHours = summary.ActualHours,
            RemainingWorkHours = summary.RemainingWorkHours,
            ParentId = summary.ParentId,
            TeamId = summary.TeamId,
            Reporter = summary.Reporter,
            Labels = summary.Labels,
            CreatedAt = summary.CreatedAt,
            UpdatedAt = summary.UpdatedAt,
            IsDeleted = summary.IsDeleted,
            WebUrl = summary.WebUrl,
            Notes = task.Notes,
            RelatedFiles = task.RelatedFiles,
            AcceptanceCriteria = task.AcceptanceCriteria,
            WorkflowDefinitionId = task.WorkflowDefinitionId,
            DeletedAt = ApiTime.Local(task.DeletedAt),
            RowVersion = task.RowVersion is { Length: > 0 } ? Convert.ToBase64String(task.RowVersion) : null,
        };
    }

    public Task<bool> IsVisibleAsync(int id, ApiCaller caller, CancellationToken ct = default) =>
        Visible(db.Tasks.AsNoTracking(), caller).AnyAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<ApiHistoryEntry>> HistoryAsync(int taskId, CancellationToken ct = default) =>
        (await db.ActivityLog.AsNoTracking()
            .Where(a => a.TaskId == taskId && a.Action != "Comment")
            .OrderByDescending(a => a.ChangedAt).ThenByDescending(a => a.Id)
            .ToListAsync(ct))
        .Select(a => new ApiHistoryEntry(a.Id, a.Action, a.Details, a.ChangedBy, ApiTime.Local(a.ChangedAt)))
        .ToList();

    public async Task<IReadOnlyList<ApiComment>> CommentsAsync(int taskId, CancellationToken ct = default) =>
        (await db.ActivityLog.AsNoTracking()
            .Where(a => a.TaskId == taskId && a.Action == "Comment")
            .OrderBy(a => a.ChangedAt).ThenBy(a => a.Id)
            .ToListAsync(ct))
        .Select(a => new ApiComment(a.Id, a.ChangedBy, a.Details, ApiTime.Local(a.ChangedAt)))
        .ToList();

    public async Task<IReadOnlyList<ApiTimeEntry>> TimeEntriesForTaskAsync(int taskId, CancellationToken ct = default) =>
        await MapTimeEntriesAsync(db.TimeEntries.AsNoTracking().Where(e => e.TaskId == taskId), ct);

    public async Task<IReadOnlyList<ApiTimeEntry>> TimeEntriesForUserAsync(int userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.ToDateTime(TimeOnly.MinValue).AddDays(1);
        return await MapTimeEntriesAsync(
            db.TimeEntries.AsNoTracking().Where(e => e.UserId == userId && e.WorkDate >= start && e.WorkDate < end), ct);
    }

    public async Task<IReadOnlyList<ApiLookup>> ModulesAsync(ApiCaller caller, CancellationToken ct = default) =>
        Lookup((await TeamTasks(caller)
                .Where(t => t.ModuleId != null)
                .Select(t => new { Id = t.ModuleId!.Value, Name = t.Project })
                .ToListAsync(ct))
            .Select(x => (x.Id, x.Name)));

    public async Task<IReadOnlyList<ApiLookup>> PropertiesAsync(ApiCaller caller, CancellationToken ct = default) =>
        Lookup((await TeamTasks(caller)
                .Where(t => t.ClientId != null)
                .Select(t => new { Id = t.ClientId!.Value, Name = t.Property })
                .ToListAsync(ct))
            .Select(x => (x.Id, x.Name)));

    public async Task<IReadOnlyList<ApiSprint>> SprintsAsync(ApiCaller caller, bool activeOnly, CancellationToken ct = default)
    {
        var sprints = db.Sprints.AsNoTracking().AsQueryable();
        if (caller.TeamId is int teamId) sprints = sprints.Where(s => s.TeamId == teamId);
        if (activeOnly) sprints = sprints.Where(s => s.IsActive);
        return (await sprints.OrderByDescending(s => s.StartDate).ThenByDescending(s => s.Id).ToListAsync(ct))
            .Select(ToApi).ToList();
    }

    // ── filtering ───────────────────────────────────────────────────────────

    // Team boundary and, for own-tasks-only keys, the caller must be one of the task's owners.
    private IQueryable<TaskItem> Visible(IQueryable<TaskItem> tasks, ApiCaller caller)
    {
        if (caller.TeamId is int teamId) tasks = tasks.Where(t => t.TeamId == teamId);
        if (caller.OwnTasksOnly)
            tasks = tasks.Where(t => db.TaskOwners.Any(o => o.TaskId == t.Id && o.UserId == caller.UserId));
        return tasks;
    }

    private IQueryable<TaskItem> TeamTasks(ApiCaller caller)
    {
        var tasks = db.Tasks.AsNoTracking();
        return caller.TeamId is int teamId ? tasks.Where(t => t.TeamId == teamId) : tasks;
    }

    private IQueryable<TaskItem> Filter(ApiTaskQuery q, ApiCaller caller)
    {
        var tasks = Visible((q.IncludeDeleted ? db.Tasks.IgnoreQueryFilters() : db.Tasks).AsNoTracking(), caller);

        if (q.Ids.Count > 0)
        {
            var ids = q.Ids.Take(ApiTaskQuery.MaxIds).ToList();
            tasks = tasks.Where(t => ids.Contains(t.Id));
        }

        if (q.OwnerIds.Count > 0)
        {
            var ownerIds = q.OwnerIds.ToList();
            tasks = tasks.Where(t => db.TaskOwners.Any(o => o.TaskId == t.Id && ownerIds.Contains(o.UserId)));
        }

        if (!string.IsNullOrWhiteSpace(q.OwnerName))
            tasks = tasks.Where(t => t.Owner != null && t.Owner.Contains(q.OwnerName));

        if (q.Statuses.Count > 0)
        {
            var statuses = q.Statuses.ToList();
            tasks = tasks.Where(t => statuses.Contains(t.Status ?? ""));
        }

        if (q.ItemTypes.Count > 0)
        {
            var itemTypes = q.ItemTypes.ToList();
            tasks = tasks.Where(t => t.ItemType != null && itemTypes.Contains(t.ItemType));
        }

        if (q.Priorities.Count > 0)
        {
            var priorities = q.Priorities.ToList();
            tasks = tasks.Where(t => t.Priority != null && priorities.Contains(t.Priority));
        }

        if (q.Open is bool open) tasks = WhereOpen(tasks, open);

        switch (q.Sprint?.Trim().ToLowerInvariant())
        {
            case null or "":
                break;
            case "backlog":
                tasks = tasks.Where(t => t.SprintId == null);
                break;
            case "active":
                var active = db.Sprints.Where(s => s.IsActive);
                if (caller.TeamId is int teamId) active = active.Where(s => s.TeamId == teamId);
                tasks = tasks.Where(t => t.SprintId != null && active.Any(s => s.Id == t.SprintId));
                break;
            default:
                var sprintId = int.TryParse(q.Sprint, out var parsed) ? parsed : -1;
                tasks = tasks.Where(t => t.SprintId == sprintId);
                break;
        }

        if (q.ProjectId is int projectId) tasks = tasks.Where(t => t.ProjectId == projectId);
        if (q.ModuleId is int moduleId) tasks = tasks.Where(t => t.ModuleId == moduleId);
        if (q.PropertyId is int propertyId) tasks = tasks.Where(t => t.ClientId == propertyId);
        if (!string.IsNullOrWhiteSpace(q.Project)) tasks = tasks.Where(t => t.Project == q.Project);
        if (!string.IsNullOrWhiteSpace(q.Property)) tasks = tasks.Where(t => t.Property == q.Property);

        if (!string.IsNullOrWhiteSpace(q.Search))
            tasks = tasks.Where(t =>
                (t.Goal != null && t.Goal.Contains(q.Search)) ||
                (t.Labels != null && t.Labels.Contains(q.Search)) ||
                (t.Project != null && t.Project.Contains(q.Search)) ||
                (t.Property != null && t.Property.Contains(q.Search)));

        // Due date = EndDate, the task form's "Due Date".
        if (q.DueFrom is { } dueFrom) tasks = tasks.Where(t => t.EndDate >= dueFrom.Date);
        if (q.DueTo is { } dueTo) tasks = tasks.Where(t => t.EndDate <= dueTo.Date);
        if (q.UpdatedSinceUtc is { } since) tasks = tasks.Where(t => t.UpdatedAt >= since);

        return tasks;
    }

    // Done or cancelled, as SQL. Mirrors TaskStatusCategories.For: Rejected is cancelled; Blocked and
    // On hold are open; otherwise the pinned workflow state's category when the status is one of its
    // states, else TaskStatusRules.IsDone.
    private IQueryable<TaskItem> WhereOpen(IQueryable<TaskItem> tasks, bool open)
    {
        var states = db.WorkflowStates;
        Expression<Func<TaskItem, bool>> closed = t =>
            t.Status == "Rejected"
            || (t.Status != "Blocked" && t.Status != "On hold" && (
                states.Any(s => s.WorkflowDefinitionId == t.WorkflowDefinitionId && s.Name == t.Status && s.Category == "Done")
                || (!states.Any(s => s.WorkflowDefinitionId == t.WorkflowDefinitionId && s.Name == t.Status)
                    && ((t.ItemType == "Task" && t.Status == "Done")
                        || ((t.ItemType == "Bug" || t.ItemType == "Story" || t.ItemType == "Feature") && (t.Status == "Resolved" || t.Status == "Closed"))
                        || (t.ItemType == "Epic" && t.Status == "Closed")
                        || ((t.ItemType == null || !KnownItemTypes.Contains(t.ItemType))
                            && (t.Status == "Done" || t.Status == "Resolved" || t.Status == "Closed"))))));

        return tasks.Where(open ? Expression.Lambda<Func<TaskItem, bool>>(Expression.Not(closed.Body), closed.Parameters) : closed);
    }

    // ── mapping ─────────────────────────────────────────────────────────────

    private async Task<List<(TaskItem Item, ApiTask Task)>> MapAsync(IReadOnlyList<TaskItem> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return [];
        var ids = tasks.Select(t => t.Id).ToList();

        var owners = (await db.TaskOwners.AsNoTracking()
                .Where(o => ids.Contains(o.TaskId))
                .Join(db.AppUsers.AsNoTracking(), o => o.UserId, u => u.Id, (o, u) => new { o.TaskId, u.Id, u.DisplayName })
                .ToListAsync(ct))
            .ToLookup(x => x.TaskId, x => new ApiPerson(x.Id, x.DisplayName));

        var sprintIds = tasks.Where(t => t.SprintId != null).Select(t => t.SprintId!.Value).Distinct().ToList();
        var sprints = sprintIds.Count == 0
            ? new Dictionary<int, ApiSprint>()
            : (await db.Sprints.AsNoTracking().Where(s => sprintIds.Contains(s.Id)).ToListAsync(ct)).ToDictionary(s => s.Id, ToApi);

        var workflowIds = tasks.Where(t => t.WorkflowDefinitionId != null).Select(t => t.WorkflowDefinitionId!.Value).Distinct().ToList();
        var states = workflowIds.Count == 0
            ? []
            : await db.WorkflowStates.AsNoTracking().Where(s => workflowIds.Contains(s.WorkflowDefinitionId)).ToListAsync(ct);
        var categories = states
            .GroupBy(s => (s.WorkflowDefinitionId, Name: s.Name.ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => g.First().Category);

        return tasks.Select(t =>
        {
            var category = t.WorkflowDefinitionId is int wf && categories.TryGetValue((wf, (t.Status ?? "").ToLowerInvariant()), out var c) ? c : null;
            var taskOwners = owners[t.Id].ToList();
            return (t, new ApiTask
            {
                Id = t.Id,
                Goal = t.Goal,
                Status = t.Status,
                StatusCategory = TaskStatusCategories.For(t.ItemType, t.Status, category),
                ItemType = t.ItemType,
                Priority = t.Priority,
                Owner = t.Owner,
                OwnerIds = taskOwners.Select(o => o.Id).ToList(),
                Owners = taskOwners,
                Project = t.Project,
                ModuleId = t.ModuleId,
                ProjectId = t.ProjectId,
                Property = t.Property,
                PropertyId = t.ClientId,
                SprintId = t.SprintId,
                Sprint = t.SprintId is int sid ? sprints.GetValueOrDefault(sid) : null,
                DueDate = ApiTime.Date(t.EndDate),
                StartDate = ApiTime.Date(t.StartDate),
                EndDate = ApiTime.Date(t.EndDate),
                TargetDate = ApiTime.Date(t.TargetDate),
                ActualStartDate = ApiTime.Utc(t.ActualStartDate),
                ActualEndDate = ApiTime.Utc(t.ActualEndDate),
                Weight = t.Weight,
                EstimateUnit = t.EstimateUnit,
                ActualHours = t.ActualHours,
                RemainingWorkHours = t.RemainingWorkHours,
                ParentId = t.ParentId,
                TeamId = t.TeamId,
                Reporter = t.Reporter,
                Labels = t.Labels,
                CreatedAt = ApiTime.Local(t.CreatedAt),
                UpdatedAt = ApiTime.Utc(t.UpdatedAt),
                IsDeleted = t.IsDeleted,
                WebUrl = $"{AppUrl}/Tasks?highlight={t.Id}",
            });
        }).ToList();
    }

    private async Task<IReadOnlyList<ApiTimeEntry>> MapTimeEntriesAsync(IQueryable<TimeEntry> entries, CancellationToken ct)
    {
        var rows = await entries.OrderBy(e => e.WorkDate).ThenBy(e => e.Id).ToListAsync(ct);
        var userIds = rows.Where(e => e.UserId != null).Select(e => e.UserId!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.AppUsers.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return rows.Select(e => new ApiTimeEntry(
                e.Id, e.TaskId, e.UserId, e.UserId is int uid ? names.GetValueOrDefault(uid) : e.CreatedBy,
                e.Hours, DateOnly.FromDateTime(e.WorkDate), e.EntryType, e.Note, e.ReversesEntryId, ApiTime.Utc(e.CreatedAt)))
            .ToList();
    }

    // One entry per id, named by the name most tasks use (names drift, e.g. "Browns" vs "Browns Hotels & Resorts").
    private static IReadOnlyList<ApiLookup> Lookup(IEnumerable<(int Id, string? Name)> rows) =>
        rows.GroupBy(r => r.Id)
            .Select(g => new ApiLookup(
                g.Key,
                g.Where(r => !string.IsNullOrWhiteSpace(r.Name))
                    .GroupBy(r => r.Name!.Trim())
                    .OrderByDescending(n => n.Count()).ThenBy(n => n.Key)
                    .Select(n => n.Key)
                    .FirstOrDefault() ?? "",
                g.Count()))
            .OrderBy(l => l.Name)
            .ToList();

    private static ApiSprint ToApi(Sprint s) => new(s.Id, s.Name, ApiTime.Date(s.StartDate), ApiTime.Date(s.EndDate), s.IsActive);
}
