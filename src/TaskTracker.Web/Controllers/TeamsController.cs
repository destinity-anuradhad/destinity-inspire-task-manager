using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskTracker.Models;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class TeamsController(ITeamService teams) : AppControllerBase
{

    public async Task<IActionResult> AutoSelect()
    {
        var uid = CurrentUserId();
        var memberships = await teams.GetUserMembershipsAsync(uid);

        if (!IsSysAdmin() && memberships.Count == 0)
            return RedirectToAction("NoTeam");

        if (memberships.Count == 1 && !IsSysAdmin())
        {
            await SignInWithTeamAsync(memberships[0].Team, memberships[0].TeamRole);
            return RedirectToAction("Index", "Dashboard");
        }

        var allTeams     = IsSysAdmin() ? await teams.GetAllActiveTeamsAsync() : new List<Team>();
        var memberCounts = await teams.GetMemberCountsAsync();

        ViewBag.Memberships  = memberships;
        ViewBag.AllTeams     = allTeams;
        ViewBag.MemberCounts = memberCounts;
        ViewBag.IsSysAdmin   = IsSysAdmin();
        return View("Select");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(int teamId, string? returnUrl = null)
    {
        var (team, teamRole) = await teams.GetSwitchDataAsync(teamId, CurrentUserId(), IsSysAdmin());
        if (team == null) return IsSysAdmin() ? NotFound() : Forbid();
        await SignInWithTeamAsync(team, teamRole);
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> SwitchGlobal(string? returnUrl = null)
    {
        await SignInGlobalAsync();
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public async Task<IActionResult> MyTeamsJson()
    {
        var uid = CurrentUserId();
        var memberships = await teams.GetUserMembershipsAsync(uid);
        var allTeams    = IsSysAdmin() ? await teams.GetAllActiveTeamsAsync() : new List<Team>();
        var shown       = memberships.Select(m => m.Team.Id).ToHashSet();

        var list = memberships
            .Select(m => new { id = m.Team.Id, name = m.Team.Name, color = m.Team.Color, role = m.TeamRole })
            .ToList<object>();

        if (IsSysAdmin())
            list.AddRange(allTeams.Where(t => !shown.Contains(t.Id))
                .Select(t => new { id = t.Id, name = t.Name, color = t.Color, role = "TeamAdmin" }));

        return Json(new { isSysAdmin = IsSysAdmin(), teams = list });
    }

    [AllowAnonymous]
    public IActionResult NoTeam() => View();

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Index()
    {
        var data = await teams.GetIndexDataAsync();
        ViewBag.Stats         = data.Stats;
        ViewData["ActiveNav"] = "Teams";
        return View(data.Teams);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(string name, string? description, string? color)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Team name is required.";
            return RedirectToAction("Index");
        }
        var team = await teams.CreateTeamAsync(name, description, color, User.FindFirst("DisplayName")?.Value);
        TempData["ProfileSuccess"] = $"Team \"{team!.Name}\" created.";
        return RedirectToAction("Manage", new { id = team.Id });
    }

    public async Task<IActionResult> Manage(int id)
    {
        if (id <= 0) return RedirectToAction(IsSysAdmin() ? "Index" : "AutoSelect");
        var data = await teams.GetManageDataAsync(id, CurrentUserId(), IsSysAdmin());
        if (data == null) return RedirectToAction(IsSysAdmin() ? "Index" : "AutoSelect");

        ViewBag.Members        = data.Members;
        ViewBag.NonMembers     = data.NonMembers;
        ViewBag.IsSysAdmin     = IsSysAdmin();
        ViewBag.CallerTeamRole = data.CallerTeamRole;
        ViewData["ActiveNav"]  = "Teams";
        return View(data.Team);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string name, string? description, string? color)
    {
        var (ok, error) = await teams.EditTeamAsync(id, name, description, color, CurrentUserId(), IsSysAdmin());
        if (!ok)
        {
            if (error == "Not found") return NotFound();
            if (error == "Forbidden")  return Forbid();
        }
        TempData["ProfileSuccess"] = "Team updated.";
        return RedirectToAction("Manage", new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        if (!await teams.ToggleActiveAsync(id)) return NotFound();
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMember(int teamId, int userId, string teamRole)
    {
        var (ok, error) = await teams.AddMemberAsync(teamId, userId, teamRole, CurrentUserId(), IsSysAdmin());
        if (!ok)
        {
            if (error == "Forbidden") return Forbid();
            return NotFound();
        }
        TempData["ProfileSuccess"] = "Member added.";
        return RedirectToAction("Manage", new { id = teamId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveMember(int teamId, int userId)
    {
        var (ok, error) = await teams.RemoveMemberAsync(teamId, userId, CurrentUserId(), IsSysAdmin());
        if (!ok)
        {
            if (error == "Forbidden") return Forbid();
            TempData["Error"] = error;
        }
        else TempData["ProfileSuccess"] = "Member removed.";
        return RedirectToAction("Manage", new { id = teamId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(int teamId, string displayName, string username,
        string? userEmail, string password, string teamRole)
    {
        var callerRole = User.FindFirst("TeamRole")?.Value;
        var req = new TeamUserCreateRequest
        {
            DisplayName = displayName,
            Username    = username,
            Email       = userEmail,
            Password    = password,
            TeamRole    = teamRole,
        };
        var (ok, error, user) = await teams.CreateTeamUserAsync(teamId, req, CurrentUserId(), IsSysAdmin(), callerRole);
        if (!ok)
        {
            if (error == "Forbidden")       return Forbid();
            if (error == "Team not found")  return NotFound();
            TempData["Error"] = error;
            return RedirectToAction("Manage", new { id = teamId });
        }
        TempData["ProfileSuccess"] = $"User '{user!.DisplayName}' created and added to the team.";
        return RedirectToAction("Manage", new { id = teamId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRole(int teamId, int userId, string teamRole)
    {
        var (ok, error) = await teams.UpdateRoleAsync(teamId, userId, teamRole, CurrentUserId(), IsSysAdmin());
        if (!ok)
        {
            if (error == "Forbidden") return Forbid();
            TempData["Error"] = error;
        }
        return RedirectToAction("Manage", new { id = teamId });
    }

    // ── Re-sign helpers ───────────────────────────────────────────────────────

    private async Task SignInWithTeamAsync(Team team, string teamRole)
    {
        var user = await teams.GetUserForAuthAsync(CurrentUserId());
        if (user == null) return;
        var claims = BuildClaims(user);
        claims.Add(new Claim(AuthClaims.ActiveTeamId, team.Id.ToString()));
        claims.Add(new Claim("ActiveTeamName",  team.Name));
        claims.Add(new Claim("ActiveTeamColor", team.Color));
        claims.Add(new Claim(AuthClaims.TeamRole, teamRole));
        await SignInAsync(claims);
    }

    private async Task SignInGlobalAsync()
    {
        var user = await teams.GetUserForAuthAsync(CurrentUserId());
        if (user == null) return;
        await SignInAsync(BuildClaims(user));
    }

    private static List<Claim> BuildClaims(AppUser user) =>
    [
        new(ClaimTypes.Name,    user.Username),
        new(ClaimTypes.Role,    user.Role),
        new(AuthClaims.UserId,   user.Id.ToString()),
        new(AuthClaims.SessionVersion, user.SessionVersion.ToString()),
        new("DisplayName",      user.DisplayName),
        new("ProfileImagePath", user.ProfileImagePath ?? ""),
        new("AvatarColor",      user.Color ?? "#3b82f6"),
    ];

    private async Task SignInAsync(List<Claim> claims)
    {
        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true });
    }
}
