using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

/// <summary>Read-only integration endpoints. Cookie authentication is required.</summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public class ApiV1Controller(IProjectService projects, ITaskService tasks) : AppControllerBase
{
    [HttpGet("projects")]
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
}
