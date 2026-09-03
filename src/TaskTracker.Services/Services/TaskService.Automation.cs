using TaskTracker.Models;

namespace TaskTracker.Services;

public partial class TaskService
{
    /// <summary>
    /// Runs the team's automation rules for a task lifecycle event. Best-effort and fully isolated:
    /// the automation engine swallows and audits its own failures, and this wrapper guarantees a rule
    /// problem can never surface to the originating task command. Fires on the same event set as
    /// <see cref="PublishTaskWebhookAsync"/>.
    /// </summary>
    private async Task RunAutomationsAsync(TaskItem task, string eventType, UserCtx ctx)
    {
        try
        {
            await _automations.RunForEventAsync(task, eventType, ctx);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "RunAutomationsAsync failed for task {TaskId} ({EventType})", task.Id, eventType);
        }
    }
}
