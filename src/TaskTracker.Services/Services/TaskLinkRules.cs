using TaskTracker.Models;

namespace TaskTracker.Services;

/// <summary>
/// The semantics of task link types — the single source of truth for which links express a blocking
/// dependency and in which direction. Shared by the task command (cycle detection, completion gating)
/// and the portfolio/dependency views.
/// </summary>
public static class TaskLinkRules
{
    public static readonly IReadOnlySet<string> SupportedLinkTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Related", "Blocks", "Blocked By", "Duplicate", "Predecessor", "Successor",
    };

    /// <summary>
    /// If the link expresses a blocking dependency, yields the blocker (must finish first) and the
    /// blocked task and returns true; otherwise returns false. A <c>Blocks</c>/<c>Predecessor</c> runs
    /// From→To; a <c>Blocked By</c>/<c>Successor</c> runs To→From.
    /// </summary>
    public static bool TryGetBlockingDirection(TaskLink link, out int blockerId, out int blockedId)
    {
        switch (link.LinkType)
        {
            case "Blocks":
            case "Predecessor":
                blockerId = link.FromTaskId;
                blockedId = link.ToTaskId;
                return true;
            case "Blocked By":
            case "Successor":
                blockerId = link.ToTaskId;
                blockedId = link.FromTaskId;
                return true;
            default:
                blockerId = blockedId = 0;
                return false;
        }
    }
}
