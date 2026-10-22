using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Web.Auth;

namespace TaskTracker.Controllers;

/// <summary>
/// v1 integration endpoints. Accepts either an interactive session cookie or an API key
/// (<c>X-Api-Key</c> header / <c>Authorization: Bearer</c>). API-key callers are limited to the
/// scopes granted to their key (and, for own-tasks-only keys, to their own tasks); cookie callers
/// retain their normal role/team authorization. See docs/api-v1.md.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + ApiKeyDefaults.Scheme)]
[Route("api/v1")]
[EnableRateLimiting("api")]  // per-key (or per-IP) throttling for the integration surface
[IgnoreAntiforgeryToken]   // replaces the app-wide auto-validation with the conditional filter below
[ApiAntiforgery]           // enforces antiforgery for cookie callers only; API-key callers are exempt
[ApiIdempotency]           // replays cached responses for API-key writes carrying Idempotency-Key
public class ApiV1Controller(IProjectService projects, ITaskService tasks, IApiTaskQueryService queries) : AppControllerBase
{
    private const int MaxTimeEntryRangeDays = 92;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        base.OnActionExecuting(context);
        Response.Headers["X-RateLimit-Limit"] = ApiRateLimits.PermitLimit.ToString(CultureInfo.InvariantCulture);
        Response.Headers["X-RateLimit-Policy"] = $"{ApiRateLimits.PermitLimit};w={ApiRateLimits.WindowSeconds}";
    }

    [HttpGet("me")]
    [EndpointSummary("Who the caller is: user id, team, scopes and the server's time zone.")]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        var me = await queries.MeAsync(Caller(), cancellationToken);
        if (me == null) return NotFound();

        var isApiKey = User.Identity?.AuthenticationType == ApiKeyDefaults.Scheme;
        return Ok(new MeResponse
        {
            UserId = me.UserId, Username = me.Username, DisplayName = me.DisplayName, Email = me.Email, Role = me.Role,
            TeamId = me.TeamId, TeamName = me.TeamName, TeamRole = CurrentTeamRole(),
            AuthenticatedWith = isApiKey ? "apiKey" : "session",
            Scopes = isApiKey ? User.FindAll(ApiKeyDefaults.ScopeClaim).Select(c => c.Value).ToList() : ApiScopes.All.ToList(),
            OwnTasksOnly = OwnTasksOnly(),
            ServerTimeZone = me.ServerTimeZone, ServerUtcOffset = me.ServerUtcOffset,
        });
    }

    [HttpGet("projects")]
    [RequireApiScope(ApiScopes.ProjectsRead)]
    [EndpointSummary("Governed projects of the active team, with releases.")]
    public async Task<IActionResult> GetProjects(CancellationToken cancellationToken)
    {
        var teamId = ActiveTeamId();
        var items = await projects.GetForTeamAsync(teamId);
        var releases = await projects.GetReleasesAsync(items.Select(p => p.Id));

        return Ok(new
        {
            data = items.Select(project => new
            {
                project.Id, project.Name, project.Color, project.Status, project.Description,
                StartDate = ApiTime.Date(project.StartDate), TargetDate = ApiTime.Date(project.TargetDate),
                project.TeamId, project.LeadUserId,
                releases = releases.Where(release => release.ProjectId == project.Id)
                    .Select(release => new { release.Id, release.Name, release.Status, TargetDate = ApiTime.Date(release.TargetDate), ReleasedAt = ApiTime.Utc(release.ReleasedAt) })
            })
        });
    }

    [HttpGet("modules")]
    [RequireApiScope(ApiScopes.TasksRead)]
    [EndpointSummary("Modules used by the team's tasks (moduleId and the name most tasks use).")]
    [ProducesResponseType(typeof(LookupResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetModules(CancellationToken cancellationToken) =>
        Ok(new LookupResponse(await queries.ModulesAsync(Caller(), cancellationToken)));

    [HttpGet("properties")]
    [RequireApiScope(ApiScopes.TasksRead)]
    [EndpointSummary("Properties (clients) used by the team's tasks (propertyId and the name most tasks use).")]
    [ProducesResponseType(typeof(LookupResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProperties(CancellationToken cancellationToken) =>
        Ok(new LookupResponse(await queries.PropertiesAsync(Caller(), cancellationToken)));

    [HttpGet("sprints")]
    [RequireApiScope(ApiScopes.TasksRead)]
    [EndpointSummary("The team's sprints with their dates; active=true for the current ones.")]
    [ProducesResponseType(typeof(SprintsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSprints([FromQuery] bool active, CancellationToken cancellationToken) =>
        Ok(new SprintsResponse(await queries.SprintsAsync(Caller(), active, cancellationToken)));

    [HttpGet("tasks")]
    [RequireApiScope(ApiScopes.TasksRead)]
    [EndpointSummary("Tasks of the active team, filtered and paged. Supports updatedSince for incremental sync and ETag/If-None-Match.")]
    [ProducesResponseType(typeof(ApiTaskPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetTasks([FromQuery] TaskListRequest request, CancellationToken cancellationToken)
    {
        var (query, error) = request.ToQuery(CurrentUserId());
        if (error != null) return BadRequest(new { error });

        var caller = Caller();
        var fingerprint = await queries.FingerprintAsync(query, caller, cancellationToken);
        var etag = $"W/\"{Hash($"{caller}|{Request.QueryString.Value}|{fingerprint}")}\"";
        Response.Headers.ETag = etag;
        if (Request.Headers.IfNoneMatch.Any(value => value != null && value.Split(',').Select(v => v.Trim()).Contains(etag)))
            return StatusCode(StatusCodes.Status304NotModified);

        return Ok(await queries.ListAsync(query, caller, cancellationToken));
    }

    [HttpGet("tasks/export")]
    [RequireApiScope(ApiScopes.TasksRead)]
    [EndpointSummary("Filtered task CSV export (not available to own-tasks-only keys).")]
    public async Task<IActionResult> ExportTasks([FromQuery] ExportFilters filters)
    {
        if (OwnTasksOnly()) return OwnTasksOnlyForbidden("export");
        var (bytes, fileName) = await tasks.ExportCsvAsync(filters, ActiveTeamId());
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    [HttpGet("tasks/{id:int}")]
    [RequireApiScope(ApiScopes.TasksRead)]
    [EndpointSummary("One task, including notes, acceptance criteria and its rowVersion.")]
    [ProducesResponseType(typeof(ApiTaskDetail), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTask(int id, CancellationToken cancellationToken)
    {
        var task = await queries.GetAsync(id, Caller(), cancellationToken);
        return task == null ? NotFound() : Ok(task);
    }

    [HttpPost("tasks")]
    [RequireApiScope(ApiScopes.TasksWrite)]
    [EndpointSummary("Create a task through the governed task command. Send an Idempotency-Key header to make retries safe.")]
    [ProducesResponseType(typeof(CreateTaskResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
            return BadRequest(new { error = "Goal is required." });

        var owner = request.Owner?.Trim();
        if (OwnTasksOnly())
        {
            // An own-tasks-only key could not see a task it assigns to someone else.
            if (!string.IsNullOrEmpty(owner) && !string.Equals(owner, CurrentDisplayName(), StringComparison.OrdinalIgnoreCase))
                return OwnTasksOnlyForbidden("assign tasks to other people");
            owner = CurrentDisplayName();
        }

        // Integrations may omit the type and status: a Task in its type's first status.
        var itemType = string.IsNullOrWhiteSpace(request.ItemType) ? "Task" : request.ItemType.Trim();
        var status = string.IsNullOrWhiteSpace(request.Status) ? TaskStatusRules.InitialStatus(itemType) : request.Status.Trim();
        var task = new TaskItem
        {
            Goal = request.Goal.Trim(), Project = request.Project?.Trim(), Property = request.Property?.Trim(),
            Priority = request.Priority?.Trim(), Status = status, Owner = owner,
            TargetDate = request.TargetDate?.Date, StartDate = request.StartDate?.Date, EndDate = (request.DueDate ?? request.EndDate)?.Date,
            Weight = request.Weight, EstimateUnit = "Hours",
            ItemType = itemType, ParentId = request.ParentId, SprintId = request.SprintId,
            Labels = request.Labels?.Trim(), AcceptanceCriteria = request.AcceptanceCriteria?.Trim(),
            Notes = request.Notes?.Trim(), TeamId = ActiveTeamId(), ProjectId = request.ProjectId,
            ModuleId = request.ModuleId, ClientId = request.PropertyId
        };
        var result = await tasks.SaveAsync(task, BuildUserCtx(), forceClose: false);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return CreatedAtAction(nameof(GetTask), new { id = result.TaskId }, new CreateTaskResponse(result.TaskId, result.CapacityWarning));
    }

    [HttpPatch("tasks/{id:int}/status")]
    [RequireApiScope(ApiScopes.TasksWrite)]
    [EndpointSummary("Apply a governed status transition. Pass rowVersion from GET /tasks/{id} to get 409 on a stale update.")]
    public async Task<IActionResult> UpdateTaskStatus(int id, [FromBody] UpdateTaskStatusRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Status)) return BadRequest(new { error = "Status is required." });
        if (!await queries.IsVisibleAsync(id, Caller(), cancellationToken)) return NotFound();

        byte[]? expectedRowVersion = null;
        if (!string.IsNullOrWhiteSpace(request.RowVersion))
        {
            try { expectedRowVersion = Convert.FromBase64String(request.RowVersion); }
            catch (FormatException) { return BadRequest(new { error = "rowVersion must be base64." }); }
        }

        var result = await tasks.UpdateStatusAsync(id, request.Status.Trim(), request.Swimlane?.Trim(),
            BuildUserCtx(), request.ForceClose, request.ActualHours, expectedRowVersion);
        if (!result.Success)
        {
            if (result.Conflict) return Conflict(new { error = result.Error });
            return BadRequest(new { error = result.Error, result.NeedOwner, result.NeedActualHours, result.OpenChildren });
        }
        return NoContent();
    }

    [HttpPost("tasks/{id:int}/time-entries")]
    [RequireApiScope(ApiScopes.TasksWrite)]
    [EndpointSummary("Log append-only work time on a task. Send an Idempotency-Key header to make retries safe.")]
    public async Task<IActionResult> LogTime(int id, [FromBody] LogTimeRequest request, CancellationToken cancellationToken)
    {
        if (request.Hours <= 0) return BadRequest(new { error = "Hours must be greater than zero." });
        if (!await queries.IsVisibleAsync(id, Caller(), cancellationToken)) return NotFound();
        var result = await tasks.LogTimeAsync(id, request.Hours, request.WorkDate.Date, request.Note?.Trim(), request.RemainingWorkHours, BuildUserCtx());
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(new { result.ActualHours, result.RemainingWorkHours });
    }

    [HttpGet("tasks/{id:int}/time-entries")]
    [RequireApiScope(ApiScopes.TasksRead)]
    [EndpointSummary("Time logged on a task (append-only; corrections are negative entries).")]
    [ProducesResponseType(typeof(TimeEntriesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTimeEntries(int id, CancellationToken cancellationToken)
    {
        if (!await queries.IsVisibleAsync(id, Caller(), cancellationToken)) return NotFound();
        return Ok(new TimeEntriesResponse(await queries.TimeEntriesForTaskAsync(id, cancellationToken)));
    }

    [HttpGet("time-entries")]
    [RequireApiScope(ApiScopes.TasksRead)]
    [EndpointSummary("The caller's own time entries with a work date from..to (inclusive, at most 92 days).")]
    [ProducesResponseType(typeof(TimeEntriesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyTimeEntries([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var end = to ?? DateOnly.FromDateTime(DateTime.Now);
        var start = from ?? end;
        if (end < start) return BadRequest(new { error = "'to' must not be before 'from'." });
        if (end.DayNumber - start.DayNumber >= MaxTimeEntryRangeDays)
            return BadRequest(new { error = $"Ask for at most {MaxTimeEntryRangeDays} days at a time." });
        return Ok(new TimeEntriesResponse(await queries.TimeEntriesForUserAsync(CurrentUserId(), start, end, cancellationToken)));
    }

    [HttpPost("tasks/{id:int}/comments")]
    [RequireApiScope(ApiScopes.TasksWrite)]
    [EndpointSummary("Add a comment to a task.")]
    public async Task<IActionResult> AddComment(int id, [FromBody] AddCommentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest(new { error = "Comment text is required." });
        if (!await queries.IsVisibleAsync(id, Caller(), cancellationToken)) return NotFound();
        var result = await tasks.AddCommentAsync(id, request.Text.Trim(), BuildUserCtx());
        if (!result.Success) return BadRequest(new { error = result.Error });
        return NoContent();
    }

    [HttpGet("tasks/{id:int}/comments")]
    [RequireApiScope(ApiScopes.TasksRead)]
    [EndpointSummary("A task's comments, oldest first.")]
    public async Task<IActionResult> GetComments(int id, CancellationToken cancellationToken)
    {
        if (!await queries.IsVisibleAsync(id, Caller(), cancellationToken)) return NotFound();
        return Ok(new { data = await queries.CommentsAsync(id, cancellationToken) });
    }

    [HttpGet("tasks/{id:int}/history")]
    [RequireApiScope(ApiScopes.TasksRead)]
    [EndpointSummary("A task's audit history (status and field changes), newest first.")]
    public async Task<IActionResult> GetHistory(int id, CancellationToken cancellationToken)
    {
        if (!await queries.IsVisibleAsync(id, Caller(), cancellationToken)) return NotFound();
        return Ok(new { data = await queries.HistoryAsync(id, cancellationToken) });
    }

    [HttpGet("tasks/{id:int}/links")]
    [RequireApiScope(ApiScopes.TasksRead)]
    public async Task<IActionResult> GetLinks(int id, CancellationToken cancellationToken)
    {
        if (!await queries.IsVisibleAsync(id, Caller(), cancellationToken)) return NotFound();
        return Ok(await tasks.GetLinksAsync(id, ActiveTeamId()));
    }

    [HttpGet("tasks/{id:int}/attachments")]
    [RequireApiScope(ApiScopes.TasksRead)]
    public async Task<IActionResult> GetAttachments(int id, CancellationToken cancellationToken)
    {
        if (!await queries.IsVisibleAsync(id, Caller(), cancellationToken)) return NotFound();
        return Ok(new { data = await tasks.GetAttachmentsAsync(id) });
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private bool OwnTasksOnly() => User.HasClaim(ApiKeyDefaults.OwnTasksOnlyClaim, "true");

    private ApiCaller Caller() => new(CurrentUserId(), ActiveTeamId(), OwnTasksOnly());

    private ObjectResult OwnTasksOnlyForbidden(string what) =>
        new(new { error = $"This API key is limited to its owner's tasks, so it cannot {what}." }) { StatusCode = StatusCodes.Status403Forbidden };

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..20].ToLowerInvariant();

    // ── request / response types ────────────────────────────────────────────

    /// <summary>Query parameters of GET /api/v1/tasks. Lists are comma-separated and match any value.</summary>
    public sealed class TaskListRequest
    {
        [Description("Owner user ids (from GET /me or a task's ownerIds), or \"me\". Exact match on the task's owners.")]
        public string? OwnerId { get; init; }
        [Description("Deprecated: loose match on owner display names. Use ownerId.")]
        public string? Owner { get; init; }
        [Description("Statuses, e.g. \"New,To Do,In Progress\".")]
        public string? Status { get; init; }
        [Description("Item types: Task, Bug, Story, Feature, Epic.")]
        public string? ItemType { get; init; }
        [Description("Priorities, e.g. \"Critical,High\".")]
        public string? Priority { get; init; }
        [Description("true = not done or cancelled; false = done or cancelled.")]
        public bool? Open { get; init; }
        [Description("A sprint id, \"active\" or \"backlog\".")]
        public string? Sprint { get; init; }
        public int? SprintId { get; init; }
        public int? ProjectId { get; init; }
        public int? ModuleId { get; init; }
        public int? PropertyId { get; init; }
        [Description("Exact module name (the task list's Project column).")]
        public string? Project { get; init; }
        [Description("Exact property name.")]
        public string? Property { get; init; }
        [Description("Text in the goal, labels, module or property.")]
        public string? Search { get; init; }
        [Description("Due date on or after (yyyy-MM-dd).")]
        public DateTime? DueFrom { get; init; }
        [Description("Due date on or before (yyyy-MM-dd).")]
        public DateTime? DueTo { get; init; }
        [Description("Deprecated alias of dueFrom.")]
        public DateTime? DateFrom { get; init; }
        [Description("Deprecated alias of dueTo.")]
        public DateTime? DateTo { get; init; }
        [Description("Only tasks changed at or after this instant, e.g. 2026-10-21T03:50:00Z. Use the previous response's serverTime minus a minute or two.")]
        public string? UpdatedSince { get; init; }
        [Description("Task ids (at most 200).")]
        public string? Ids { get; init; }
        [Description("Also return soft-deleted tasks (isDeleted = true), so a sync can remove them.")]
        public bool IncludeDeleted { get; init; }
        [Description("created (default) | updated | due | id. With updatedSince the default is updated, ascending.")]
        public string? Sort { get; init; }
        [Description("asc or desc (default desc).")]
        public string? SortDir { get; init; }
        public int Page { get; init; } = 1;
        [Description("1–100, default 20.")]
        public int PageSize { get; init; } = 20;

        public (ApiTaskQuery Query, string? Error) ToQuery(int currentUserId)
        {
            var ownerIds = new List<int>();
            foreach (var part in Split(OwnerId))
            {
                if (string.Equals(part, "me", StringComparison.OrdinalIgnoreCase)) ownerIds.Add(currentUserId);
                else if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) ownerIds.Add(id);
                else return (new ApiTaskQuery(), $"ownerId must be user ids or \"me\", not \"{part}\".");
            }

            var ids = new List<int>();
            foreach (var part in Split(Ids))
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                    return (new ApiTaskQuery(), $"ids must be task ids, not \"{part}\".");
                ids.Add(id);
            }
            if (ids.Count > ApiTaskQuery.MaxIds) return (new ApiTaskQuery(), $"Ask for at most {ApiTaskQuery.MaxIds} ids at a time.");

            DateTime? updatedSince = null;
            if (!string.IsNullOrWhiteSpace(UpdatedSince))
            {
                if (!DateTimeOffset.TryParse(UpdatedSince, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var since))
                    return (new ApiTaskQuery(), "updatedSince must be an ISO 8601 date-time, e.g. 2026-10-21T03:50:00Z.");
                updatedSince = since.UtcDateTime;
            }

            var sort = Sort?.Trim().ToLowerInvariant();
            if (sort is not (null or "" or "created" or "updated" or "due" or "id"))
                return (new ApiTaskQuery(), "sort must be created, updated, due or id.");

            return (new ApiTaskQuery
            {
                OwnerIds = ownerIds, OwnerName = Owner?.Trim(),
                Statuses = Split(Status).ToList(), ItemTypes = Split(ItemType).ToList(), Priorities = Split(Priority).ToList(),
                Open = Open,
                Sprint = SprintId?.ToString(CultureInfo.InvariantCulture) ?? (Sprint == "__none__" ? "backlog" : Sprint),
                ProjectId = ProjectId, ModuleId = ModuleId, PropertyId = PropertyId,
                Project = Project?.Trim(), Property = Property?.Trim(), Search = Search?.Trim(),
                DueFrom = DueFrom ?? DateFrom, DueTo = DueTo ?? DateTo,
                UpdatedSinceUtc = updatedSince, Ids = ids, IncludeDeleted = IncludeDeleted,
                Sort = string.IsNullOrEmpty(sort) ? null : sort,
                Descending = !string.Equals(SortDir, "asc", StringComparison.OrdinalIgnoreCase),
                Page = Math.Max(Page, 1), PageSize = Math.Clamp(PageSize, 1, ApiTaskQuery.MaxPageSize),
            }, null);
        }

        private static IEnumerable<string> Split(string? value) =>
            (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public sealed class MeResponse
    {
        public int UserId { get; init; }
        public string Username { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string? Email { get; init; }
        public string Role { get; init; } = "";
        public int? TeamId { get; init; }
        public string? TeamName { get; init; }
        public string TeamRole { get; init; } = "";
        [Description("apiKey or session.")]
        public string AuthenticatedWith { get; init; } = "";
        public IReadOnlyList<string> Scopes { get; init; } = [];
        [Description("True when the key only sees and changes tasks the caller is assigned to.")]
        public bool OwnTasksOnly { get; init; }
        [Description("Windows time zone id of the server.")]
        public string ServerTimeZone { get; init; } = "";
        [Description("The server's current UTC offset, e.g. +05:30.")]
        public string ServerUtcOffset { get; init; } = "";
    }

    public sealed record LookupResponse(IReadOnlyList<ApiLookup> Data);
    public sealed record SprintsResponse(IReadOnlyList<ApiSprint> Data);
    public sealed record TimeEntriesResponse(IReadOnlyList<ApiTimeEntry> Data);
    public sealed record CreateTaskResponse(int Id, string? CapacityWarning);

    public sealed class CreateTaskRequest
    {
        [Description("Required. The task title.")]
        public string? Goal { get; init; }
        [Description("Module name as shown in the task list.")]
        public string? Project { get; init; }
        [Description("Module id (GET /modules).")]
        public int? ModuleId { get; init; }
        [Description("Governed project id (GET /projects).")]
        public int? ProjectId { get; init; }
        [Description("Property (client) name.")]
        public string? Property { get; init; }
        [Description("Property id (GET /properties).")]
        public int? PropertyId { get; init; }
        [Description("Critical, High, Medium or Low.")]
        public string? Priority { get; init; }
        [Description("Initial status; defaults to the item type's first status.")]
        public string? Status { get; init; }
        [Description("Owner display names, comma-separated. Own-tasks-only keys always assign to the caller.")]
        public string? Owner { get; init; }
        [Description("Due date (yyyy-MM-dd). Same as endDate.")]
        public DateTime? DueDate { get; init; }
        [Description("Legacy; prefer dueDate.")]
        public DateTime? TargetDate { get; init; }
        public DateTime? StartDate { get; init; }
        [Description("Due date (yyyy-MM-dd); dueDate wins when both are sent.")]
        public DateTime? EndDate { get; init; }
        [Description("Estimate in hours.")]
        public decimal? Weight { get; init; }
        [Description("Ignored: estimates are always in hours.")]
        public string? EstimateUnit { get; init; }
        [Description("Task (default), Bug, Story, Feature or Epic.")]
        public string? ItemType { get; init; }
        public int? ParentId { get; init; }
        public int? SprintId { get; init; }
        [Description("Comma-separated labels.")]
        public string? Labels { get; init; }
        public string? AcceptanceCriteria { get; init; }
        public string? Notes { get; init; }
    }

    public sealed class UpdateTaskStatusRequest
    {
        public string? Status { get; init; }
        public string? Swimlane { get; init; }
        public bool ForceClose { get; init; }
        public decimal? ActualHours { get; init; }
        /// <summary>Base64 row version from GET /tasks/{id}. If supplied, a stale value returns 409.</summary>
        [Description("Base64 rowVersion from GET /tasks/{id}; a stale value returns 409.")]
        public string? RowVersion { get; init; }
    }

    public sealed class LogTimeRequest
    {
        [Description("Required, greater than zero.")]
        public decimal Hours { get; init; }
        [Description("The day the work was done (yyyy-MM-dd); defaults to today.")]
        public DateTime WorkDate { get; init; } = DateTime.UtcNow;
        public string? Note { get; init; }
        [Description("Optional: hours of work left on the task.")]
        public decimal? RemainingWorkHours { get; init; }
    }

    public sealed class AddCommentRequest { public string? Text { get; init; } }
}
