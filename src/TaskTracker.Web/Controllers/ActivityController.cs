using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;
using TaskTracker.Web.Auth;

namespace TaskTracker.Controllers;

[Authorize(Policy = Policies.SystemAdmin)]
public class ActivityController(IActivityService activity, IAuditExportService auditExport) : AppControllerBase
{
    public async Task<IActionResult> Index(
        int page = 1,
        string? project  = null,
        string? user     = null,
        string? dateFrom = null,
        string? dateTo   = null,
        string? category = null,
        int?    team     = null)
    {
        const int pageSize = 50;
        bool isAdmin = IsAdmin();
        int? teamId  = ActiveTeamId();

        var result = await activity.GetLogsAsync(isAdmin, teamId, page, project, user, dateFrom, dateTo, category, team);

        ViewBag.Logs       = result.Logs;
        ViewBag.Page       = page;
        ViewBag.PageSize   = pageSize;
        ViewBag.TotalCount = result.TotalCount;
        ViewBag.TotalPages = result.TotalCount > 0 ? (int)Math.Ceiling((double)result.TotalCount / pageSize) : 0;
        ViewBag.IsAdmin    = isAdmin;
        ViewBag.Projects   = result.Projects;
        ViewBag.Users      = result.Users;
        ViewBag.UserColors = result.UserColors;
        ViewBag.UserImages = result.UserImages;
        if (result.TeamNames  != null) ViewBag.TeamNames     = result.TeamNames;
        if (result.SetupRequired)      ViewBag.SetupRequired = true;

        ViewBag.FilterProject  = project  ?? "";
        ViewBag.FilterUser     = user     ?? "";
        ViewBag.FilterDateFrom = dateFrom ?? "";
        ViewBag.FilterDateTo   = dateTo   ?? "";
        ViewBag.FilterCategory = category ?? "";
        ViewBag.FilterTeam     = team;

        ViewData["ActiveNav"] = "Activity";
        return View();
    }

    // ── Audit export & retention (Phase 4 / compliance) ────────────────────────

    /// <summary>Downloads the filtered audit trail as CSV. System-Admin only.</summary>
    [HttpGet]
    public async Task<IActionResult> Export(
        string? dateFrom = null, string? dateTo = null,
        string? category = null, string? user = null, int? team = null)
    {
        DateTime? from = DateTime.TryParse(dateFrom, out var f) ? f : null;
        DateTime? to   = DateTime.TryParse(dateTo,   out var t) ? t.AddDays(1) : null;   // inclusive day

        var result = await auditExport.ExportAsync(new AuditExportFilter(from, to, category, user, team));
        var csv = AuditExportService.ToCsv(result.Rows);
        var name = $"audit-log-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";

        await activity.LogAsync(CurrentDisplayName(), "Audit Export", "System",
            $"Exported {result.Rows.Count} of {result.TotalMatched} audit entrie(s){(result.Truncated ? " (truncated)" : "")}",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers.UserAgent.ToString());

        return File(csv, "text/csv; charset=utf-8", name);
    }

    /// <summary>Read-only preview of how many entries a retention cutoff would remove.</summary>
    [HttpGet]
    public async Task<IActionResult> RetentionPreview(int retainDays)
    {
        if (retainDays < 1) return BadRequest(new { error = "retainDays must be at least 1." });
        var cutoff = DateTime.Now.AddDays(-retainDays);
        var count = await auditExport.CountOlderThanAsync(cutoff);
        return Json(new { retainDays, cutoff, count });
    }

    /// <summary>Permanently deletes audit entries older than the retention window. Guarded + audited.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Purge(int retainDays, bool confirm)
    {
        if (retainDays < 1)
        {
            TempData["Error"] = "Retention window must be at least 1 day.";
            return RedirectToAction(nameof(Index));
        }
        if (!confirm)
        {
            TempData["Error"] = "Confirm the purge to proceed.";
            return RedirectToAction(nameof(Index));
        }

        var cutoff = DateTime.Now.AddDays(-retainDays);
        var deleted = await auditExport.PurgeOlderThanAsync(cutoff, CurrentDisplayName());
        TempData["Success"] = $"Purged {deleted} audit entrie(s) older than {retainDays} day(s).";
        return RedirectToAction(nameof(Index));
    }
}
