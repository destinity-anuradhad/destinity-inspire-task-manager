using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Models;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class ProjectsController(IProjectService projects, IActivityService activity) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var teamId = ActiveTeamId();
        ViewBag.Members = teamId.HasValue ? await projects.GetTeamMembersAsync(teamId.Value) : [];
        var projectList = await projects.GetForTeamAsync(teamId);
        ViewBag.Releases = await projects.GetReleasesAsync(projectList.Select(p => p.Id));
        ViewData["ActiveNav"] = "Projects";
        return View(projectList);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ProjectItem project)
    {
        var result = await projects.SaveAsync(project, BuildUserCtx());
        if (!result.Success) TempData["Error"] = result.Error;
        else
        {
            await activity.LogAsync(CurrentDisplayName(), project.Id == 0 ? "Project Created" : "Project Updated", "Project",
                result.Project!.Name, teamId: ActiveTeamId());
            TempData["Success"] = project.Id == 0 ? "Project created." : "Project updated.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id)
    {
        var (ok, error) = await projects.ArchiveAsync(id, BuildUserCtx());
        if (!ok) TempData["Error"] = error;
        else
        {
            await activity.LogAsync(CurrentDisplayName(), "Project Archived", "Project", $"Project {id}", teamId: ActiveTeamId());
            TempData["Success"] = "Project archived.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRelease(ProjectRelease release)
    {
        var (ok, error) = await projects.CreateReleaseAsync(release, BuildUserCtx());
        if (!ok) TempData["Error"] = error;
        else TempData["Success"] = "Release created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReleased(int id)
    {
        var (ok, error) = await projects.MarkReleasedAsync(id, BuildUserCtx());
        if (!ok) TempData["Error"] = error;
        else TempData["Success"] = "Release marked as released.";
        return RedirectToAction(nameof(Index));
    }
}
