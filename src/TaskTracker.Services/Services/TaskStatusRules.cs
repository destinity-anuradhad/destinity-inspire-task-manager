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
        status is "Active" or "In Progress" or "Development Done" or "Resolved" or "Done" or "Closed";

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
    // Members are restricted to these forward/backward paths.
    // TeamLead and above bypass all transition guards.

    private static readonly Dictionary<string, HashSet<string>> _taskTransitions = new() {
        ["To Do"]            = ["In Progress", "Blocked"],
        ["In Progress"]      = ["Development Done", "Blocked", "To Do"],
        ["Development Done"] = ["Done", "In Progress", "Blocked"],
        ["Done"]             = ["In Progress", "To Do"],
        ["Blocked"]          = ["To Do", "In Progress", "Development Done"],
    };

    private static readonly Dictionary<string, HashSet<string>> _bugTransitions = new() {
        ["New"]      = ["Active", "Resolved", "Blocked"],
        ["Active"]   = ["Resolved", "Blocked", "New"],
        ["Resolved"] = ["Closed", "Active", "New"],
        ["Closed"]   = ["Active", "New"],
        ["Blocked"]  = ["New", "Active", "Resolved"],
    };

    private static readonly Dictionary<string, HashSet<string>> _storyTransitions = new() {
        ["New"]      = ["Active", "Blocked"],
        ["Active"]   = ["Resolved", "Blocked", "New"],
        ["Resolved"] = ["Closed", "Active", "New"],
        ["Closed"]   = ["Active", "New"],
        ["Blocked"]  = ["New", "Active", "Resolved"],
    };

    private static readonly Dictionary<string, HashSet<string>> _epicTransitions = new() {
        ["New"]    = ["Active"],
        ["Active"] = ["Closed", "New"],
        ["Closed"] = ["Active", "New"],
    };

    private static readonly HashSet<string> _emptySet = [];

    public static IReadOnlySet<string> AllowedTransitions(string? itemType, string? fromStatus)
    {
        if (string.IsNullOrEmpty(fromStatus)) return _emptySet;
        var map = itemType switch {
            "Task"    => _taskTransitions,
            "Bug"     => _bugTransitions,
            "Story"   => _storyTransitions,
            "Feature" => _storyTransitions,
            "Epic"    => _epicTransitions,
            _         => _storyTransitions,
        };
        return map.TryGetValue(fromStatus, out var set) ? set : _emptySet;
    }

    public static bool IsTransitionAllowed(string? itemType, string? fromStatus, string? toStatus) =>
        fromStatus == toStatus
        || AllowedTransitions(itemType, fromStatus).Contains(toStatus ?? "");
}
