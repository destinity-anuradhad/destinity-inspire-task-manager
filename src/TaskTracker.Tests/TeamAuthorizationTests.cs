using System.Security.Claims;
using TaskTracker.Services;
using Xunit;

namespace TaskTracker.Tests;

public class TeamAuthorizationTests
{
    private static ClaimsPrincipal User(string? role = null, string? teamRole = null)
    {
        var claims = new List<Claim>();
        if (role != null) claims.Add(new Claim(ClaimTypes.Role, role));
        if (teamRole != null) claims.Add(new Claim(TeamAuthorization.TeamRoleClaim, teamRole));
        // "cookies" auth type → Identity.IsAuthenticated == true; default RoleClaimType is ClaimTypes.Role.
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "cookies"));
    }

    [Theory]
    [InlineData("Admin", null, TeamRoleLevel.SystemAdmin)]
    [InlineData(null, "TeamAdmin", TeamRoleLevel.TeamAdmin)]
    [InlineData(null, "TeamLead", TeamRoleLevel.TeamLead)]
    [InlineData(null, "Member", TeamRoleLevel.Member)]
    [InlineData(null, null, TeamRoleLevel.Member)]
    public void EffectiveLevel_MapsClaimsToLevel(string? role, string? teamRole, TeamRoleLevel expected)
        => Assert.Equal(expected, TeamAuthorization.EffectiveLevel(User(role, teamRole)));

    [Fact]
    public void SystemAdmin_SatisfiesEveryPolicy()
    {
        var admin = User(role: "Admin");
        Assert.True(TeamAuthorization.Meets(admin, TeamRoleLevel.SystemAdmin));
        Assert.True(TeamAuthorization.Meets(admin, TeamRoleLevel.TeamAdmin));
        Assert.True(TeamAuthorization.Meets(admin, TeamRoleLevel.TeamLead));
    }

    [Fact]
    public void TeamAdmin_MeetsTeamAdminAndLead_ButNotSystemAdmin()
    {
        var teamAdmin = User(teamRole: "TeamAdmin");
        Assert.True(TeamAuthorization.Meets(teamAdmin, TeamRoleLevel.TeamAdmin));
        Assert.True(TeamAuthorization.Meets(teamAdmin, TeamRoleLevel.TeamLead));
        Assert.False(TeamAuthorization.Meets(teamAdmin, TeamRoleLevel.SystemAdmin));
    }

    [Fact]
    public void TeamLead_MeetsLeadOnly()
    {
        var lead = User(teamRole: "TeamLead");
        Assert.True(TeamAuthorization.Meets(lead, TeamRoleLevel.TeamLead));
        Assert.False(TeamAuthorization.Meets(lead, TeamRoleLevel.TeamAdmin));
    }

    [Fact]
    public void Member_MeetsNothingElevated()
    {
        var member = User(teamRole: "Member");
        Assert.False(TeamAuthorization.Meets(member, TeamRoleLevel.TeamLead));
        Assert.False(TeamAuthorization.Meets(member, TeamRoleLevel.TeamAdmin));
    }

    [Fact]
    public void Unauthenticated_MeetsNothing()
    {
        var anon = new ClaimsPrincipal(new ClaimsIdentity());   // no auth type → not authenticated
        Assert.False(TeamAuthorization.Meets(anon, TeamRoleLevel.TeamLead));
        Assert.Equal(TeamRoleLevel.Member, TeamAuthorization.EffectiveLevel(anon));
    }
}
