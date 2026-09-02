using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize(Roles = "Admin")]
public class MasterController(IUserService userService) : AppControllerBase
{
    public async Task<IActionResult> Index(string tab = "users")
    {
        var data = await userService.GetMasterDataAsync();
        ViewBag.Users       = data.Users;
        ViewBag.ActiveTeams = data.ActiveTeams;
        ViewBag.UserTeams   = data.UserTeams;
        ViewBag.WorkStates  = data.WorkStates;
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

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveWorkState(int? id, string name, string color, int? wipLimit, string? itemTypes)
    {
        var (ok, error) = await userService.SaveWorkStateAsync(id, name, color, wipLimit, itemTypes);
        if (ok) TempData["ProfileSuccess"] = id > 0 ? $"Column '{name?.Trim()}' updated." : $"Column '{name?.Trim()}' added.";
        else    TempData["Error"]           = error;
        return RedirectToAction(nameof(Index), new { tab = "columns" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteWorkState(int id)
    {
        var (ok, error) = await userService.DeleteWorkStateAsync(id);
        if (!ok) TempData["Error"] = error;
        return RedirectToAction(nameof(Index), new { tab = "columns" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReorderWorkStates([FromBody] int[] ids)
    {
        if (ids == null || ids.Length == 0) return BadRequest();
        await userService.ReorderWorkStatesAsync(ids);
        return Ok();
    }

    public async Task<IActionResult> Pending()
    {
        var users    = await userService.GetPendingUsersAsync();
        var data     = await userService.GetMasterDataAsync();
        ViewBag.TeamNames = data.ActiveTeams.ToDictionary(t => t.Id, t => t.Name);
        ViewData["ActiveNav"] = "Master";
        return View(users);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var (ok, error) = await userService.ApproveUserAsync(id);
        if (ok) TempData["ProfileSuccess"] = "User approved and activated.";
        else    TempData["Error"]           = error;
        return RedirectToAction(nameof(Pending));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id)
    {
        var (ok, error) = await userService.RejectUserAsync(id);
        if (ok) TempData["ProfileSuccess"] = "Registration request rejected.";
        else    TempData["Error"]           = error;
        return RedirectToAction(nameof(Pending));
    }
}
