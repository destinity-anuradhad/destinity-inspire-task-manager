using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class RecurringTaskServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesActiveWeeklyDefinitionForTeamTemplate()
    {
        using var db = DbContextFactory.Create(nameof(CreateAsync_CreatesActiveWeeklyDefinitionForTeamTemplate));
        var template = new TaskTemplate { TeamId = 1, Name = "Weekly checks" };
        db.TaskTemplates.Add(template);
        await db.SaveChangesAsync();

        var result = await new RecurringTaskService(db).CreateAsync(new RecurringTaskDefinition {
            TaskTemplateId = template.Id, Goal = "Weekly operations checks", Frequency = "Weekly", NextRunAt = new DateTime(2026, 9, 7)
        }, new UserCtx(1, "Priya", "priya", false, "TeamAdmin", 1));

        Assert.True(result.Success, result.Error);
        var definition = Assert.Single(db.RecurringTaskDefinitions);
        Assert.True(definition.IsActive);
        Assert.Equal(1, definition.TeamId);
    }
}
