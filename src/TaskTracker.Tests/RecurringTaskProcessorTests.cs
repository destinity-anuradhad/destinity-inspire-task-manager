using Microsoft.Extensions.Logging.Abstractions;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class RecurringTaskProcessorTests
{
    [Fact]
    public async Task ProcessDueAsync_CreatesTaskChecklistAndAdvancesWeeklySchedule()
    {
        using var db = DbContextFactory.Create(nameof(ProcessDueAsync_CreatesTaskChecklistAndAdvancesWeeklySchedule));
        var dueAt = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc);
        var template = new TaskTemplate { TeamId = 1, Name = "Weekly release checks", Priority = "High", Estimate = 3, EstimateUnit = "Points" };
        db.TaskTemplates.Add(template);
        await db.SaveChangesAsync();
        db.TaskTemplateChecklistItems.Add(new TaskTemplateChecklistItem { TaskTemplateId = template.Id, Text = "Confirm deployment", SortOrder = 1 });
        db.RecurringTaskDefinitions.Add(new RecurringTaskDefinition {
            TeamId = 1, TaskTemplateId = template.Id, Goal = "Perform weekly release checks", Frequency = "Weekly", NextRunAt = dueAt
        });
        await db.SaveChangesAsync();

        var processor = new RecurringTaskProcessor(db, ServiceFactory.CreateTaskService(db), NullLogger<RecurringTaskProcessor>.Instance);
        var created = await processor.ProcessDueAsync(dueAt);
        var repeated = await processor.ProcessDueAsync(dueAt);

        Assert.Equal(1, created);
        Assert.Equal(0, repeated);
        var task = Assert.Single(db.Tasks);
        Assert.Equal("Perform weekly release checks", task.Goal);
        Assert.Equal(1, task.TeamId);
        Assert.Equal("Confirm deployment", Assert.Single(db.TaskChecklistItems).Text);
        var run = Assert.Single(db.RecurringTaskRuns);
        Assert.Equal(task.Id, run.TaskId);
        var definition = Assert.Single(db.RecurringTaskDefinitions);
        Assert.Equal(dueAt.AddDays(7), definition.NextRunAt);
        Assert.Equal(dueAt, definition.LastRunAt);
    }
}
