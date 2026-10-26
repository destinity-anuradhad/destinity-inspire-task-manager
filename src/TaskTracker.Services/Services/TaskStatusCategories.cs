namespace TaskTracker.Services;

/// <summary>
/// Stable, coarse status categories for integrations, so a status added later (or a team workflow's
/// own state names) does not break API clients: <c>todo</c>, <c>in_progress</c>, <c>blocked</c>,
/// <c>done</c>, <c>cancelled</c>.
/// </summary>
public static class TaskStatusCategories
{
    public const string ToDo = "todo";
    public const string InProgress = "in_progress";
    public const string Blocked = "blocked";
    public const string Done = "done";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [ToDo, InProgress, Blocked, Done, Cancelled];

    /// <param name="workflowCategory">The category of the task's pinned workflow state
    /// (<see cref="TaskTracker.Models.WorkflowState.Category"/>), when it has one.</param>
    public static string For(string? itemType, string? status, string? workflowCategory = null)
    {
        // Side-states first: they mean the same thing in every workflow.
        if (string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase)) return Cancelled;
        if (string.Equals(status, "Blocked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "On hold", StringComparison.OrdinalIgnoreCase)) return Blocked;

        switch (workflowCategory?.Trim())
        {
            case "Done": return Done;
            case "InProgress": return InProgress;
            case "NotStarted": return ToDo;
        }

        if (TaskStatusRules.IsDone(itemType, status)) return Done;
        return TaskStatusRules.IsStarted(status) ? InProgress : ToDo;
    }

    public static bool IsOpen(string category) => category is not (Done or Cancelled);
}
