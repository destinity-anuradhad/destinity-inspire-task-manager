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

    public override async Task OnConnectedAsync()
    {
        // Join the team group so task updates are scoped to the active team only.
        var teamId = Context.User?.FindFirst("ActiveTeamId")?.Value;
        if (!string.IsNullOrEmpty(teamId))
            await Groups.AddToGroupAsync(Context.ConnectionId, "team-" + teamId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var teamId = Context.User?.FindFirst("ActiveTeamId")?.Value;
        if (!string.IsNullOrEmpty(teamId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "team-" + teamId);
        await base.OnDisconnectedAsync(exception);
    }
}
