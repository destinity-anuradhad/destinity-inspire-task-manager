using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;
using TaskTracker.Web.Auth;

namespace TaskTracker.Controllers;

[Authorize(Policy = Policies.SystemAdmin)]
public class ActivityController(IActivityService activity) : AppControllerBase
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
}
