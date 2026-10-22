using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class ApiClientServiceTests
{
    private static (AppUser admin, Team team) SeedAdminTeam(TaskTracker.Data.AppDbContext db)
    {
        var admin = new AppUser { Username = "root", DisplayName = "Root", Role = "Admin", Status = "Active" };
        var team = new Team { Name = "Core", IsActive = true };
        db.AddRange(admin, team);
        db.SaveChanges();
        return (admin, team);
    }

    [Fact]
    public async Task Create_then_Authenticate_ResolvesOwnerTeamAndScopes()
    {
        using var db = DbContextFactory.Create(nameof(Create_then_Authenticate_ResolvesOwnerTeamAndScopes));
        var (admin, team) = SeedAdminTeam(db);
        var svc = new ApiClientService(db);

        var created = await svc.CreateAsync(new ApiClientCreateRequest("CI", admin.Id, team.Id, ["tasks:read", "bogus:scope"], null));
        Assert.Contains(".", created.PlaintextKey);
        Assert.Equal("tasks:read", created.Client.Scopes);   // unknown scope dropped

        var principal = await svc.AuthenticateAsync(created.PlaintextKey, "127.0.0.1");
        Assert.NotNull(principal);
        Assert.Equal(admin.Id, principal!.UserId);
        Assert.Equal(team.Id, principal.TeamId);
        Assert.Equal("TeamAdmin", principal.TeamRole);   // admin owner acts as TeamAdmin
        Assert.Contains("tasks:read", principal.Scopes);

        Assert.Null(await svc.AuthenticateAsync("tt_none.WRONGSECRET", null));   // unknown key
        Assert.Null(await svc.AuthenticateAsync(created.Client.KeyPrefix + ".WRONGSECRET", null));   // right prefix, wrong secret
    }

    [Fact]
    public async Task Authenticate_RejectsRevokedAndExpiredKeys()
    {
        using var db = DbContextFactory.Create(nameof(Authenticate_RejectsRevokedAndExpiredKeys));
        var (admin, team) = SeedAdminTeam(db);
        var svc = new ApiClientService(db);

        var live = await svc.CreateAsync(new ApiClientCreateRequest("k1", admin.Id, team.Id, ["tasks:read"], null));
        Assert.NotNull(await svc.AuthenticateAsync(live.PlaintextKey, null));
        await svc.RevokeAsync(live.Client.Id, team.Id, isSysAdmin: false);
        Assert.Null(await svc.AuthenticateAsync(live.PlaintextKey, null));

        var expired = await svc.CreateAsync(new ApiClientCreateRequest("k2", admin.Id, team.Id, ["tasks:read"], DateTime.UtcNow.AddMinutes(-1)));
        Assert.Null(await svc.AuthenticateAsync(expired.PlaintextKey, null));
    }

    [Fact]
    public async Task Authenticate_RejectsWhenOwnerNoLongerInTeam()
    {
        using var db = DbContextFactory.Create(nameof(Authenticate_RejectsWhenOwnerNoLongerInTeam));
        var member = new AppUser { Username = "dev", DisplayName = "Dev", Role = "Member", Status = "Active" };
        var team = new Team { Name = "Mobile", IsActive = true };
        db.AddRange(member, team);
        await db.SaveChangesAsync();
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, TeamRole = "Member" });
        await db.SaveChangesAsync();

        var svc = new ApiClientService(db);
        var key = await svc.CreateAsync(new ApiClientCreateRequest("k", member.Id, team.Id, ["tasks:read"], null));

        var principal = await svc.AuthenticateAsync(key.PlaintextKey, null);
        Assert.NotNull(principal);
        Assert.Equal("Member", principal!.TeamRole);

        db.TeamMembers.RemoveRange(db.TeamMembers);
        await db.SaveChangesAsync();
        Assert.Null(await svc.AuthenticateAsync(key.PlaintextKey, null));   // membership gone → key inert
    }

    private static (AppUser member, Team team) SeedMember(TaskTracker.Data.AppDbContext db)
    {
        var member = new AppUser { Username = "dev", DisplayName = "Dev", Role = "Member", Status = "Active" };
        var team = new Team { Name = "Inspire", IsActive = true };
        db.AddRange(member, team);
        db.SaveChanges();
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, TeamRole = "Member" });
        db.SaveChanges();
        return (member, team);
    }

    [Fact]
    public async Task CreatePersonal_MemberGetsAnExpiringOwnTasksOnlyKeyThatActsAsThem()
    {
        using var db = DbContextFactory.Create(nameof(CreatePersonal_MemberGetsAnExpiringOwnTasksOnlyKeyThatActsAsThem));
        var (member, team) = SeedMember(db);
        var svc = new ApiClientService(db);

        var created = await svc.CreatePersonalAsync(new PersonalKeyRequest("WorkHub", member.Id, false, team.Id, ["tasks:read"], 90, OwnTasksOnly: true));

        Assert.True(created.Client.OwnTasksOnly);
        Assert.InRange(created.Client.ExpiresAt!.Value, DateTime.UtcNow.AddDays(89), DateTime.UtcNow.AddDays(91));
        var principal = await svc.AuthenticateAsync(created.PlaintextKey, null);
        Assert.NotNull(principal);
        Assert.Equal((member.Id, team.Id, true), (principal!.UserId, principal.TeamId, principal.OwnTasksOnly));
        Assert.Equal(created.Client.Id, Assert.Single(await svc.ListForOwnerAsync(member.Id)).Id);
    }

    [Fact]
    public async Task CreatePersonal_EnforcesExpiryTeamAndKeyLimits()
    {
        using var db = DbContextFactory.Create(nameof(CreatePersonal_EnforcesExpiryTeamAndKeyLimits));
        var (member, team) = SeedMember(db);
        var svc = new ApiClientService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreatePersonalAsync(new PersonalKeyRequest("k", member.Id, false, team.Id, ["tasks:read"], 0, true)));   // no "never expires"
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreatePersonalAsync(new PersonalKeyRequest("k", member.Id, false, team.Id, ["tasks:read"], PersonalKeyRules.MaxDays + 1, true)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreatePersonalAsync(new PersonalKeyRequest("k", member.Id, false, null, ["tasks:read"], 30, true)));     // members need a team

        for (var i = 0; i < PersonalKeyRules.MaxActiveKeys; i++)
            await svc.CreatePersonalAsync(new PersonalKeyRequest($"k{i}", member.Id, false, team.Id, ["tasks:read"], 30, true));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreatePersonalAsync(new PersonalKeyRequest("one too many", member.Id, false, team.Id, ["tasks:read"], 30, true)));
    }

    [Fact]
    public async Task RevokeOwn_RevokesOnlyTheCallersOwnKeys()
    {
        using var db = DbContextFactory.Create(nameof(RevokeOwn_RevokesOnlyTheCallersOwnKeys));
        var (member, team) = SeedMember(db);
        var other = new AppUser { Username = "other", DisplayName = "Other", Role = "Member", Status = "Active" };
        db.Add(other);
        await db.SaveChangesAsync();
        var svc = new ApiClientService(db);
        var key = await svc.CreatePersonalAsync(new PersonalKeyRequest("k", member.Id, false, team.Id, ["tasks:read"], 30, true));

        Assert.False(await svc.RevokeOwnAsync(key.Client.Id, other.Id));
        Assert.NotNull(await svc.AuthenticateAsync(key.PlaintextKey, null));
        Assert.True(await svc.RevokeOwnAsync(key.Client.Id, member.Id));
        Assert.Null(await svc.AuthenticateAsync(key.PlaintextKey, null));
    }
}
