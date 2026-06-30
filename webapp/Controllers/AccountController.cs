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

public class AccountController(AppDbContext db) : Controller
{
    private readonly AppDbContext _db = db;

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect(returnUrl ?? "/Dashboard");
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [AllowAnonymous]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null)
        {
            ViewBag.Error = "Invalid username or password.";
            return View();
        }

        var hasher = new PasswordHasher<AppUser>();
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            ViewBag.Error = "Invalid username or password.";
            return View();
        }

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

        return Redirect(returnUrl ?? "/Dashboard");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    // AJAX — unread notification count
    [HttpGet]
    public async Task<IActionResult> NotificationCount()
    {
        var uid = int.Parse(User.FindFirst("UserId")!.Value);
        var count = await _db.Notifications.CountAsync(n => n.UserId == uid && !n.IsRead);
        return Json(new { count });
    }

    // AJAX — notification list HTML
    [HttpGet]
    public async Task<IActionResult> NotificationList()
    {
        var uid = int.Parse(User.FindFirst("UserId")!.Value);
        var items = await _db.Notifications
            .Where(n => n.UserId == uid)
            .OrderByDescending(n => n.CreatedAt)
            .Take(15)
            .ToListAsync();
        return PartialView("_NotificationList", items);
    }

    // AJAX — mark all as read (no antiforgery needed — not a state-mutating risk from external origin)
    [HttpPost, IgnoreAntiforgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        var uid = int.Parse(User.FindFirst("UserId")!.Value);
        await _db.Notifications
            .Where(n => n.UserId == uid && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return Json(new { success = true });
    }
}
