using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class TaskTemplateServiceTests
{
    [Fact]
    public async Task SaveAsync_CreatesTeamScopedTemplateWithChecklistSeeds()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_CreatesTeamScopedTemplateWithChecklistSeeds));
        var service = new TaskTemplateService(db);
        var result = await service.SaveAsync(new TaskTemplate {
            Name = "Deployment", ItemType = "Task", Estimate = 4, EstimateUnit = "Hours", Labels = "release"
        }, ["Take backup", "Run smoke tests"], new UserCtx(1, "Priya", "priya", false, "TeamAdmin", 1));

        Assert.True(result.Success, result.Error);
        var template = Assert.Single(db.TaskTemplates);
        Assert.Equal(1, template.TeamId);
        var checklist = await service.GetChecklistAsync(template.Id, 1);
        Assert.Equal(["Take backup", "Run smoke tests"], checklist.Select(i => i.Text));
    }

    [Fact]
    public async Task SaveAsync_RejectsDuplicateNameWithinTeam()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_RejectsDuplicateNameWithinTeam));
        db.TaskTemplates.Add(new TaskTemplate { Name = "Deployment", TeamId = 1 });
        await db.SaveChangesAsync();

        var result = await new TaskTemplateService(db).SaveAsync(new TaskTemplate { Name = "Deployment" }, [],
            new UserCtx(1, "Priya", "priya", false, "TeamAdmin", 1));

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
