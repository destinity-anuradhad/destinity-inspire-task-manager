using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
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

    // If a non-admin user's cookie has no ActiveTeamId (stale cookie from before they
    // were added to a team, or pre-team-feature cookie), send them back through AutoSelect
    // so their cookie gets re-issued with the correct team claims.
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        base.OnActionExecuting(context);

        if (!User.Identity?.IsAuthenticated == true) return;
        if (IsAdmin()) return;
        if (ActiveTeamId().HasValue) return;

        // Skip the controllers that are part of the auth / team-selection flow.
        var controller = (context.RouteData.Values["controller"] as string ?? "").ToLower();
        if (controller is "account" or "teams") return;

        context.Result = RedirectToAction("AutoSelect", "Teams");
    }
}
