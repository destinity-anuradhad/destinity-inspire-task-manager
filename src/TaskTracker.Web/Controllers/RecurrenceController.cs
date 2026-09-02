using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Models;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class RecurrenceController(IRecurringTaskService recurring, ITaskTemplateService templates, IProjectService projects) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var teamId = ActiveTeamId();
        ViewBag.Templates = await templates.GetForTeamAsync(teamId);
        ViewBag.Members = teamId.HasValue ? await projects.GetTeamMembersAsync(teamId.Value) : [];
        ViewData["ActiveNav"] = "Recurrence";
        return View(await recurring.GetForTeamAsync(teamId));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RecurringTaskDefinition definition)
    {
        var (ok, error) = await recurring.CreateAsync(definition, BuildUserCtx());
        if (!ok) TempData["Error"] = error;
        else TempData["Success"] = "Recurring work definition created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, bool isActive)
    {
        var (ok, error) = await recurring.ToggleAsync(id, isActive, BuildUserCtx());
        if (!ok) TempData["Error"] = error;
        else TempData["Success"] = isActive ? "Schedule resumed." : "Schedule paused.";
        return RedirectToAction(nameof(Index));
    }
}
