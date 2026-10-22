using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class QueriesController(IQueryService queries) : AppControllerBase
{

    public async Task<IActionResult> Index()
    {
        var list = await queries.GetQueriesAsync(CurrentDisplayName(), ActiveTeamId());
        ViewData["ActiveNav"] = "Queries";
        return View(list);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string name, string? description, bool isShared, string filters)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Json(new { success = false, error = "Name is required" });
        var (ok, error) = await queries.SaveQueryAsync(name, description, isShared, filters, CurrentDisplayName(), ActiveTeamId());
        return ok ? Json(new { success = true }) : Json(new { success = false, error });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleShare(int id)
    {
        var (ok, error, isShared) = await queries.ToggleShareAsync(id, CurrentDisplayName(), IsAdmin(), ActiveTeamId());
        return ok ? Json(new { success = true, isShared }) : Json(new { success = false, error });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var (ok, error) = await queries.DeleteAsync(id, CurrentDisplayName(), IsAdmin(), ActiveTeamId());
        return ok ? Json(new { success = true }) : Json(new { success = false, error });
    }

    public async Task<IActionResult> Run(int id)
    {
        var f = await queries.GetFiltersAsync(id, CurrentDisplayName(), IsAdmin(), ActiveTeamId());
        if (f == null) return NotFound();
        return RedirectToAction("Index", "Tasks", new
        {
            search   = f.GetValueOrDefault("search",   ""),
            project  = f.GetValueOrDefault("project",  ""),
            property = f.GetValueOrDefault("property", ""),
            owner    = f.GetValueOrDefault("owner",    ""),
            status   = f.GetValueOrDefault("status",   ""),
            priority = f.GetValueOrDefault("priority", ""),
            dateFrom = f.GetValueOrDefault("dateFrom", ""),
            dateTo   = f.GetValueOrDefault("dateTo",   ""),
            view     = f.GetValueOrDefault("view",     "table"),
            sort     = f.GetValueOrDefault("sort",     ""),
            sortDir  = f.GetValueOrDefault("sortDir",  "desc")
        });
    }
}
