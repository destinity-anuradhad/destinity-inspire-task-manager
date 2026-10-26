namespace TaskTracker.Services;

/// <summary>
/// Read side of the v1 integration API (<c>/api/v1</c>). Separate from <see cref="ITaskService"/>'s
/// UI-oriented queries so integrations get stable shapes, exact owner ids, explicit time zones and
/// incremental sync without changing what the UI receives.
/// </summary>
public interface IApiTaskQueryService
{
    Task<ApiMe?> MeAsync(ApiCaller caller, CancellationToken ct = default);
    Task<ApiTaskPage> ListAsync(ApiTaskQuery query, ApiCaller caller, CancellationToken ct = default);

    /// <summary>A cheap summary of the tasks a query matches, for ETags: changes whenever any of them changes.</summary>
    Task<string> FingerprintAsync(ApiTaskQuery query, ApiCaller caller, CancellationToken ct = default);

    Task<ApiTaskDetail?> GetAsync(int id, ApiCaller caller, CancellationToken ct = default);

    /// <summary>Whether the caller may see (and so act on) the task: same team and, for own-tasks-only keys, an owner.</summary>
    Task<bool> IsVisibleAsync(int id, ApiCaller caller, CancellationToken ct = default);

    Task<IReadOnlyList<ApiHistoryEntry>> HistoryAsync(int taskId, CancellationToken ct = default);
    Task<IReadOnlyList<ApiComment>> CommentsAsync(int taskId, CancellationToken ct = default);
    Task<IReadOnlyList<ApiTimeEntry>> TimeEntriesForTaskAsync(int taskId, CancellationToken ct = default);
    Task<IReadOnlyList<ApiTimeEntry>> TimeEntriesForUserAsync(int userId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<ApiLookup>> ModulesAsync(ApiCaller caller, CancellationToken ct = default);
    Task<IReadOnlyList<ApiLookup>> PropertiesAsync(ApiCaller caller, CancellationToken ct = default);
    Task<IReadOnlyList<ApiSprint>> SprintsAsync(ApiCaller caller, bool activeOnly, CancellationToken ct = default);
}

/// <summary>Who is calling the API: the user, their active team (null = system admin, all teams) and key limits.</summary>
public sealed record ApiCaller(int UserId, int? TeamId, bool OwnTasksOnly = false);

/// <summary>Filters for <c>GET /api/v1/tasks</c>. Every list is "any of".</summary>
public sealed class ApiTaskQuery
{
    public const int MaxPageSize = 100;
    public const int MaxIds = 200;

    public IReadOnlyList<int> OwnerIds { get; init; } = [];
    /// <summary>Legacy loose match on the owner display string (<c>owner=</c>); prefer <see cref="OwnerIds"/>.</summary>
    public string? OwnerName { get; init; }
    public IReadOnlyList<string> Statuses { get; init; } = [];
    public IReadOnlyList<string> ItemTypes { get; init; } = [];
    public IReadOnlyList<string> Priorities { get; init; } = [];
    /// <summary>true = not done/cancelled (per item type and workflow); false = done or cancelled.</summary>
    public bool? Open { get; init; }
    /// <summary>A sprint id, "active" (the team's active sprints) or "backlog" (no sprint).</summary>
    public string? Sprint { get; init; }
    public int? ProjectId { get; init; }
    public int? ModuleId { get; init; }
    public int? PropertyId { get; init; }
    public string? Project { get; init; }
    public string? Property { get; init; }
    public string? Search { get; init; }
    public DateTime? DueFrom { get; init; }
    public DateTime? DueTo { get; init; }
    public DateTime? UpdatedSinceUtc { get; init; }
    public IReadOnlyList<int> Ids { get; init; } = [];
    public bool IncludeDeleted { get; init; }
    /// <summary>created (default) | updated | due | id. With updatedSince the default is updated, ascending.</summary>
    public string? Sort { get; init; }
    public bool Descending { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record ApiMe(
    int UserId, string Username, string DisplayName, string? Email, string Role,
    int? TeamId, string? TeamName, string ServerTimeZone, string ServerUtcOffset);

public sealed record ApiPerson(int Id, string DisplayName);

public sealed record ApiSprint(int Id, string Name, DateOnly? StartDate, DateOnly? EndDate, bool IsActive);

public sealed record ApiLookup(int Id, string Name, int TaskCount);

/// <summary>A task as the v1 API returns it in lists. Dates are <c>yyyy-MM-dd</c>; instants carry an offset.</summary>
public record ApiTask
{
    public int Id { get; init; }
    public string? Goal { get; init; }
    public string? Status { get; init; }
    /// <summary>todo | in_progress | blocked | done | cancelled</summary>
    public string StatusCategory { get; init; } = TaskStatusCategories.ToDo;
    public string? ItemType { get; init; }
    public string? Priority { get; init; }
    /// <summary>Display names, comma-separated (kept for older clients; use <see cref="OwnerIds"/>).</summary>
    public string? Owner { get; init; }
    public IReadOnlyList<int> OwnerIds { get; init; } = [];
    public IReadOnlyList<ApiPerson> Owners { get; init; } = [];
    /// <summary>The module name, as shown in the task list.</summary>
    public string? Project { get; init; }
    public int? ModuleId { get; init; }
    /// <summary>The governed project, when the task has one.</summary>
    public int? ProjectId { get; init; }
    public string? Property { get; init; }
    public int? PropertyId { get; init; }
    public int? SprintId { get; init; }
    public ApiSprint? Sprint { get; init; }
    /// <summary>The task's due date (the task form's "Due Date").</summary>
    public DateOnly? DueDate { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    /// <summary>Legacy column, no longer set by the task form; use <see cref="DueDate"/>.</summary>
    public DateOnly? TargetDate { get; init; }
    public DateTimeOffset? ActualStartDate { get; init; }
    public DateTimeOffset? ActualEndDate { get; init; }
    /// <summary>Estimate, always in hours.</summary>
    public decimal? Weight { get; init; }
    public string EstimateUnit { get; init; } = "Hours";
    public decimal? ActualHours { get; init; }
    public decimal? RemainingWorkHours { get; init; }
    public int? ParentId { get; init; }
    public int? TeamId { get; init; }
    public string? Reporter { get; init; }
    public string? Labels { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public bool IsDeleted { get; init; }
    public string WebUrl { get; init; } = "";
}

/// <summary>A single task with its longer fields.</summary>
public sealed record ApiTaskDetail : ApiTask
{
    public string? Notes { get; init; }
    public string? RelatedFiles { get; init; }
    public string? AcceptanceCriteria { get; init; }
    public int? WorkflowDefinitionId { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    /// <summary>Pass back on PATCH /tasks/{id}/status for optimistic concurrency.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>
/// A page of tasks. <see cref="ServerTime"/> is taken before the query: pass it (minus a minute or two of
/// overlap) as the next <c>updatedSince</c>.
/// </summary>
public sealed record ApiTaskPage(IReadOnlyList<ApiTask> Data, int Page, int PageSize, int TotalCount, DateTimeOffset ServerTime);

public sealed record ApiHistoryEntry(int Id, string Action, string Details, string ChangedBy, DateTimeOffset ChangedAt)
{
    /// <summary>Same instant as <see cref="ChangedAt"/>; kept for older clients.</summary>
    public string ChangedAtIso => ChangedAt.ToString("o");
}

public sealed record ApiComment(int Id, string ChangedBy, string Details, DateTimeOffset ChangedAt)
{
    public string ChangedAtIso => ChangedAt.ToString("o");
}

public sealed record ApiTimeEntry(
    long Id, int TaskId, int? UserId, string? UserName, decimal Hours, DateOnly WorkDate,
    string EntryType, string? Note, long? ReversesEntryId, DateTimeOffset CreatedAt);
