using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TaskTracker.Data;
using TaskTracker.Models;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Controllers;

[Authorize]
public class DashboardController(AppDbContext db) : Controller
{
    private readonly AppDbContext _db = db;

    public async Task<IActionResult> Index()
    {
        var displayName = User.FindFirst("DisplayName")?.Value ?? User.Identity?.Name ?? "";
        var all   = await _db.Tasks.ToListAsync();
        var today = DateTime.Today;

        var mine = all
            .Where(t => string.Equals(t.Owner, displayName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var myDone       = mine.Count(t => t.Status == "Done");
        var myInProgress = mine.Count(t => t.Status is "On track" or "Testing");
        var myOverdue    = mine.Count(t =>
            t.Status is not ("Done" or "On hold") &&
            !string.IsNullOrEmpty(t.TargetDate) &&
            DateTime.TryParse(t.TargetDate, out var d) && d.Date < today);

        var myPending = mine
            .Where(t => t.Status != "Done")
            .OrderBy(t =>
            {
                if (!string.IsNullOrEmpty(t.TargetDate) && DateTime.TryParse(t.TargetDate, out var d)) return d;
                return DateTime.MaxValue;
            })
            .Take(10)
            .ToList();

        var myWorkingOn = mine
            .Where(t => t.Status is "On track" or "Testing")
            .ToList();

        var bestGroup = mine
            .Where(t => !string.IsNullOrEmpty(t.Project))
            .GroupBy(t => t.Project)
            .OrderByDescending(g => g.Count())
            .Select(g => new { Key = g.Key, Count = g.Count(), Done = g.Count(t => t.Status == "Done") })
            .FirstOrDefault();

        List<ActivityLog> myActivity = [];
        ActivityLog? lastActLog = null;
        TaskItem? lastTask = null;
        try
        {
            var myTaskIds = mine.Select(t => t.Id).ToHashSet();
            var recent = await _db.ActivityLog
                .OrderByDescending(a => a.ChangedAt)
                .Take(300)
                .ToListAsync();
            myActivity = recent.Where(a => myTaskIds.Contains(a.TaskId)).Take(12).ToList();
            lastActLog = myActivity.FirstOrDefault();
            if (lastActLog != null)
                lastTask = mine.FirstOrDefault(t => t.Id == lastActLog.TaskId);
        }
        catch { }

        ViewBag.DisplayName        = displayName;
        ViewBag.MyTotal            = mine.Count;
        ViewBag.MyDone             = myDone;
        ViewBag.MyInProgress       = myInProgress;
        ViewBag.MyOverdue          = myOverdue;
        ViewBag.MyCompletionPct    = mine.Count > 0 ? (int)Math.Round((double)myDone / mine.Count * 100) : 0;
        ViewBag.MyPending          = myPending;
        ViewBag.MyWorkingOn        = myWorkingOn;
        ViewBag.MyBestProject      = bestGroup?.Key;
        ViewBag.MyBestProjectCount = bestGroup?.Count ?? 0;
        ViewBag.MyBestProjectDone  = bestGroup?.Done  ?? 0;
        ViewBag.MyActivity         = myActivity;
        ViewBag.LastTask           = lastTask;
        ViewBag.LastActLog         = lastActLog;

        ViewData["ActiveNav"] = "Dashboard";
        return View();
    }

    public async Task<IActionResult> Team()
    {
        var all   = await _db.Tasks.ToListAsync();
        var today = DateTime.Today;

        var overdueCount = all.Count(t =>
            t.Status is not ("Done" or "On hold") &&
            !string.IsNullOrEmpty(t.TargetDate) &&
            DateTime.TryParse(t.TargetDate, out var d) && d.Date < today);

        var statusCounts = all
            .GroupBy(t => t.Status ?? "")
            .ToDictionary(g => g.Key, g => g.Count());

        var projectCounts = all
            .Where(t => !string.IsNullOrEmpty(t.Project))
            .GroupBy(t => t.Project)
            .OrderByDescending(g => g.Count())
            .Take(8)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToList();

        var ownerStats = all
            .Where(t => !string.IsNullOrEmpty(t.Owner))
            .GroupBy(t => t.Owner)
            .Select(g => new OwnerStat
            {
                Name       = g.Key ?? "",
                Total      = g.Count(),
                Done       = g.Count(t => t.Status == "Done"),
                InProgress = g.Count(t => t.Status is "On track" or "Testing"),
                Overdue    = g.Count(t =>
                    t.Status is not ("Done" or "On hold") &&
                    !string.IsNullOrEmpty(t.TargetDate) &&
                    DateTime.TryParse(t.TargetDate, out var d) && d.Date < today)
            })
            .OrderByDescending(o => o.Total)
            .ToList();

        ViewBag.TotalCount    = all.Count;
        ViewBag.DoneCount     = statusCounts.GetValueOrDefault("Done", 0);
        ViewBag.OverdueCount  = overdueCount;
        ViewBag.InProgress    = statusCounts.GetValueOrDefault("On track", 0)
                              + statusCounts.GetValueOrDefault("Testing", 0);

        ViewBag.StatusLabels  = JsonSerializer.Serialize(statusCounts.Keys.ToArray());
        ViewBag.StatusValues  = JsonSerializer.Serialize(statusCounts.Values.ToArray());
        ViewBag.ProjectLabels = JsonSerializer.Serialize(projectCounts.Select(p => p.Key).ToArray());
        ViewBag.ProjectValues = JsonSerializer.Serialize(projectCounts.Select(p => p.Count).ToArray());
        ViewBag.OwnerStats    = ownerStats;
        ViewBag.OwnerNames    = JsonSerializer.Serialize(ownerStats.Select(o => o.Name).ToArray());
        ViewBag.OwnerDone     = JsonSerializer.Serialize(ownerStats.Select(o => o.Done).ToArray());
        ViewBag.OwnerPending  = JsonSerializer.Serialize(ownerStats.Select(o => o.Total - o.Done).ToArray());

        List<ActivityLog> activity = [];
        try
        {
            activity = await _db.ActivityLog
                .OrderByDescending(a => a.ChangedAt)
                .Take(15)
                .ToListAsync();
        }
        catch { }

        ViewBag.RecentActivity = activity;
        ViewData["ActiveNav"]  = "Team";
        return View();
    }
}
