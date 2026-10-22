using TaskTracker.Models;

namespace TaskTracker.Services;

public partial class TaskService
{
    /// <summary>
    /// Fans a task lifecycle event out to the team's integrations: webhook subscribers (durable
    /// outbox) and the automation rule engine. Best-effort — any failure here never affects the
    /// originating command.
    /// </summary>
    private async Task PublishTaskWebhookAsync(TaskItem task, string eventType, UserCtx ctx)
    {
        try
        {
            await _webhooks.PublishAsync(task.TeamId, eventType, new
            {
                id         = task.Id,
                goal       = task.Goal,
                status     = task.Status,
                priority   = task.Priority,
                owner      = task.Owner,
                itemType   = task.ItemType,
                projectId  = task.ProjectId,
                sprintId   = task.SprintId,
                parentId   = task.ParentId,
                teamId     = task.TeamId,
                targetDate = task.TargetDate,
            }, ctx.DisplayName);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PublishTaskWebhookAsync failed for task {TaskId} ({EventType})", task.Id, eventType);
        }

        // Same event set drives automation rules; kept separate so a webhook failure can't skip it.
        await RunAutomationsAsync(task, eventType, ctx);
    }
}
