using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Controllers;

[Authorize]
public class ActivityController(AppDbContext db) : Controller
{
    private readonly AppDbContext _db = db;

    public async Task<IActionResult> Index(int page = 1, string? project = null, string? user = null, string? dateFrom = null, string? dateTo = null)
    {
        const int pageSize = 50;
        List<ActivityLog> logs = [];
        int totalCount = 0;

        try
        {
            var query = _db.ActivityLog.AsQueryable();

            if (!string.IsNullOrWhiteSpace(project))
                query = query.Where(a => a.Project == project);

            if (!string.IsNullOrWhiteSpace(user))
                query = query.Where(a => a.ChangedBy == user);

            if (DateTime.TryParse(dateFrom, out var from))
                query = query.Where(a => a.ChangedAt >= from);

            if (DateTime.TryParse(dateTo, out var to))
                query = query.Where(a => a.ChangedAt < to.AddDays(1));

            totalCount = await query.CountAsync();
            logs = await query
                .OrderByDescending(a => a.ChangedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Load distinct values for filter dropdowns
            ViewBag.Projects = await _db.ActivityLog
                .Where(a => a.Project != null && a.Project != "")
                .Select(a => a.Project)
                .Distinct()
                .OrderBy(p => p)
                .ToListAsync();

            ViewBag.Users = await _db.ActivityLog
                .Where(a => a.ChangedBy != null && a.ChangedBy != "")
                .Select(a => a.ChangedBy)
                .Distinct()
                .OrderBy(u => u)
                .ToListAsync();

            // Color map for user avatars
            ViewBag.UserColors = (await _db.AppUsers.ToListAsync())
                .ToDictionary(u => u.DisplayName, u => u.Color ?? "#094f70", StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            ViewBag.SetupRequired = true;
            ViewBag.Projects   = new List<string>();
            ViewBag.Users      = new List<string>();
            ViewBag.UserColors = new Dictionary<string, string>();
        }

        ViewBag.Logs       = logs;
        ViewBag.Page       = page;
        ViewBag.PageSize   = pageSize;
        ViewBag.TotalCount = totalCount;
        ViewBag.TotalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0;

        // Pass active filters back to view
        ViewBag.FilterProject  = project  ?? "";
        ViewBag.FilterUser     = user     ?? "";
        ViewBag.FilterDateFrom = dateFrom ?? "";
        ViewBag.FilterDateTo   = dateTo   ?? "";

        ViewData["ActiveNav"] = "Activity";
        return View();
    }
}
