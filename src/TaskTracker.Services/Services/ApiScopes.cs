namespace TaskTracker.Services;

/// <summary>
/// Coarse-grained permission scopes an <see cref="TaskTracker.Models.ApiClient"/> may hold.
/// Scopes are stored space-separated on the client row and surfaced as <c>scope</c> claims
/// when a request is authenticated with an API key. Cookie-authenticated (interactive) users
/// are not scope-limited — they retain their existing role/team authorization.
/// </summary>
public static class ApiScopes
{
    public const string ProjectsRead = "projects:read";
    public const string TasksRead    = "tasks:read";
    public const string TasksWrite   = "tasks:write";

    /// <summary>All recognised scopes, in canonical display order.</summary>
    public static readonly IReadOnlyList<string> All = [ProjectsRead, TasksRead, TasksWrite];

    private static readonly char[] Separators = [' ', ',', ';', '\t', '\n', '\r'];

    /// <summary>Splits a stored scope string into its individual scope tokens.</summary>
    public static IReadOnlyList<string> Parse(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(s => s.ToLowerInvariant())
                 .Distinct()
                 .ToArray();

    /// <summary>
    /// Keeps only the recognised scopes from <paramref name="requested"/> and returns them as a
    /// canonical, space-separated string suitable for persistence. Unknown scopes are dropped.
    /// </summary>
    public static string Normalize(IEnumerable<string> requested)
    {
        var set = requested.Select(s => (s ?? "").Trim().ToLowerInvariant()).ToHashSet();
        return string.Join(' ', All.Where(set.Contains));
    }
}
