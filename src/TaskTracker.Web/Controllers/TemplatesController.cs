using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Models;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class TemplatesController(ITaskTemplateService templates, IActivityService activity) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["ActiveNav"] = "Templates";
        return View(await templates.GetForTeamAsync(ActiveTeamId()));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(TaskTemplate template, string? checklistText)
    {
        var items = (checklistText ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (ok, error) = await templates.SaveAsync(template, items, BuildUserCtx());
        if (!ok) TempData["Error"] = error;
        else
        {
            await activity.LogAsync(CurrentDisplayName(), "Task Template Created", "Template", template.Name, teamId: ActiveTeamId());
            TempData["Success"] = "Template created.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var (ok, error) = await templates.DeleteAsync(id, BuildUserCtx());
        if (!ok) TempData["Error"] = error;
        else TempData["Success"] = "Template deleted.";
        return RedirectToAction(nameof(Index));
    }
}
