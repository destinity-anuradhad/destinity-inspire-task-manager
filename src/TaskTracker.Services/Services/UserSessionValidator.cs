using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;

namespace TaskTracker.Services;

public static class AuthClaims
{
    public const string UserId = "UserId";
    public const string SessionVersion = "SessionVersion";
    public const string ActiveTeamId = "ActiveTeamId";
    public const string TeamRole = "TeamRole";
}

public interface IUserSessionValidator
{
    Task<bool> IsValidAsync(ClaimsPrincipal principal);
}

public sealed class UserSessionValidator(AppDbContext db) : IUserSessionValidator
{
    public async Task<bool> IsValidAsync(ClaimsPrincipal principal)
    {
        var userIdClaim = principal.FindFirst(AuthClaims.UserId)?.Value;
        var sessionVersionClaim = principal.FindFirst(AuthClaims.SessionVersion)?.Value;
        if (!int.TryParse(userIdClaim, out var userId)
            || !int.TryParse(sessionVersionClaim, out var sessionVersion))
        {
            return false;
        }

        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null
            || !string.Equals(user.Status, "Active", StringComparison.Ordinal)
            || user.SessionVersion != sessionVersion
            || !string.Equals(user.Role, principal.FindFirst(ClaimTypes.Role)?.Value, StringComparison.Ordinal))
        {
            return false;
        }

        var teamIdClaim = principal.FindFirst(AuthClaims.ActiveTeamId)?.Value;
        if (!int.TryParse(teamIdClaim, out var teamId)) return true;

        if (user.Role == "Admin")
            return await db.Teams.AsNoTracking().AnyAsync(t => t.Id == teamId && t.IsActive);

        var teamRole = principal.FindFirst(AuthClaims.TeamRole)?.Value;
        return await db.TeamMembers
            .Join(db.Teams,
                membership => membership.TeamId,
                team => team.Id,
                (membership, team) => new { membership, team })
            .AnyAsync(x => x.membership.UserId == userId
                && x.membership.TeamId == teamId
                && x.team.IsActive
                && x.membership.TeamRole == teamRole);
    }
}
