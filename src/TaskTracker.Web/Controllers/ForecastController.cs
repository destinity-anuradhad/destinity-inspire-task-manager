using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;
using TaskTracker.Web.Auth;

namespace TaskTracker.Controllers;

/// <summary>
/// Velocity-based delivery forecasting (Phase 4). Team leads and above see how many sprints their
/// remaining backlog is projected to take, from historical completed velocity.
/// </summary>
[Authorize(Policy = Policies.TeamLead)]
public class ForecastController(IForecastService forecast) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (ActiveTeamId() is not int teamId)
            return RedirectToAction("AutoSelect", "Teams");

        var model = await forecast.GetForecastAsync(teamId);
        ViewBag.ActiveTeam = ActiveTeamName();
        ViewData["ActiveNav"] = "Forecast";
        return View(model);
    }
}
