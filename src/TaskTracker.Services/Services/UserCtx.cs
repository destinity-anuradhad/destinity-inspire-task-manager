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
    public bool IsTeamLead  => IsTeamAdmin || TeamRole is "TeamLead";
    public bool IsMember    => !IsAdmin && TeamRole is not ("TeamAdmin" or "TeamLead");
}
