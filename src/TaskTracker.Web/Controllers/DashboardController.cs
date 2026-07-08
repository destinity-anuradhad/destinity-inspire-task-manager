using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class DashboardController(IDashboardService dashboard) : AppControllerBase
{

    public async Task<IActionResult> Index()
    {
        var displayName = CurrentDisplayName();
        var data = await dashboard.GetPersonalAsync(displayName, ActiveTeamId());

        ViewBag.DisplayName        = displayName;
        ViewBag.MyTotal            = data.Total;
        ViewBag.MyDone             = data.Done;
        ViewBag.MyInProgress       = data.InProgress;
        ViewBag.MyOpen             = data.Open;
        ViewBag.MyOverdue          = data.Overdue;
        ViewBag.MyCompletionPct    = data.CompletionPct;
        ViewBag.MyPending          = data.Pending;
        ViewBag.MyWorkingOn        = data.WorkingOn;
        ViewBag.MyByType           = data.ByType;
        ViewBag.MyByPriority       = data.ByPriority;
        ViewBag.MyBestProject      = data.BestProject;
        ViewBag.MyBestProjectCount = data.BestProjectCount;
        ViewBag.MyBestProjectDone  = data.BestProjectDone;
        ViewBag.MyActivity         = data.Activity;
        ViewBag.LastTask           = data.LastTask;
        ViewBag.LastActLog         = data.LastActLog;
        ViewBag.TeamName           = ActiveTeamName();

        ViewData["ActiveNav"] = "Dashboard";
        return View();
    }

    public async Task<IActionResult> Team()
    {
        var data = await dashboard.GetTeamAsync(ActiveTeamId());

        ViewBag.TotalCount     = data.Total;
        ViewBag.DoneCount      = data.Done;
        ViewBag.OverdueCount   = data.Overdue;
        ViewBag.InProgress     = data.InProgress;
        ViewBag.StatusLabels   = data.StatusLabels;
        ViewBag.StatusValues   = data.StatusValues;
        ViewBag.ProjectLabels  = data.ProjectLabels;
        ViewBag.ProjectValues  = data.ProjectValues;
        ViewBag.TypeLabels     = data.TypeLabels;
        ViewBag.TypeValues     = data.TypeValues;
        ViewBag.PriorityLabels = data.PriorityLabels;
        ViewBag.PriorityValues = data.PriorityValues;
        ViewBag.OwnerStats     = data.OwnerStats;
        ViewBag.OwnerColorMap  = data.OwnerColorMap;
        ViewBag.OwnerImageMap  = data.OwnerImageMap;
        ViewBag.OwnerNames     = data.OwnerNames;
        ViewBag.OwnerDone      = data.OwnerDone;
        ViewBag.OwnerPending   = data.OwnerPending;
        ViewBag.RecentActivity = data.RecentActivity;
        ViewBag.TeamName       = User.FindFirst("ActiveTeamName")?.Value ?? "";

        ViewData["ActiveNav"] = "Team";
        return View();
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Teams()
    {
        var stats = await dashboard.GetAllTeamsStatsAsync();
        ViewBag.TeamStats     = stats;
        ViewData["ActiveNav"] = "Teams";
        return View();
    }
}
