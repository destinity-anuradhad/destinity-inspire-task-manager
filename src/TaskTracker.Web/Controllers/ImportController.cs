using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;
using TaskTracker.Web.Auth;

namespace TaskTracker.Controllers;

/// <summary>
/// CSV bulk import of tasks. Uploading previews (parses + validates, writes nothing); applying
/// creates the valid rows through the governed task command. System administrators and team
/// administrators only.
/// </summary>
[Authorize(Policy = Policies.TeamAdmin)]
public class ImportController(IImportService import, IActivityService activity) : AppControllerBase
{
    private const long MaxFileBytes = 5 * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewBag.Jobs = await import.ListJobsAsync(ActiveTeamId(), IsSysAdmin());
        ViewData["ActiveNav"] = "Import";
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(MaxFileBytes)]
    public async Task<IActionResult> Upload(IFormFile? file)
    {
        var teamId = ActiveTeamId();
        if (teamId is null)
        {
            TempData["Error"] = "Select a team before importing.";
            return RedirectToAction(nameof(Index));
        }
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose a CSV file to import.";
            return RedirectToAction(nameof(Index));
        }
        if (file.Length > MaxFileBytes)
        {
            TempData["Error"] = "File is too large (max 5 MB).";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var preview = await import.PreviewAsync(teamId.Value, CurrentUserId(), CurrentDisplayName(), file.FileName, stream);
            return RedirectToAction(nameof(Preview), new { id = preview.Job.Id });
        }
        catch (ArgumentException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Preview(int id)
    {
        var result = await import.GetJobAsync(id, ActiveTeamId() ?? 0, IsSysAdmin());
        if (result == null) return NotFound();
        ViewData["ActiveNav"] = "Import";
        return View(result);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(int id)
    {
        var result = await import.ApplyAsync(id, BuildUserCtx(), IsSysAdmin());
        if (result == null)
        {
            TempData["Error"] = "Import not found.";
            return RedirectToAction(nameof(Index));
        }

        await activity.LogAsync(CurrentDisplayName(), "Import Applied", "System",
            $"Job #{id}: created {result.Created}, skipped {result.Skipped}, failed {result.Failed}",
            HttpContext.Connection.RemoteIpAddress?.ToString(), HttpContext.Request.Headers.UserAgent.ToString());

        TempData["Success"] = $"Imported {result.Created} task(s). Skipped {result.Skipped}, failed {result.Failed}.";
        return RedirectToAction(nameof(Preview), new { id });
    }
}
