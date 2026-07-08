using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class ChartsController(IChartService charts) : Controller
{
    private int? ActiveTeamId() => int.TryParse(User.FindFirst("ActiveTeamId")?.Value, out var id) ? id : (int?)null;

    public async Task<IActionResult> Index()
    {
        var data = await charts.GetChartDataAsync(ActiveTeamId());

        ViewBag.TotalTasks   = data.TotalTasks;
        ViewBag.VelocityJson = data.VelocityJson;
        ViewBag.StatusJson   = data.StatusJson;
        ViewBag.PriorityJson = data.PriorityJson;
        ViewBag.OwnerJson    = data.OwnerJson;
        ViewBag.PropertyJson = data.PropertyJson;
        ViewBag.ProjectJson  = data.ProjectJson;

        ViewData["ActiveNav"] = "Charts";
        return View();
    }
}
