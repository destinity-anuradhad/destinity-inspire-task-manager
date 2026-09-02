namespace TaskTracker.Services;

/// <summary>
/// The domain events that can be delivered to webhook subscribers. A subscription stores a
/// space-separated subset of these (or <c>*</c> for all).
/// </summary>
public static class WebhookEvents
{
    public const string TaskCreated       = "task.created";
    public const string TaskStatusChanged = "task.status_changed";
    public const string TaskAssigned      = "task.assigned";
    public const string TaskCommentAdded  = "task.comment_added";

    public const string Wildcard = "*";

    /// <summary>All deliverable event types, in canonical display order.</summary>
    public static readonly IReadOnlyList<string> All =
        [TaskCreated, TaskStatusChanged, TaskAssigned, TaskCommentAdded];

    private static readonly char[] Separators = [' ', ',', ';', '\t', '\n', '\r'];

    public static IReadOnlyList<string> Parse(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(s => s.ToLowerInvariant())
                 .Distinct()
                 .ToArray();

    /// <summary>Keeps recognised events (or <c>*</c>) and returns them as a canonical space-separated string.</summary>
    public static string Normalize(IEnumerable<string> requested)
    {
        var set = requested.Select(s => (s ?? "").Trim().ToLowerInvariant()).ToHashSet();
        if (set.Contains(Wildcard)) return Wildcard;
        return string.Join(' ', All.Where(set.Contains));
    }

    /// <summary>Whether a subscription storing <paramref name="subscribed"/> should receive <paramref name="eventType"/>.</summary>
    public static bool Matches(string? subscribed, string eventType)
    {
        var set = Parse(subscribed);
        return set.Contains(Wildcard) || set.Contains(eventType.ToLowerInvariant());
    }
}
