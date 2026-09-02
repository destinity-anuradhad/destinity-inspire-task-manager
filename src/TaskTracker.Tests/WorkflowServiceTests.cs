using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class WorkflowServiceTests
{
    [Fact]
    public async Task PublishAsync_RejectsWorkflowWithoutExactlyOneInitialState()
    {
        using var db = DbContextFactory.Create(nameof(PublishAsync_RejectsWorkflowWithoutExactlyOneInitialState));
        var svc = new WorkflowService(db);

        var result = await svc.PublishAsync(new WorkflowDraft {
            TeamId = 1,
            ItemType = "Task",
            States = [new WorkflowStateDraft { Key = "done", Name = "Done", IsTerminal = true }]
        }, AdminContext());

        Assert.False(result.Success);
        Assert.Contains("initial", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.WorkflowDefinitions);
    }

    [Fact]
    public async Task PublishedWorkflow_ControlsTransitionAndPinsTaskVersion()
    {
        using var db = DbContextFactory.Create(nameof(PublishedWorkflow_ControlsTransitionAndPinsTaskVersion));
        var workflow = new WorkflowService(db);
        var published = await workflow.PublishAsync(new WorkflowDraft {
            TeamId = 1,
            ItemType = "Task",
            Name = "Delivery",
            States = [
                new WorkflowStateDraft { Key = "queued", Name = "Queued", Category = "NotStarted", IsInitial = true, SortOrder = 10 },
                new WorkflowStateDraft { Key = "doing", Name = "Doing", Category = "InProgress", RequiresOwner = true, SortOrder = 20 },
                new WorkflowStateDraft { Key = "complete", Name = "Complete", Category = "Done", IsTerminal = true, RequiresOwner = true, RequiresActualHours = true, SortOrder = 30 },
            ],
            Transitions = [
                new WorkflowTransitionDraft { FromStateKey = "queued", ToStateKey = "doing" },
                new WorkflowTransitionDraft { FromStateKey = "doing", ToStateKey = "complete" },
            ]
        }, AdminContext());
        Assert.True(published.Success, published.Error);

        var task = new TaskItem {
            Goal = "Custom workflow task", ItemType = "Task", Status = "Queued", Owner = "Priya",
            TeamId = 1, CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var tasks = ServiceFactory.CreateTaskService(db);
        var result = await tasks.UpdateStatusAsync(task.Id, "Doing", null, AdminContext(), forceClose: false);

        Assert.True(result.Success, result.Error);
        var updated = await db.Tasks.FindAsync(task.Id);
        Assert.Equal("Doing", updated!.Status);
        Assert.Equal(published.WorkflowDefinitionId, updated.WorkflowDefinitionId);
        Assert.NotNull(updated.ActualStartDate);
    }

    private static UserCtx AdminContext() => new(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);
}
