using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TaskTracker.Data;

namespace TaskTracker.Controllers;

[Authorize]
public class ChartsController(AppDbContext db) : Controller
{
    private readonly AppDbContext _db = db;

    public async Task<IActionResult> Index()
    {
        var tasks = await _db.Tasks.ToListAsync();

        // By Status
        var byStatus = tasks
            .GroupBy(t => string.IsNullOrEmpty(t.Status) ? "Unknown" : t.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        // By Priority
        var byPriority = tasks
            .GroupBy(t => string.IsNullOrEmpty(t.Priority) ? "None" : t.Priority)
            .ToDictionary(g => g.Key, g => g.Count());

        // By Owner
        var byOwner = tasks
            .Where(t => !string.IsNullOrEmpty(t.Owner))
            .GroupBy(t => t.Owner)
            .OrderByDescending(g => g.Count())
            .Take(15)
            .ToDictionary(g => g.Key, g => g.Count());

        // By Property
        var byProperty = tasks
            .Where(t => !string.IsNullOrEmpty(t.Property))
            .GroupBy(t => t.Property)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        // By Project
        var byProject = tasks
            .Where(t => !string.IsNullOrEmpty(t.Project))
            .GroupBy(t => t.Project)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        ViewBag.TotalTasks    = tasks.Count;
        ViewBag.StatusJson    = JsonSerializer.Serialize(byStatus);
        ViewBag.PriorityJson  = JsonSerializer.Serialize(byPriority);
        ViewBag.OwnerJson     = JsonSerializer.Serialize(byOwner);
        ViewBag.PropertyJson  = JsonSerializer.Serialize(byProperty);
        ViewBag.ProjectJson   = JsonSerializer.Serialize(byProject);

        ViewData["ActiveNav"] = "Charts";
        return View();
    }
}
