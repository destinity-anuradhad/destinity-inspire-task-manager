using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Web.Auth;

namespace TaskTracker.Controllers;

[Authorize]
public class SprintsController(ISprintService sprints, IConfiguration config) : AppControllerBase
{
    private bool LegacySprintMutationsEnabled() =>
        config.GetValue<bool>("Features:AllowLegacySprintMutations");
    private IActionResult SprintMutationsDisabled() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new {
            message = "Sprint changes are temporarily disabled while sprint-history remediation is in progress."
        });

    // Returns the active team ID, or null if not in a team context.
    // All sprint operations require a team — an admin browsing without selecting
    // a team would otherwise get/mutate global data.
    private int? TeamId() => ActiveTeamId();
    private bool NoTeam(out int teamId)
    {
        var id = TeamId();
        teamId = id ?? 0;
        return !id.HasValue;
    }

    // Read-only sprint board is visible to every authenticated team member.
    // All mutation endpoints below stay gated by the TeamLead/TeamAdmin policies,
    // and the view hides their controls for members — so this is view-only for them.
    public async Task<IActionResult> Index()
    {
        if (NoTeam(out _)) return RedirectToAction("AutoSelect", "Teams");
        var data = await sprints.GetIndexDataAsync(TeamId());
        ViewBag.TaskCounts        = data.TaskCounts;
        ViewBag.OwnerNames        = data.OwnerNames;
        ViewBag.TeamMemberNames   = data.TeamMemberNames;
        ViewBag.VelocityHours     = data.VelocityHours;
        ViewBag.SpilloverCounts   = data.SpilloverCounts;
        ViewBag.ActualHours       = data.ActualHours;
        ViewBag.EstimateHours     = data.EstimateHours;
        ViewBag.SprintHours       = data.SprintHours;
        ViewBag.UserHours         = data.UserHours;
        ViewData["ActiveNav"]     = "Sprints";
        return View(data.Sprints);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.TeamLead)]
    public async Task<IActionResult> Save(Sprint sprint)
    {
        if (NoTeam(out _)) return BadRequest(new { message = "No active team." });
        if (!LegacySprintMutationsEnabled()) return SprintMutationsDisabled();
        var createdBy = User.FindFirst("DisplayName")?.Value ?? User.Identity?.Name ?? "";
        var (ok, sprintId, error) = await sprints.SaveAsync(sprint, createdBy, TeamId());
        return ok ? Ok(new { message = "Saved", id = sprintId }) : BadRequest(new { message = error });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.TeamLead)]
    public async Task<IActionResult> SetActive(int id)
    {
        if (NoTeam(out _)) return BadRequest(new { message = "No active team." });
        if (!LegacySprintMutationsEnabled()) return SprintMutationsDisabled();
        var by = User.FindFirst("DisplayName")?.Value ?? User.Identity?.Name ?? "";
        await sprints.SetActiveAsync(id, TeamId(), by);
        return Ok();
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.TeamLead)]
    public async Task<IActionResult> Deactivate()
    {
        if (NoTeam(out _)) return BadRequest(new { message = "No active team." });
        if (!LegacySprintMutationsEnabled()) return SprintMutationsDisabled();
        await sprints.DeactivateAsync(TeamId());
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> GetIncomplete(int sprintId)
    {
        if (NoTeam(out _)) return BadRequest();
        var result = await sprints.GetIncompleteAsync(sprintId, TeamId());
        if (result == null) return NotFound();
        return Json(new {
            total = result.Total, incomplete = result.Incomplete, done = result.Done, excluded = result.Excluded,
            tasks = result.Tasks.Select(t => new {
                id = t.Id, goal = t.Goal, itemType = t.ItemType, priority = t.Priority, owner = t.Owner,
                estimate = t.Estimate, actual = t.Actual, remaining = t.Remaining
            })
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.TeamLead)]
    public async Task<IActionResult> Complete(int id, string moveTo = "backlog", string? items = null)
    {
        if (NoTeam(out _)) return BadRequest(new { message = "No active team." });
        if (!LegacySprintMutationsEnabled()) return SprintMutationsDisabled();
        var completedBy = User.FindFirst("DisplayName")?.Value ?? User.Identity?.Name ?? "";

        // Per-task carry-forward instructions (destination + hours worked + remaining) — optional;
        // absent means "use the single fallback moveTo for every unfinished task".
        List<SprintCarryItem>? carry = null;
        if (!string.IsNullOrWhiteSpace(items))
        {
            try {
                carry = System.Text.Json.JsonSerializer.Deserialize<List<SprintCarryItem>>(
                    items, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            } catch { return BadRequest(new { message = "Invalid carry-forward data." }); }
        }

        var (found, ok, moved, movedTo, error) = await sprints.CompleteAsync(id, moveTo, carry, TeamId(), completedBy);
        if (!found) return NotFound();
        if (!ok)    return BadRequest(new { message = error });
        return Json(new { success = true, moved, movedTo });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.TeamAdmin)]
    public async Task<IActionResult> Delete(int id)
    {
        if (NoTeam(out _)) return BadRequest(new { message = "No active team." });
        if (!LegacySprintMutationsEnabled()) return SprintMutationsDisabled();
        var (ok, error) = await sprints.DeleteAsync(id, TeamId());
        if (!ok) return error == "Not found" ? NotFound() : BadRequest(new { message = error });
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> GetCapacity(int sprintId)
    {
        if (NoTeam(out _)) return BadRequest();
        var data = await sprints.GetCapacityAsync(sprintId, TeamId());
        if (data == null) return NotFound();
        return Json(new {
            sprintId       = data.SprintId,
            sprintName     = data.SprintName,
            startDate      = data.StartDate,
            endDate        = data.EndDate,
            rows           = data.Rows.Select(r => new {
                ownerName          = r.OwnerName,
                capacityHours      = r.CapacityHours,
                allocatedHours     = r.AllocatedHours,
                actualHours        = r.ActualHours,
                taskCount          = r.TaskCount,
                doneCount          = r.DoneCount,
                completionPct      = r.CompletionPct,
                hoursCompletionPct = r.HoursCompletionPct
            }).ToList(),
            totalCapacity    = data.TotalCapacity,
            totalAllocated   = data.TotalAllocated,
            totalActualHours = data.TotalActualHours
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.TeamLead)]
    public async Task<IActionResult> SaveCapacity([FromBody] SprintCapacityRequest req)
    {
        if (req == null) return BadRequest();
        if (NoTeam(out _)) return BadRequest(new { message = "No active team." });
        if (!LegacySprintMutationsEnabled()) return SprintMutationsDisabled();
        var ok = await sprints.SaveCapacityAsync(req, TeamId());
        return ok ? Json(new { success = true }) : NotFound();
    }
}
