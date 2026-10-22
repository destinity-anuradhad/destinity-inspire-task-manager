using System.Security.Claims;

namespace TaskTracker.Services;

/// <summary>The ordered privilege levels a caller can hold, from least to most.</summary>
public enum TeamRoleLevel
{
    Member      = 0,
    TeamLead    = 1,
    TeamAdmin   = 2,
    SystemAdmin = 3,
}

/// <summary>
/// The single source of truth for resolving a caller's coarse privilege level from their claims. Both
/// the MVC authorization policies and any imperative checks derive from this, so "who counts as a team
/// admin" is defined once and tested once. Per-resource decisions (can this user edit *this* task)
/// stay in the domain services — this only covers role/team-level gates.
/// </summary>
public static class TeamAuthorization
{
    public const string TeamRoleClaim   = "TeamRole";
    public const string SystemAdminRole = "Admin";

    public static TeamRoleLevel EffectiveLevel(ClaimsPrincipal user)
    {
        if (user.IsInRole(SystemAdminRole)) return TeamRoleLevel.SystemAdmin;
        return user.FindFirst(TeamRoleClaim)?.Value switch
        {
            "TeamAdmin" => TeamRoleLevel.TeamAdmin,
            "TeamLead"  => TeamRoleLevel.TeamLead,
            _           => TeamRoleLevel.Member,
        };
    }

    /// <summary>Whether the caller holds at least <paramref name="required"/> privilege.</summary>
    public static bool Meets(ClaimsPrincipal user, TeamRoleLevel required) =>
        user.Identity?.IsAuthenticated == true && EffectiveLevel(user) >= required;
}
