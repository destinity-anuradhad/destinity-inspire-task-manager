using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Controllers;

[Authorize(Roles = "Admin")]
public class MasterController(AppDbContext db) : Controller
{
    private readonly AppDbContext _db = db;

    // GET /Master
    public async Task<IActionResult> Index(string tab = "projects")
    {
        ViewBag.Projects    = await _db.Projects.OrderBy(p => p.Name).ToListAsync();
        ViewBag.Properties  = await _db.Properties.OrderBy(p => p.Name).ToListAsync();
        ViewBag.Users       = await _db.AppUsers.OrderBy(u => u.DisplayName).ToListAsync();
        ViewBag.WorkStates  = await _db.WorkStates.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        ViewBag.ActiveTab   = tab;
        ViewData["ActiveNav"] = "Master";
        return View();
    }

    // ── Projects ─────────────────────────────────────────────────

    [HttpPost][ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProject(string name, string color)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _db.Projects.Add(new ProjectItem
            {
                Name  = name.Trim(),
                Color = string.IsNullOrEmpty(color) ? "#094f70" : color
            });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index), new { tab = "projects" });
    }

    [HttpPost][ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProject(int id, string name, string color)
    {
        var item = await _db.Projects.FindAsync(id);
        if (item != null && !string.IsNullOrWhiteSpace(name))
        {
            item.Name  = name.Trim();
            if (!string.IsNullOrEmpty(color)) item.Color = color;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index), new { tab = "projects" });
    }

    [HttpPost][ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProject(int id)
    {
        var item = await _db.Projects.FindAsync(id);
        if (item != null) { _db.Projects.Remove(item); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index), new { tab = "projects" });
    }

    // ── Properties ───────────────────────────────────────────────

    [HttpPost][ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProperty(string name, string color)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _db.Properties.Add(new PropertyItem
            {
                Name  = name.Trim(),
                Color = string.IsNullOrEmpty(color) ? "#094f70" : color
            });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index), new { tab = "properties" });
    }

    [HttpPost][ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProperty(int id, string name, string color)
    {
        var item = await _db.Properties.FindAsync(id);
        if (item != null && !string.IsNullOrWhiteSpace(name))
        {
            item.Name  = name.Trim();
            if (!string.IsNullOrEmpty(color)) item.Color = color;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index), new { tab = "properties" });
    }

    [HttpPost][ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProperty(int id)
    {
        var item = await _db.Properties.FindAsync(id);
        if (item != null) { _db.Properties.Remove(item); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index), new { tab = "properties" });
    }

    // ── Work States ──────────────────────────────────────────────

    [HttpPost][ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWorkState(string name, string color)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var maxOrder = await _db.WorkStates.AnyAsync()
                ? await _db.WorkStates.MaxAsync(s => s.SortOrder)
                : 0;
            _db.WorkStates.Add(new WorkState
            {
                Name      = name.Trim(),
                Color     = string.IsNullOrEmpty(color) ? "#094f70" : color,
                SortOrder = maxOrder + 1
            });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index), new { tab = "workstates" });
    }

    [HttpPost][ValidateAntiForgeryToken]
    public async Task<IActionResult> EditWorkState(int id, string name, string color)
    {
        var item = await _db.WorkStates.FindAsync(id);
        if (item != null && !string.IsNullOrWhiteSpace(name))
        {
            item.Name  = name.Trim();
            if (!string.IsNullOrEmpty(color)) item.Color = color;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index), new { tab = "workstates" });
    }

    [HttpPost][ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteWorkState(int id)
    {
        var item = await _db.WorkStates.FindAsync(id);
        if (item != null) { _db.WorkStates.Remove(item); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index), new { tab = "workstates" });
    }

    // ── Users ────────────────────────────────────────────────────

    [HttpPost][ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(int id, string username, string displayName, string role, string color)
    {
        var user = await _db.AppUsers.FindAsync(id);
        if (user != null)
        {
            var oldName = user.DisplayName;
            if (!string.IsNullOrWhiteSpace(username))    user.Username    = username.Trim();
            if (!string.IsNullOrWhiteSpace(displayName)) user.DisplayName = displayName.Trim();
            if (role is "Admin" or "Member")             user.Role        = role;
            if (!string.IsNullOrEmpty(color))            user.Color       = color;
            await _db.SaveChangesAsync();

            var newName = user.DisplayName;
            if (!string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                var owner = await _db.Owners.FirstOrDefaultAsync(o => o.Name == oldName);
                if (owner != null) owner.Name = newName;

                var tasks = await _db.Tasks
                    .Where(t => t.Owner != null && t.Owner.Contains(oldName))
                    .ToListAsync();
                foreach (var task in tasks)
                {
                    task.Owner = string.Join(", ",
                        (task.Owner ?? "").Split(',')
                            .Select(o => o.Trim())
                            .Select(o => string.Equals(o, oldName, StringComparison.OrdinalIgnoreCase) ? newName : o));
                }
                await _db.SaveChangesAsync();
            }

            TempData["Success"] = $"User '{user.DisplayName}' updated.";
        }
        return RedirectToAction(nameof(Index), new { tab = "users" });
    }
}
