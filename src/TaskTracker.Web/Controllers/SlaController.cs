using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;
using TaskTracker.Web.Auth;

namespace TaskTracker.Controllers;

/// <summary>
/// Interactive management for team SLA policies plus a live compliance report. System administrators
/// manage every team's policies; team administrators manage their active team's. Reporting is
/// read-only over existing task data.
/// </summary>
[Authorize(Policy = Policies.TeamAdmin)]
public class SlaController(ISlaService sla, IActivityService activity) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var policies = await sla.ListAsync(ActiveTeamId(), IsSysAdmin());
        ViewBag.IsSysAdmin = IsSysAdmin();
        ViewBag.ActiveTeam = ActiveTeamName();
        ViewBag.Report = ActiveTeamId() is int teamId ? await sla.GetReportAsync(teamId) : null;
        ViewData["ActiveNav"] = "Sla";
        return View(policies);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? appliesToItemType, string? appliesToPriority, int targetHours)
    {
        var teamId = ActiveTeamId();
        if (teamId is null)
        {
            TempData["Error"] = "Select a team before creating an SLA policy.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var policy = await sla.CreateAsync(new SlaPolicyInput(
                name ?? "", teamId.Value, appliesToItemType, appliesToPriority, targetHours, CurrentDisplayName()));

            await activity.LogAsync(CurrentDisplayName(), "SLA Policy Created", "System",
                $"Policy '{policy.Name}' — target {policy.TargetHours}h",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers.UserAgent.ToString());

            TempData["Success"] = "SLA policy created.";
        }
        catch (ArgumentException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, bool enabled)
    {
        var ok = await sla.SetEnabledAsync(id, enabled, ActiveTeamId(), IsSysAdmin());
        TempData[ok ? "Success" : "Error"] = ok
            ? (enabled ? "Policy enabled." : "Policy disabled.")
            : "That policy could not be updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var ok = await sla.DeleteAsync(id, ActiveTeamId(), IsSysAdmin());
        if (ok)
            await activity.LogAsync(CurrentDisplayName(), "SLA Policy Deleted", "System",
                $"Policy #{id} deleted",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers.UserAgent.ToString());

        TempData[ok ? "Success" : "Error"] = ok ? "SLA policy deleted." : "That policy could not be deleted.";
        return RedirectToAction(nameof(Index));
    }
}
