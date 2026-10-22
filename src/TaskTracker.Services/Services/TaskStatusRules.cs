using TaskTracker.Models;

namespace TaskTracker.Services;

public static class TaskStatusRules
{
    // "Development Done" is intentionally excluded from Task's done condition —
    // it is a mid-stage status (dev complete, pending QA) and must NOT count as
    // done for capacity planning. Only "Done" marks a Task as complete.
    public static bool IsDone(string? itemType, string? status) =>
        itemType switch {
            "Task"    => status == "Done",
            "Bug"     => status is "Resolved" or "Closed",
            "Story"   => status is "Resolved" or "Closed",
            "Feature" => status is "Resolved" or "Closed",
            "Epic"    => status == "Closed",
            _         => status is "Done" or "Resolved" or "Closed"
        };

    public static bool IsDone(TaskItem t)       => IsDone(t.ItemType, t.Status);
    public static bool IsExcludedFromSprintCompletion(TaskItem t) =>
        string.Equals(t.Status, "On hold", StringComparison.OrdinalIgnoreCase)
        || string.Equals(t.Status, "Rejected", StringComparison.OrdinalIgnoreCase);
    public static bool IsActive(string? status) => status is "In Progress" or "Active";
    public static bool IsActive(TaskItem t)     => IsActive(t.Status);

    // Any status past New/To-Do — triggers parent auto-advance to Active
    public static bool IsStarted(string? status) =>
        status is "Active" or "In Progress" or "Development Done" or "In QA" or "Resolved" or "Done" or "Closed";

    // Absolute final status for the item type — stamps ActualEndDate on transition
    public static bool IsTerminal(string? itemType, string? status) =>
        itemType == "Task" ? status == "Done" : status == "Closed";

    // Status a parent auto-advances to when all its children are done
    public static string? CascadeAutoStatus(string? itemType) =>
        itemType switch {
            "Story"   => "Resolved",
            "Feature" => "Closed",
            "Epic"    => "Closed",
            _         => null
        };

    // ── Transition rules ──────────────────────────────────────────────────────
    // FORWARD-ONLY flow: from any status you may advance to ANY later status in the item
    // type's order (skipping intermediate stages is allowed — e.g. "To Do" → "Done") or to
    // "Blocked", "On hold", or "Rejected". BACKWARD moves are rejected for every role. These
    // statuses are side-states: they can be set from anywhere and resume to any flow status.
    private static readonly Dictionary<string, string[]> _flowOrder = new(StringComparer.OrdinalIgnoreCase) {
        ["Task"]    = ["To Do", "In Progress", "Development Done", "In QA", "Done"],
        ["Bug"]     = ["New", "Active", "Resolved", "Closed"],
        ["Story"]   = ["New", "Active", "Resolved", "Closed"],
        ["Feature"] = ["New", "Active", "Resolved", "Closed"],
        ["Epic"]    = ["New", "Active", "Closed"],   // no Blocked side-state
    };
    private const string BlockedStatus = "Blocked";
    private static readonly string[] SideStatuses = [BlockedStatus, "On hold", "Rejected"];

    private static string[] FlowFor(string? itemType) =>
        itemType != null && _flowOrder.TryGetValue(itemType, out var f) ? f : _flowOrder["Task"];

    /// <summary>The initial (first) status a new work item of this type starts in.</summary>
    public static string InitialStatus(string? itemType) => FlowFor(itemType).FirstOrDefault() ?? "To Do";

    // Blocked is available to every type except Epic. On hold and Rejected apply to all types.
    private static bool HasBlocked(string? itemType) =>
        !string.Equals(itemType, "Epic", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> _emptySet = [];

    public static IReadOnlySet<string> AllowedTransitions(string? itemType, string? fromStatus)
    {
        if (string.IsNullOrEmpty(fromStatus)) return _emptySet;
        var flow = FlowFor(itemType);
        var set  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Resume from a side-state to any status in the type's workflow.
        if (SideStatuses.Contains(fromStatus, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var s in flow) set.Add(s);
            return set;
        }

        var idx = Array.FindIndex(flow, s => string.Equals(s, fromStatus, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            for (var i = idx + 1; i < flow.Length; i++) set.Add(flow[i]);   // forward only — never backward
        else
            foreach (var s in flow) set.Add(s);   // unknown/legacy status — offer the whole flow, don't trap

        set.Add("On hold");
        set.Add("Rejected");
        if (HasBlocked(itemType)) set.Add(BlockedStatus);
        return set;
    }

    public static bool IsTransitionAllowed(string? itemType, string? fromStatus, string? toStatus) =>
        fromStatus == toStatus
        || AllowedTransitions(itemType, fromStatus).Contains(toStatus ?? "");
}
