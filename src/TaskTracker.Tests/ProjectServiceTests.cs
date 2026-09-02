using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class ProjectServiceTests
{
    [Fact]
    public async Task SaveAsync_CreatesTeamScopedProjectWithValidLead()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_CreatesTeamScopedProjectWithValidLead));
        var lead = new AppUser { Username = "priya", DisplayName = "Priya" };
        db.AppUsers.Add(lead);
        await db.SaveChangesAsync();
        db.TeamMembers.Add(new TeamMember { TeamId = 1, UserId = lead.Id, TeamRole = "TeamAdmin" });
        await db.SaveChangesAsync();

        var result = await new ProjectService(db).SaveAsync(new ProjectItem {
            Name = "Payments", LeadUserId = lead.Id, Description = "  Payment delivery  "
        }, new UserCtx(lead.Id, "Priya", "priya", false, "TeamAdmin", 1));

        Assert.True(result.Success, result.Error);
        var project = Assert.Single(db.Projects);
        Assert.Equal(1, project.TeamId);
        Assert.Equal("Active", project.Status);
        Assert.Equal("Payment delivery", project.Description);
    }

    [Fact]
    public async Task SaveAsync_RejectsLeadOutsideActiveTeam()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_RejectsLeadOutsideActiveTeam));
        var otherUser = new AppUser { Username = "kasun", DisplayName = "Kasun" };
        db.AppUsers.Add(otherUser);
        await db.SaveChangesAsync();

        var result = await new ProjectService(db).SaveAsync(new ProjectItem { Name = "Payments", LeadUserId = otherUser.Id },
            new UserCtx(1, "Priya", "priya", false, "TeamAdmin", 1));

        Assert.False(result.Success);
        Assert.Contains("active team", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArchiveAsync_OnlyArchivesProjectInCallerTeam()
    {
        using var db = DbContextFactory.Create(nameof(ArchiveAsync_OnlyArchivesProjectInCallerTeam));
        var project = new ProjectItem { Name = "Other team", TeamId = 2 };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var result = await new ProjectService(db).ArchiveAsync(project.Id,
            new UserCtx(1, "Priya", "priya", false, "TeamAdmin", 1));

        Assert.False(result.Success);
        Assert.Equal("Active", (await db.Projects.FindAsync(project.Id))!.Status);
    }

    [Fact]
    public async Task ReleaseLifecycle_CreatesAndMarksReleaseWithTimestamp()
    {
        using var db = DbContextFactory.Create(nameof(ReleaseLifecycle_CreatesAndMarksReleaseWithTimestamp));
        var project = new ProjectItem { Name = "Payments", TeamId = 1, Status = "Active" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var service = new ProjectService(db);
        var ctx = new UserCtx(1, "Priya", "priya", false, "TeamAdmin", 1);

        var created = await service.CreateReleaseAsync(new ProjectRelease {
            ProjectId = project.Id, Name = "2.1", TargetDate = new DateTime(2026, 10, 1)
        }, ctx);

        Assert.True(created.Success, created.Error);
        var release = Assert.Single(db.ProjectReleases);
        Assert.Equal("Planned", release.Status);
        Assert.True((await service.MarkReleasedAsync(release.Id, ctx)).Success);
        Assert.Equal("Released", release.Status);
        Assert.NotNull(release.ReleasedAt);
        Assert.False((await service.MarkReleasedAsync(release.Id, ctx)).Success);
    }
}
