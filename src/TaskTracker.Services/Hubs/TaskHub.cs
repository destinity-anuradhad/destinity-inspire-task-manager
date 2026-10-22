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

    // Transient "user is typing" ping to the rest of the team (chat). Nothing is persisted.
    public async Task ChatTyping()
    {
        var teamId = Context.User?.FindFirst("ActiveTeamId")?.Value;
        var name   = Context.User?.FindFirst("DisplayName")?.Value ?? "Someone";
        if (!string.IsNullOrEmpty(teamId))
            await Clients.OthersInGroup("team-" + teamId).SendAsync("ChatTyping", name);
    }

    public override async Task OnConnectedAsync()
    {
        // Join team group (task updates) and user group (notification badge pushes).
        var teamId = Context.User?.FindFirst("ActiveTeamId")?.Value;
        var userId = Context.User?.FindFirst("UserId")?.Value;
        if (!string.IsNullOrEmpty(teamId))
            await Groups.AddToGroupAsync(Context.ConnectionId, "team-" + teamId);
        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, "user-" + userId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var teamId = Context.User?.FindFirst("ActiveTeamId")?.Value;
        var userId = Context.User?.FindFirst("UserId")?.Value;
        if (!string.IsNullOrEmpty(teamId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "team-" + teamId);
        if (!string.IsNullOrEmpty(userId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "user-" + userId);
        await base.OnDisconnectedAsync(exception);
    }
}
