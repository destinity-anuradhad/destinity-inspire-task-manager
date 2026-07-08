using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskTracker.Models;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

public abstract class AppControllerBase : Controller
{
    protected int?   ActiveTeamId()       => int.TryParse(User.FindFirst("ActiveTeamId")?.Value,  out var id)  ? id  : (int?)null;
    protected bool   IsAdmin()            => User.IsInRole("Admin");
    protected bool   IsSysAdmin()         => User.IsInRole("Admin");
    protected int    CurrentUserId()      => int.TryParse(User.FindFirst("UserId")?.Value,         out var id)  ? id  : 0;
    protected string CurrentUsername()    => User.Identity?.Name ?? "";
    protected string CurrentDisplayName() => User.FindFirst("DisplayName")?.Value ?? User.Identity?.Name ?? "";
    protected string CurrentTeamRole()    => User.FindFirst("TeamRole")?.Value ?? "";
    protected string ActiveTeamName()     => User.FindFirst("ActiveTeamName")?.Value ?? "";

    protected UserCtx BuildUserCtx() => new(
        CurrentUserId(),
        CurrentDisplayName(),
        CurrentUsername(),
        IsAdmin(),
        CurrentTeamRole(),
        ActiveTeamId(),
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        HttpContext.Request.Headers["User-Agent"].ToString()
    );
}
