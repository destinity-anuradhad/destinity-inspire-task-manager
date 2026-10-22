using TaskTracker.Models;

namespace TaskTracker.Services;

public partial class TaskService
{
    // ── Auto-posted activity cards to the team chat ──────────────────────────────
    // These are best-effort: PostEventAsync swallows its own errors, so a chat hiccup
    // can never fail the task operation that triggered it.

    // "Task assigned" — @mentions the newly-added owners with the task id, name, priority & due date.
    private async Task PostAssignmentChatAsync(TaskItem task, IEnumerable<AppUser> newOwners, UserCtx ctx)
    {
        if (!task.TeamId.HasValue) return;
        var names = newOwners
            .Select(u => u.DisplayName)
            .Where(n => !string.IsNullOrWhiteSpace(n)
                     && !string.Equals(n, ctx.DisplayName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0) return;   // self-assignment (or no real change) — nothing to announce

        var mentions = string.Join(", ", names.Select(n => "@" + n));
        var chips = new List<string>();
        if (!string.IsNullOrWhiteSpace(task.Priority)) chips.Add($"Priority: {task.Priority}");
        if (task.TargetDate.HasValue) chips.Add($"Due: {task.TargetDate:yyyy-MM-dd}");
        var body = $"Assigned #{task.Id} “{task.Goal}” to {mentions}"
                 + (chips.Count > 0 ? "\n" + string.Join(" · ", chips) : "");
        await _chat.PostEventAsync(task.TeamId.Value, ctx.UserId > 0 ? ctx.UserId : null, ctx.DisplayName, body,
            new { icon = "📌", type = "assign", url = $"/Tasks?highlight={task.Id}", link = "Open task" });
    }

    // "Completed" / "Blocked" — posted only for those two milestone statuses (not every transition).
    private async Task PostStatusChatAsync(TaskItem task, string? oldStatus, string? newStatus, UserCtx ctx)
    {
        if (!task.TeamId.HasValue || string.Equals(oldStatus, newStatus, StringComparison.Ordinal)) return;
        string? icon = null, verb = null;
        if (string.Equals(newStatus, "Blocked", StringComparison.OrdinalIgnoreCase)) { icon = "🚫"; verb = "Marked as blocked"; }
        else if (TaskStatusRules.IsDone(task.ItemType, newStatus ?? "")) { icon = "✅"; verb = "Completed"; }
        if (icon == null) return;
        var body = $"{verb}: #{task.Id} “{task.Goal}”";
        await _chat.PostEventAsync(task.TeamId.Value, ctx.UserId > 0 ? ctx.UserId : null, ctx.DisplayName, body,
            new { icon, type = "status", url = $"/Tasks?highlight={task.Id}", link = "Open task" });
    }
}
