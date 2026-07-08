using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskTracker.Models;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class AccountController(IUserService userService, IActivityService activity, IConfiguration config, IHttpClientFactory httpFactory) : AppControllerBase
{

    // ── Login ─────────────────────────────────────────────────────────────────

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/Dashboard");
        ViewBag.ReturnUrl = returnUrl;
        SetRecaptchaViewBag();
        return View();
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        SetRecaptchaViewBag();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua = HttpContext.Request.Headers["User-Agent"].ToString();

        var captchaToken = Request.Form["g-recaptcha-response"].ToString();
        if (!await VerifyRecaptchaAsync(captchaToken))
        {
            await activity.LogAsync(username, "Login Failed", "Auth", "Failed reCAPTCHA", ip, ua);
            ViewBag.Error = "Please confirm you're not a robot and try again.";
            return View();
        }

        var user = await userService.GetByUsernameAsync(username);
        if (user == null)
        {
            await activity.LogAsync(username, "Login Failed", "Auth", "Unknown user", ip, ua);
            ViewBag.Error = "Invalid username or password.";
            return View();
        }

        var hasher = new PasswordHasher<AppUser>();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            await activity.LogAsync(user.DisplayName, "Login Failed", "Auth", "Bad password", ip, ua);
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
            new("AvatarColor",      user.Color ?? "#3b82f6"),
        };
        if (user.MustChangePassword)
            claims.Add(new Claim("MustChangePassword", "1"));

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true });

        await activity.LogAsync(user.DisplayName, "Login", "Auth", "Signed in", ip, ua);

        if (user.MustChangePassword)
            return RedirectToAction("ForceChangePassword");

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/Teams/AutoSelect");
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var name = User.FindFirst("DisplayName")?.Value ?? User.Identity?.Name ?? "";
        var ip   = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua   = HttpContext.Request.Headers["User-Agent"].ToString();
        await activity.LogAsync(name, "Logout", "Auth", "Signed out", ip, ua);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    // ── Forced password change ────────────────────────────────────────────────

    [HttpGet]
    public IActionResult ForceChangePassword()
    {
        if (User.FindFirst("MustChangePassword")?.Value != "1")
            return RedirectToAction("Index", "Dashboard");
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ForceChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        if (User.FindFirst("MustChangePassword")?.Value != "1")
            return RedirectToAction("Index", "Dashboard");

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            ViewBag.Error = "New password must be at least 8 characters.";
            return View();
        }
        if (newPassword != confirmPassword)
        {
            ViewBag.Error = "Passwords do not match.";
            return View();
        }
        if (newPassword == currentPassword)
        {
            ViewBag.Error = "New password must be different from your current password.";
            return View();
        }

        var uid = CurrentUserId();
        var (ok, error) = await userService.ChangePasswordAsync(uid, currentPassword, newPassword);
        if (!ok)
        {
            ViewBag.Error = error ?? "Password change failed.";
            return View();
        }

        // Clear MustChangePassword flag in DB and re-issue cookie without the claim.
        await userService.ClearMustChangePasswordAsync(uid);
        var user = await userService.GetByIdAsync(uid);
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name,    user!.Username),
            new(ClaimTypes.Role,    user.Role),
            new("UserId",           user.Id.ToString()),
            new("DisplayName",      user.DisplayName),
            new("ProfileImagePath", user.ProfileImagePath ?? ""),
            new("AvatarColor",      user.Color ?? "#3b82f6"),
        };
        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });

        await activity.LogAsync(user.DisplayName, "Password Changed", "Auth", "Forced change on first login",
            HttpContext.Connection.RemoteIpAddress?.ToString(), HttpContext.Request.Headers["User-Agent"].ToString());

        return RedirectToAction("AutoSelect", "Teams");
    }

    // ── Notifications (team-scoped) ───────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> NotificationCount()
    {
        var uid   = CurrentUserId();
        var count = await userService.GetNotificationCountAsync(uid, ActiveTeamId());
        return Json(new { count });
    }

    [HttpGet]
    public async Task<IActionResult> NotificationList()
    {
        var uid   = CurrentUserId();
        var items = await userService.GetNotificationsAsync(uid, ActiveTeamId());
        return PartialView("_NotificationList", items);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    {
        var uid = CurrentUserId();
        await userService.MarkNotificationReadAsync(id, uid);
        return Json(new { success = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        var uid = CurrentUserId();
        await userService.MarkAllNotificationsReadAsync(uid, ActiveTeamId());
        return Json(new { success = true });
    }

    // ── Profile ───────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var uid  = CurrentUserId();
        var user = await userService.GetByIdAsync(uid);
        if (user == null) return RedirectToAction("Login");
        ViewData["ActiveNav"] = "Profile";
        return View(user);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(string? email, bool notifyOnAssigned, bool notifyOnComment, bool notifyOnBlocked)
    {
        var uid  = CurrentUserId();
        var user = await userService.GetByIdAsync(uid);
        if (user == null) return RedirectToAction("Login");

        var req = new ProfileUpdateRequest
        {
            DisplayName = user.DisplayName,
            Color       = user.Color,
            JobTitle    = user.JobTitle,
            Department  = user.Department,
            Email       = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
        };
        await userService.UpdateProfileAsync(uid, req);
        await userService.UpdateNotificationsAsync(uid, notifyOnAssigned, notifyOnComment, notifyOnBlocked);

        await activity.LogAsync(user.DisplayName, "Notifications Updated", "System", "Notification preferences changed",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers["User-Agent"].ToString());

        TempData["Success"] = "Profile saved.";
        ViewData["ActiveNav"] = "Profile";
        var updated = await userService.GetByIdAsync(uid);
        return View(updated);
    }

    // ── Google reCAPTCHA v2 ───────────────────────────────────────────────────

    private void SetRecaptchaViewBag()
    {
        ViewBag.RecaptchaEnabled = config.GetValue("Recaptcha:Enabled", false);
        ViewBag.RecaptchaSiteKey = config["Recaptcha:SiteKey"] ?? "";
    }

    private async Task<bool> VerifyRecaptchaAsync(string? token)
    {
        if (!config.GetValue("Recaptcha:Enabled", false)) return true;

        var secret = config["Recaptcha:SecretKey"];
        if (string.IsNullOrWhiteSpace(secret)) return true;

        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var verifyUrl = config["Recaptcha:VerifyUrl"] ?? "https://www.google.com/recaptcha/api/siteverify";
            var client    = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"]   = secret,
                ["response"] = token,
                ["remoteip"] = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
            });

            var resp = await client.PostAsync(verifyUrl, form);
            if (!resp.IsSuccessStatusCode) return true;

            var json   = await resp.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<RecaptchaResult>(json);
            return result?.Success == true;
        }
        catch
        {
            return true;
        }
    }

    private sealed class RecaptchaResult
    {
        [JsonPropertyName("success")]      public bool      Success     { get; set; }
        [JsonPropertyName("challenge_ts")] public string?   ChallengeTs { get; set; }
        [JsonPropertyName("hostname")]     public string?   Hostname    { get; set; }
        [JsonPropertyName("error-codes")]  public string[]? ErrorCodes  { get; set; }
    }
}
