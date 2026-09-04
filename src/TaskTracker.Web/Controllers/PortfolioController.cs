using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;
using TaskTracker.Web.Auth;

namespace TaskTracker.Controllers;

/// <summary>
/// Cross-team portfolio views (Phase 4). System administrators see the whole org's cross-team
/// dependencies; team administrators see only those touching their active team.
/// </summary>
[Authorize(Policy = Policies.TeamAdmin)]
public class PortfolioController(IPortfolioService portfolio) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Dependencies()
    {
        var report = await portfolio.GetCrossTeamDependenciesAsync(ActiveTeamId(), IsSysAdmin());
        ViewBag.IsSysAdmin = IsSysAdmin();
        ViewBag.ActiveTeam = ActiveTeamName();
        ViewData["ActiveNav"] = "Portfolio";
        return View(report);
    }
}
