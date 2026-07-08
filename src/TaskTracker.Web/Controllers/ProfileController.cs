using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskTracker.Models;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class ProfileController(IUserService userService, IActivityService activity, IWebHostEnvironment env) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var uid  = int.Parse(User.FindFirst("UserId")!.Value);
        var user = await userService.GetByIdAsync(uid);
        if (user == null) return NotFound();

        var stats = await userService.GetProfileStatsAsync(uid, user.DisplayName);
        ViewBag.TasksAssigned  = stats.TasksAssigned;
        ViewBag.TasksCompleted = stats.TasksCompleted;
        ViewBag.TasksWatching  = stats.TasksWatching;

        ViewData["ActiveNav"] = "Profile";
        return View(user);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(string displayName, IFormFile? avatar, string? color, string? jobTitle, string? department, string? email)
    {
        var uid  = int.Parse(User.FindFirst("UserId")!.Value);
        var user = await userService.GetByIdAsync(uid);
        if (user == null) return NotFound();

        string? newAvatarPath = null;
        if (avatar != null && avatar.Length > 0)
        {
            var ext = Path.GetExtension(avatar.FileName).ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".gif" or ".webp"))
            {
                TempData["ProfileError"] = "Unsupported image format. Use JPG, PNG, GIF or WebP.";
                return RedirectToAction(nameof(Index));
            }
            if (avatar.Length > 3 * 1024 * 1024)
            {
                TempData["ProfileError"] = "Image must be under 3 MB.";
                return RedirectToAction(nameof(Index));
            }
            try
            {
                var dir = Path.Combine(env.WebRootPath, "uploads", "avatars");
                Directory.CreateDirectory(dir);
                var fileName = $"{user.Username}{ext}";
                var filePath = Path.Combine(dir, fileName);
                await using var fs = System.IO.File.Create(filePath);
                await avatar.CopyToAsync(fs);
                newAvatarPath = $"/uploads/avatars/{fileName}";
            }
            catch (Exception ex)
            {
                TempData["ProfileError"] = $"Photo could not be saved: {ex.Message}";
            }
        }

        var oldName = user.DisplayName;
        var req = new ProfileUpdateRequest
        {
            DisplayName   = displayName,
            Color         = color,
            JobTitle      = jobTitle,
            Department    = department,
            Email         = email,
            NewAvatarPath = newAvatarPath,
        };
        await userService.UpdateProfileAsync(uid, req);

        var updated = await userService.GetByIdAsync(uid);
        if (updated != null) await RefreshClaims(updated);

        var details = string.Equals(oldName, displayName?.Trim(), StringComparison.Ordinal)
            ? "Profile updated"
            : $"Display name: '{oldName}' → '{displayName?.Trim()}'";
        await activity.LogAsync(displayName?.Trim() ?? oldName, "Profile Updated", "System", details,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers["User-Agent"].ToString());

        TempData["ProfileSuccess"] = "Profile updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        if (newPassword != confirmPassword)
        {
            TempData["PasswordError"] = "New passwords do not match.";
            return RedirectToAction(nameof(Index));
        }
        if (newPassword.Length < 6)
        {
            TempData["PasswordError"] = "Password must be at least 6 characters.";
            return RedirectToAction(nameof(Index));
        }

        var uid = int.Parse(User.FindFirst("UserId")!.Value);
        var (ok, error) = await userService.ChangePasswordAsync(uid, currentPassword, newPassword);
        if (!ok)
        {
            TempData["PasswordError"] = error ?? "Password change failed.";
            return RedirectToAction(nameof(Index));
        }

        var displayName = User.FindFirst("DisplayName")?.Value ?? "";
        await activity.LogAsync(displayName, "Password Changed", "System", "Password changed successfully",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers["User-Agent"].ToString());
        TempData["PasswordSuccess"] = "Password changed successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateWorkPreferences(
        decimal? dailyCapacityHours,
        bool workMon, bool workTue, bool workWed, bool workThu, bool workFri, bool workSat, bool workSun)
    {
        var uid = int.Parse(User.FindFirst("UserId")!.Value);
        var days = new List<string>();
        if (workMon) days.Add("Mon");
        if (workTue) days.Add("Tue");
        if (workWed) days.Add("Wed");
        if (workThu) days.Add("Thu");
        if (workFri) days.Add("Fri");
        if (workSat) days.Add("Sat");
        if (workSun) days.Add("Sun");
        var workingDays = string.Join(",", days);
        await userService.UpdateWorkPreferencesAsync(uid, dailyCapacityHours, workingDays);

        var displayName = User.FindFirst("DisplayName")?.Value ?? "";
        await activity.LogAsync(displayName, "Work Preferences Updated", "System",
            $"Capacity: {dailyCapacityHours}h/day · Days: {workingDays}",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers["User-Agent"].ToString());
        TempData["WorkPrefSuccess"] = "Work preferences saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateNotifications(bool notifyOnAssigned, bool notifyOnComment, bool notifyOnBlocked)
    {
        var uid = int.Parse(User.FindFirst("UserId")!.Value);
        await userService.UpdateNotificationsAsync(uid, notifyOnAssigned, notifyOnComment, notifyOnBlocked);

        var displayName = User.FindFirst("DisplayName")?.Value ?? "";
        await activity.LogAsync(displayName, "Notifications Updated", "System",
            $"Assigned:{notifyOnAssigned} Comment:{notifyOnComment} Blocked:{notifyOnBlocked}",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers["User-Agent"].ToString());
        TempData["NotifSuccess"] = "Notification preferences saved.";
        return RedirectToAction(nameof(Index));
    }

    private async Task RefreshClaims(AppUser user)
    {
        // Preserve the four team-context claims that exist in the current cookie so
        // a profile edit does not strip ActiveTeamId and silently disable team scoping.
        var preserve = new[] { "ActiveTeamId", "ActiveTeamName", "ActiveTeamColor", "TeamRole" };
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name,    user.Username),
            new(ClaimTypes.Role,    user.Role),
            new("UserId",           user.Id.ToString()),
            new("DisplayName",      user.DisplayName),
            new("ProfileImagePath", user.ProfileImagePath ?? ""),
            new("AvatarColor",      user.Color ?? "#3b82f6"),
        };
        foreach (var name in preserve)
        {
            var existing = User.FindFirst(name);
            if (existing != null) claims.Add(new Claim(name, existing.Value));
        }
        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true });
    }
}
