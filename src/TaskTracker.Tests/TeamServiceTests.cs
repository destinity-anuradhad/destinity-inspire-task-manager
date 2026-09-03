using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class TeamServiceTests
{
    private static (TeamService svc, Team team) Setup(TaskTracker.Data.AppDbContext db)
    {
        var team = new Team { Name = "Core", IsActive = true };
        db.Add(team);
        db.AppUsers.Add(new AppUser { Id = 1, Username = "admin", DisplayName = "Admin", Role = "Admin" });
        db.SaveChanges();
        return (new TeamService(db), team);
    }

    [Fact]
    public async Task CreateTeamUser_ProvisionsWithForcedPasswordChange_AndAudits()
    {
        using var db = DbContextFactory.Create(nameof(CreateTeamUser_ProvisionsWithForcedPasswordChange_AndAudits));
        var (svc, team) = Setup(db);

        var (ok, error, user) = await svc.CreateTeamUserAsync(team.Id,
            new TeamUserCreateRequest {
                DisplayName = "New Person", Username = "newp", Password = "Password@123", TeamRole = "Member"
            },
            callerId: 1, isSysAdmin: true, callerRole: "Admin");

        Assert.True(ok, error);
        Assert.NotNull(user);
        Assert.True(user!.MustChangePassword);          // must set own password on first login
        Assert.Equal("Active", user.Status);
        Assert.Contains(db.ActivityLog, a => a.Action == "User Provisioned" && a.TeamId == team.Id);
    }

    [Fact]
    public async Task CreateTeamUser_RejectsDuplicateUsername()
    {
        using var db = DbContextFactory.Create(nameof(CreateTeamUser_RejectsDuplicateUsername));
        var (svc, team) = Setup(db);
        var req = new TeamUserCreateRequest {
            DisplayName = "A", Username = "dupe", Password = "Password@123", TeamRole = "Member"
        };

        Assert.True((await svc.CreateTeamUserAsync(team.Id, req, 1, true, "Admin")).Success);
        var second = await svc.CreateTeamUserAsync(team.Id, req, 1, true, "Admin");
        Assert.False(second.Success);
        Assert.Contains("taken", second.Error);
    }
}
