using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TaskTracker.Hubs;

[Authorize]
public class TaskHub : Hub
{
    // Called by a client to subscribe to live updates for a specific task (comments, links)
    public async Task JoinTask(string taskId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, "task-" + taskId);

    public async Task LeaveTask(string taskId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "task-" + taskId);
}
