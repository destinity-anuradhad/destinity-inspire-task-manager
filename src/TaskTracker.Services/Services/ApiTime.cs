namespace TaskTracker.Services;

/// <summary>
/// Converts stored timestamps to unambiguous API values. The database mixes two conventions, so each
/// column is converted according to how it is written:
/// <list type="bullet">
/// <item>server-local (<c>DateTime.Now</c>): Tasks.CreatedAt, Tasks.DeletedAt, ActivityLog.ChangedAt;</item>
/// <item>UTC (<c>DateTime.UtcNow</c>): Tasks.UpdatedAt, Tasks.ActualStartDate/ActualEndDate, TimeEntries.CreatedAt, ProjectReleases.ReleasedAt;</item>
/// <item>calendar dates with no time: due/start/target dates, sprint dates, time-entry work dates.</item>
/// </list>
/// </summary>
public static class ApiTime
{
    /// <summary>A server-local timestamp, with the server's UTC offset (e.g. <c>+05:30</c>).</summary>
    public static DateTimeOffset Local(DateTime value)
    {
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
    }

    public static DateTimeOffset? Local(DateTime? value) => value is { } v ? Local(v) : null;

    /// <summary>A UTC timestamp (<c>Z</c>).</summary>
    public static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static DateTimeOffset? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    /// <summary>A calendar date (<c>yyyy-MM-dd</c>); any time part is dropped.</summary>
    public static DateOnly? Date(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;

    /// <summary>The server's time zone, as reported by <c>GET /api/v1/me</c>.</summary>
    public static string ServerTimeZone => TimeZoneInfo.Local.Id;

    public static string ServerUtcOffset
    {
        get
        {
            var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
            return (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm");
        }
    }
}
