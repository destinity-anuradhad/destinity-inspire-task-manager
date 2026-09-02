using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Web.Auth;

namespace TaskTracker.Controllers;

/// <summary>
/// v1 integration endpoints. Accepts either an interactive session cookie or an API key
/// (<c>X-Api-Key</c> header / <c>Authorization: Bearer</c>). API-key callers are limited to the
/// scopes granted to their key; cookie callers retain their normal role/team authorization.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + ApiKeyDefaults.Scheme)]
[Route("api/v1")]
[EnableRateLimiting("api")]  // per-key (or per-IP) throttling for the integration surface
[IgnoreAntiforgeryToken]   // replaces the app-wide auto-validation with the conditional filter below
[ApiAntiforgery]           // enforces antiforgery for cookie callers only; API-key callers are exempt
public class ApiV1Controller(IProjectService projects, ITaskService tasks) : AppControllerBase
{
    [HttpGet("projects")]
    [RequireApiScope(ApiScopes.ProjectsRead)]
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
                project.StartDate, project.TargetDate, project.TeamId, project.LeadUserId,
                releases = releases.Where(release => release.ProjectId == project.Id)
                    .Select(release => new { release.Id, release.Name, release.Status, release.TargetDate, release.ReleasedAt })
            })
        });
    }

    [HttpGet("tasks")]
    [RequireApiScope(ApiScopes.TasksRead)]
    public async Task<IActionResult> GetTasks([FromQuery] TaskListFilters filters)
    {
        filters.Page = Math.Max(filters.Page, 1);
        filters.PageSize = Math.Clamp(filters.PageSize, 1, 100);
        var result = await tasks.GetListAsync(filters, ActiveTeamId());
        var model = result.ViewModel;

        return Ok(new
        {
            page = model.Page,
            pageSize = model.PageSize,
            totalCount = model.TotalCount,
            data = model.Tasks.Select(task => new
            {
                task.Id, task.Goal, task.Project, task.ProjectId, task.Property, task.Status,
                task.Priority, task.Owner, task.TargetDate, task.StartDate, task.EndDate,
                task.Weight, task.EstimateUnit, task.ActualHours, task.RemainingWorkHours,
                task.ItemType, task.ParentId, task.SprintId, task.TeamId, task.CreatedAt
            })
        });
    }

    [HttpGet("tasks/export")]
    [RequireApiScope(ApiScopes.TasksRead)]
    public async Task<IActionResult> ExportTasks([FromQuery] ExportFilters filters)
    {
        var (bytes, fileName) = await tasks.ExportCsvAsync(filters, ActiveTeamId());
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    [HttpGet("tasks/{id:int}")]
    [RequireApiScope(ApiScopes.TasksRead)]
    public async Task<IActionResult> GetTask(int id)
    {
        var task = await tasks.GetByIdAsync(id, ActiveTeamId());
        if (task == null) return NotFound();

        return Ok(new
        {
            task.Id, task.Goal, task.Project, task.ProjectId, task.Property, task.Status,
            task.Priority, task.Owner, task.TargetDate, task.StartDate, task.EndDate,
            task.ActualStartDate, task.ActualEndDate, task.Notes, task.RelatedFiles,
            task.Weight, task.EstimateUnit, task.ActualHours, task.RemainingWorkHours,
            task.ItemType, task.ParentId, task.SprintId, task.WorkflowDefinitionId,
            task.Labels, task.AcceptanceCriteria, task.Reporter, task.TeamId, task.CreatedAt,
            rowVersion = task.RowVersion is { Length: > 0 } ? Convert.ToBase64String(task.RowVersion) : null
        });
    }

    [HttpPost("tasks")]
    [RequireApiScope(ApiScopes.TasksWrite)]
    public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
            return BadRequest(new { error = "Goal is required." });

        var task = new TaskItem
        {
            Goal = request.Goal.Trim(), Project = request.Project?.Trim(), Property = request.Property?.Trim(),
            Priority = request.Priority?.Trim(), Status = request.Status?.Trim(), Owner = request.Owner?.Trim(),
            TargetDate = request.TargetDate?.Date, StartDate = request.StartDate?.Date, EndDate = request.EndDate?.Date,
            Weight = request.Weight, EstimateUnit = request.EstimateUnit?.Trim() ?? "Points",
            ItemType = request.ItemType?.Trim(), ParentId = request.ParentId, SprintId = request.SprintId,
            Labels = request.Labels?.Trim(), AcceptanceCriteria = request.AcceptanceCriteria?.Trim(),
            Notes = request.Notes?.Trim(), TeamId = ActiveTeamId(), ProjectId = request.ProjectId
        };
        var result = await tasks.SaveAsync(task, BuildUserCtx(), forceClose: false);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return CreatedAtAction(nameof(GetTask), new { id = result.TaskId }, new { id = result.TaskId, capacityWarning = result.CapacityWarning });
    }

    [HttpPatch("tasks/{id:int}/status")]
    [RequireApiScope(ApiScopes.TasksWrite)]
    public async Task<IActionResult> UpdateTaskStatus(int id, [FromBody] UpdateTaskStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Status)) return BadRequest(new { error = "Status is required." });

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
    public async Task<IActionResult> LogTime(int id, [FromBody] LogTimeRequest request)
    {
        if (request.Hours <= 0) return BadRequest(new { error = "Hours must be greater than zero." });
        var result = await tasks.LogTimeAsync(id, request.Hours, request.WorkDate.Date, request.Note?.Trim(), request.RemainingWorkHours, BuildUserCtx());
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(new { result.ActualHours, result.RemainingWorkHours });
    }

    [HttpPost("tasks/{id:int}/comments")]
    [RequireApiScope(ApiScopes.TasksWrite)]
    public async Task<IActionResult> AddComment(int id, [FromBody] AddCommentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest(new { error = "Comment text is required." });
        var result = await tasks.AddCommentAsync(id, request.Text.Trim(), BuildUserCtx());
        if (!result.Success) return BadRequest(new { error = result.Error });
        return NoContent();
    }

    [HttpGet("tasks/{id:int}/comments")]
    [RequireApiScope(ApiScopes.TasksRead)]
    public async Task<IActionResult> GetComments(int id)
    {
        var task = await tasks.GetByIdAsync(id, ActiveTeamId());
        if (task == null) return NotFound();
        return Ok(new { data = await tasks.GetCommentsAsync(id, ActiveTeamId()) });
    }

    [HttpGet("tasks/{id:int}/history")]
    [RequireApiScope(ApiScopes.TasksRead)]
    public async Task<IActionResult> GetHistory(int id)
    {
        var task = await tasks.GetByIdAsync(id, ActiveTeamId());
        if (task == null) return NotFound();
        return Ok(new { data = await tasks.GetHistoryAsync(id, ActiveTeamId()) });
    }

    [HttpGet("tasks/{id:int}/links")]
    [RequireApiScope(ApiScopes.TasksRead)]
    public async Task<IActionResult> GetLinks(int id)
    {
        var task = await tasks.GetByIdAsync(id, ActiveTeamId());
        if (task == null) return NotFound();
        return Ok(await tasks.GetLinksAsync(id, ActiveTeamId()));
    }

    [HttpGet("tasks/{id:int}/attachments")]
    [RequireApiScope(ApiScopes.TasksRead)]
    public async Task<IActionResult> GetAttachments(int id)
    {
        var task = await tasks.GetByIdAsync(id, ActiveTeamId());
        if (task == null) return NotFound();
        return Ok(new { data = await tasks.GetAttachmentsAsync(id) });
    }

    public sealed class CreateTaskRequest
    {
        public string? Goal { get; init; }
        public string? Project { get; init; }
        public int? ProjectId { get; init; }
        public string? Property { get; init; }
        public string? Priority { get; init; }
        public string? Status { get; init; }
        public string? Owner { get; init; }
        public DateTime? TargetDate { get; init; }
        public DateTime? StartDate { get; init; }
        public DateTime? EndDate { get; init; }
        public decimal? Weight { get; init; }
        public string? EstimateUnit { get; init; }
        public string? ItemType { get; init; }
        public int? ParentId { get; init; }
        public int? SprintId { get; init; }
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
        public string? RowVersion { get; init; }
    }

    public sealed class LogTimeRequest
    {
        public decimal Hours { get; init; }
        public DateTime WorkDate { get; init; } = DateTime.UtcNow;
        public string? Note { get; init; }
        public decimal? RemainingWorkHours { get; init; }
    }

    public sealed class AddCommentRequest { public string? Text { get; init; } }
}
