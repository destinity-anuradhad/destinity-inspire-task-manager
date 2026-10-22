using Microsoft.AspNetCore.Authorization;
using TaskTracker.Services;

namespace TaskTracker.Web.Auth;

/// <summary>Named authorization policies for the coarse role/team gates, applied via
/// <c>[Authorize(Policy = Policies.X)]</c>. Backed by <see cref="TeamAuthorization"/>.</summary>
public static class Policies
{
    public const string SystemAdmin = "SystemAdmin";   // system administrators only
    public const string TeamAdmin   = "TeamAdmin";     // team administrators (and system admins)
    public const string TeamLead    = "TeamLead";      // team leads and above

    /// <summary>Registers all named policies. Call from <c>AddAuthorization</c>.</summary>
    public static void AddAll(AuthorizationOptions options)
    {
        options.AddPolicy(SystemAdmin, p => p.Requirements.Add(new MinimumTeamRoleRequirement(TeamRoleLevel.SystemAdmin)));
        options.AddPolicy(TeamAdmin,   p => p.Requirements.Add(new MinimumTeamRoleRequirement(TeamRoleLevel.TeamAdmin)));
        options.AddPolicy(TeamLead,    p => p.Requirements.Add(new MinimumTeamRoleRequirement(TeamRoleLevel.TeamLead)));
    }
}

/// <summary>Requires the caller to hold at least <see cref="Level"/> privilege.</summary>
public sealed class MinimumTeamRoleRequirement(TeamRoleLevel level) : IAuthorizationRequirement
{
    public TeamRoleLevel Level { get; } = level;
}

public sealed class MinimumTeamRoleHandler : AuthorizationHandler<MinimumTeamRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, MinimumTeamRoleRequirement requirement)
    {
        if (TeamAuthorization.Meets(context.User, requirement.Level))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
