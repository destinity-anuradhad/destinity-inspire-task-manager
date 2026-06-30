using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Controllers;

[Authorize]
public class SprintsController(AppDbContext db) : Controller
{
    private readonly AppDbContext _db = db;

    // GET /Sprints — admin only management page
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Index()
    {
        var sprints = await _db.Sprints.OrderByDescending(s => s.IsActive).ThenByDescending(s => s.CreatedAt).ToListAsync();
        var taskCounts = await _db.Tasks
            .Where(t => t.SprintId != null)
            .GroupBy(t => t.SprintId)
            .Select(g => new { SprintId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SprintId!.Value, x => x.Count);
        ViewBag.TaskCounts = taskCounts;
        ViewData["ActiveNav"] = "Sprints";
        return View(sprints);
    }

    // POST /Sprints/Save — create or update sprint (admin only)
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Sprint sprint)
    {
        try
        {
            if (sprint.Id == 0)
            {
                sprint.CreatedAt = DateTime.Now;
                sprint.CreatedBy = User.FindFirst("DisplayName")?.Value ?? User.Identity?.Name;
                _db.Sprints.Add(sprint);
            }
            else
            {
                var existing = await _db.Sprints.FindAsync(sprint.Id);
                if (existing == null) return NotFound();
                existing.Name      = sprint.Name;
                existing.StartDate = sprint.StartDate;
                existing.EndDate   = sprint.EndDate;
                existing.Goal      = sprint.Goal;
            }
            await _db.SaveChangesAsync();
            return Ok(new { message = "Saved" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // POST /Sprints/SetActive/5 — mark one sprint active, deactivate all others
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetActive(int id)
    {
        var sprints = await _db.Sprints.ToListAsync();
        foreach (var s in sprints) s.IsActive = s.Id == id;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // POST /Sprints/Deactivate — clear active sprint
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Deactivate()
    {
        var sprints = await _db.Sprints.ToListAsync();
        foreach (var s in sprints) s.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // POST /Sprints/Delete/5 — delete sprint only if no tasks assigned
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var sprint = await _db.Sprints.FindAsync(id);
        if (sprint == null) return NotFound();
        var hasTasks = await _db.Tasks.AnyAsync(t => t.SprintId == id);
        if (hasTasks) return BadRequest(new { message = "Cannot delete sprint with assigned tasks. Move or unassign them first." });
        _db.Sprints.Remove(sprint);
        await _db.SaveChangesAsync();
        return Ok();
    }
}
