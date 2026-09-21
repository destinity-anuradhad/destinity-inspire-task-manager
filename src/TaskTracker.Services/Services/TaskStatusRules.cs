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
    // "Blocked". BACKWARD moves are rejected for every role. Blocked is a side-state: you can
    // block from anywhere and resume from Blocked to any status.
    private static readonly Dictionary<string, string[]> _flowOrder = new(StringComparer.OrdinalIgnoreCase) {
        ["Task"]    = ["To Do", "In Progress", "Development Done", "In QA", "Done"],
        ["Bug"]     = ["New", "Active", "Resolved", "Closed"],
        ["Story"]   = ["New", "Active", "Resolved", "Closed"],
        ["Feature"] = ["New", "Active", "Resolved", "Closed"],
        ["Epic"]    = ["New", "Active", "Closed"],   // no Blocked side-state
    };
    private const string BlockedStatus = "Blocked";

    private static string[] FlowFor(string? itemType) =>
        itemType != null && _flowOrder.TryGetValue(itemType, out var f) ? f : _flowOrder["Task"];

    // Every type except Epic carries the Blocked side-state.
    private static bool HasBlocked(string? itemType) =>
        !string.Equals(itemType, "Epic", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> _emptySet = [];

    public static IReadOnlySet<string> AllowedTransitions(string? itemType, string? fromStatus)
    {
        if (string.IsNullOrEmpty(fromStatus)) return _emptySet;
        var flow = FlowFor(itemType);
        var set  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // From Blocked: resume to any status in the flow.
        if (string.Equals(fromStatus, BlockedStatus, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var s in flow) set.Add(s);
            return set;
        }

        var idx = Array.FindIndex(flow, s => string.Equals(s, fromStatus, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            for (var i = idx + 1; i < flow.Length; i++) set.Add(flow[i]);   // forward only — never backward
        else
            foreach (var s in flow) set.Add(s);   // unknown/legacy status — offer the whole flow, don't trap

        if (HasBlocked(itemType)) set.Add(BlockedStatus);
        return set;
    }

    public static bool IsTransitionAllowed(string? itemType, string? fromStatus, string? toStatus) =>
        fromStatus == toStatus
        || AllowedTransitions(itemType, fromStatus).Contains(toStatus ?? "");
}
