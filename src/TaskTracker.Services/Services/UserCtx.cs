namespace TaskTracker.Services;

public readonly record struct UserCtx(
    int     UserId,
    string  DisplayName,
    string  Username,
    bool    IsAdmin,
    string  TeamRole,
    int?    TeamId,
    string? IpAddress = null,
    string? UserAgent = null
)
{
    public bool IsTeamAdmin => IsAdmin || TeamRole is "TeamAdmin";
    public bool IsLead      => TeamRole is "TeamAdmin" or "Lead";
    public bool IsMember    => !IsAdmin && TeamRole is not ("TeamAdmin" or "Lead");
}
