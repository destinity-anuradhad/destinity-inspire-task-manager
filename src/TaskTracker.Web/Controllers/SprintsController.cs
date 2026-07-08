using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Models;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class SprintsController(ISprintService sprints) : AppControllerBase
{
    private bool IsTeamLead()       => CurrentTeamRole() is "TeamAdmin" or "Lead";
    private bool CanManageSprints() => IsAdmin() || IsTeamLead();
    private bool CanDeleteSprints() => IsAdmin() || CurrentTeamRole() == "TeamAdmin";

    public async Task<IActionResult> Index()
    {
        if (!CanManageSprints()) return Forbid();
        var data = await sprints.GetIndexDataAsync(ActiveTeamId());
        ViewBag.TaskCounts    = data.TaskCounts;
        ViewBag.OwnerNames    = data.OwnerNames;
        ViewData["ActiveNav"] = "Sprints";
        return View(data.Sprints);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Sprint sprint)
    {
        if (!CanManageSprints()) return Forbid();
        var createdBy = User.FindFirst("DisplayName")?.Value ?? User.Identity?.Name ?? "";
        var (ok, error) = await sprints.SaveAsync(sprint, createdBy, ActiveTeamId());
        return ok ? Ok(new { message = "Saved" }) : BadRequest(new { message = error });
    }

    [HttpPost]
    public async Task<IActionResult> SetActive(int id)
    {
        if (!CanManageSprints()) return Forbid();
        await sprints.SetActiveAsync(id, ActiveTeamId());
        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> Deactivate()
    {
        if (!CanManageSprints()) return Forbid();
        await sprints.DeactivateAsync(ActiveTeamId());
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> GetIncomplete(int sprintId)
    {
        var result = await sprints.GetIncompleteAsync(sprintId, ActiveTeamId());
        if (result == null) return NotFound();
        return Json(new { total = result.Total, incomplete = result.Incomplete, done = result.Done });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id, string moveTo = "backlog")
    {
        if (!CanManageSprints()) return Forbid();
        var (found, ok, moved, movedTo, error) = await sprints.CompleteAsync(id, moveTo, ActiveTeamId());
        if (!found) return NotFound();
        if (!ok)    return BadRequest(new { message = error });
        return Json(new { success = true, moved, movedTo });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        if (!CanDeleteSprints()) return Forbid();
        var (ok, error) = await sprints.DeleteAsync(id, ActiveTeamId());
        if (!ok) return error == "Not found" ? NotFound() : BadRequest(new { message = error });
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> GetCapacity(int sprintId)
    {
        var data = await sprints.GetCapacityAsync(sprintId, ActiveTeamId());
        if (data == null) return NotFound();
        return Json(new {
            sprintId       = data.SprintId,
            sprintName     = data.SprintName,
            startDate      = data.StartDate,
            endDate        = data.EndDate,
            rows           = data.Rows.Select(r => new {
                ownerName       = r.OwnerName,
                capacityHours   = r.CapacityHours,
                allocatedPoints = r.AllocatedPoints,
                taskCount       = r.TaskCount
            }).ToList(),
            totalCapacity  = data.TotalCapacity,
            totalAllocated = data.TotalAllocated
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCapacity([FromBody] SprintCapacityRequest req)
    {
        if (req == null) return BadRequest();
        var ok = await sprints.SaveCapacityAsync(req, ActiveTeamId());
        return ok ? Json(new { success = true }) : NotFound();
    }
}
