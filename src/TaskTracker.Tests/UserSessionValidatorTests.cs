using System.Security.Claims;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class UserSessionValidatorTests
{
    [Fact]
    public async Task IsValidAsync_RejectsInactiveAndRevokedSessions()
    {
        using var db = DbContextFactory.Create(nameof(IsValidAsync_RejectsInactiveAndRevokedSessions));
        var user = new AppUser { Username = "priya", DisplayName = "Priya", Status = "Active", SessionVersion = 3 };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        var validator = new UserSessionValidator(db);
        Assert.True(await validator.IsValidAsync(PrincipalFor(user, 3)));

        user.SessionVersion++;
        await db.SaveChangesAsync();
        Assert.False(await validator.IsValidAsync(PrincipalFor(user, 3)));

        user.Status = "Rejected";
        await db.SaveChangesAsync();
        Assert.False(await validator.IsValidAsync(PrincipalFor(user, user.SessionVersion)));
    }

    [Fact]
    public async Task IsValidAsync_RejectsStaleTeamMembershipAndRole()
    {
        using var db = DbContextFactory.Create(nameof(IsValidAsync_RejectsStaleTeamMembershipAndRole));
        var user = new AppUser { Username = "kasun", DisplayName = "Kasun", Status = "Active", SessionVersion = 1 };
        var team = new Team { Name = "Delivery", IsActive = true };
        db.AddRange(user, team);
        await db.SaveChangesAsync();
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = user.Id, TeamRole = "Member" });
        await db.SaveChangesAsync();

        var validator = new UserSessionValidator(db);
        Assert.True(await validator.IsValidAsync(PrincipalFor(user, 1, team.Id, "Member")));
        Assert.False(await validator.IsValidAsync(PrincipalFor(user, 1, team.Id, "TeamLead")));

        db.TeamMembers.RemoveRange(db.TeamMembers);
        await db.SaveChangesAsync();
        Assert.False(await validator.IsValidAsync(PrincipalFor(user, 1, team.Id, "Member")));
    }

    private static ClaimsPrincipal PrincipalFor(AppUser user, int sessionVersion, int? teamId = null, string? teamRole = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new(AuthClaims.UserId, user.Id.ToString()),
            new(AuthClaims.SessionVersion, sessionVersion.ToString()),
        };
        if (teamId.HasValue)
        {
            claims.Add(new Claim(AuthClaims.ActiveTeamId, teamId.Value.ToString()));
            claims.Add(new Claim(AuthClaims.TeamRole, teamRole ?? "Member"));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
