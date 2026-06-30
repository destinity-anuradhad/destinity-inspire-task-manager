using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Controllers;

[Authorize]
public class ProfileController(AppDbContext db, IWebHostEnvironment env) : Controller
{
    private readonly AppDbContext      _db  = db;
    private readonly IWebHostEnvironment _env = env;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var uid  = int.Parse(User.FindFirst("UserId")!.Value);
        var user = await _db.AppUsers.FindAsync(uid);
        if (user == null) return NotFound();
        ViewData["ActiveNav"] = "Profile";
        return View(user);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(string displayName, IFormFile? avatar)
    {
        var uid  = int.Parse(User.FindFirst("UserId")!.Value);
        var user = await _db.AppUsers.FindAsync(uid);
        if (user == null) return NotFound();

        var oldName = user.DisplayName;
        user.DisplayName = displayName.Trim();
        var newName = user.DisplayName;

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

            var dir = Path.Combine(_env.WebRootPath, "uploads", "avatars");
            Directory.CreateDirectory(dir);
            var fileName = $"{user.Username}{ext}";
            var filePath = Path.Combine(dir, fileName);
            await using var fs = System.IO.File.Create(filePath);
            await avatar.CopyToAsync(fs);
            user.ProfileImagePath = $"/uploads/avatars/{fileName}";
        }

        await _db.SaveChangesAsync();

        // Sync Owners master table and task owners when display name changes
        if (!string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            await SyncDisplayNameChange(oldName, newName);
        }

        // Re-issue cookie so new DisplayName and ProfileImagePath take effect
        await RefreshClaims(user);

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

        var uid  = int.Parse(User.FindFirst("UserId")!.Value);
        var user = await _db.AppUsers.FindAsync(uid);
        if (user == null) return NotFound();

        var hasher = new PasswordHasher<AppUser>();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            TempData["PasswordError"] = "Current password is incorrect.";
            return RedirectToAction(nameof(Index));
        }

        user.PasswordHash = hasher.HashPassword(user, newPassword);
        await _db.SaveChangesAsync();
        TempData["PasswordSuccess"] = "Password changed successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateNotifications(bool notifyOnAssigned)
    {
        var uid  = int.Parse(User.FindFirst("UserId")!.Value);
        var user = await _db.AppUsers.FindAsync(uid);
        if (user == null) return NotFound();

        user.NotifyOnAssigned = notifyOnAssigned;
        await _db.SaveChangesAsync();
        TempData["NotifSuccess"] = "Notification preferences saved.";
        return RedirectToAction(nameof(Index));
    }

    private async Task SyncDisplayNameChange(string oldName, string newName)
    {
        // Update Owners master table
        var owner = await _db.Owners.FirstOrDefaultAsync(o => o.Name == oldName);
        if (owner != null)
            owner.Name = newName;

        // Update Tasks.Owner (handles comma-separated multi-owner fields)
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

    private async Task RefreshClaims(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name,    user.Username),
            new(ClaimTypes.Role,    user.Role),
            new("UserId",           user.Id.ToString()),
            new("DisplayName",      user.DisplayName),
            new("ProfileImagePath", user.ProfileImagePath ?? ""),
        };
        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true });
    }
}
