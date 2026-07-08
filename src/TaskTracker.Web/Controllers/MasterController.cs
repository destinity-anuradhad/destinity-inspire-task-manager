using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize(Roles = "Admin")]
public class MasterController(IUserService userService) : Controller
{
    public async Task<IActionResult> Index(string tab = "users")
    {
        var data = await userService.GetMasterDataAsync();
        ViewBag.Users       = data.Users;
        ViewBag.ActiveTeams = data.ActiveTeams;
        ViewBag.UserTeams   = data.UserTeams;
        ViewBag.ActiveTab   = tab;
        ViewData["ActiveNav"] = "Master";
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(int id, string username, string displayName, string role, string color)
    {
        var (ok, error) = await userService.EditUserAsync(id, username, displayName, role, color);
        if (ok) TempData["ProfileSuccess"] = $"User '{displayName?.Trim()}' updated.";
        else    TempData["Error"]           = error;
        return RedirectToAction(nameof(Index), new { tab = "users" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(string displayName, string username, string? userEmail,
        string password, string systemRole, int? teamId, string? teamRole, string? color)
    {
        var req = new MasterCreateUserRequest
        {
            DisplayName = displayName,
            Username    = username,
            Email       = userEmail,
            Password    = password,
            SystemRole  = systemRole,
            TeamId      = teamId,
            TeamRole    = teamRole,
            Color       = color,
        };
        var (ok, error) = await userService.CreateUserAsync(req);
        if (ok) TempData["ProfileSuccess"] = $"User '{displayName?.Trim()}' created successfully.";
        else    TempData["Error"]           = error;
        return RedirectToAction(nameof(Index), new { tab = "users" });
    }
}
