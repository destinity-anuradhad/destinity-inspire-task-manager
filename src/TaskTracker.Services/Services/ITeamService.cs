using TaskTracker.Models;

namespace TaskTracker.Services;

public interface ITeamService
{
    Task<List<TeamPickItem>> GetUserMembershipsAsync(int userId);
    Task<List<Team>> GetAllActiveTeamsAsync();
    Task<Dictionary<int, int>> GetMemberCountsAsync();
    Task<TeamIndexData> GetIndexDataAsync();
    Task<Team?> CreateTeamAsync(string name, string? description, string? color, string? createdBy);
    Task<TeamManageData?> GetManageDataAsync(int teamId, int callerId, bool isSysAdmin);
    Task<(bool Success, string? Error)> EditTeamAsync(int teamId, string name, string? description, string? color, int callerId, bool isSysAdmin);
    Task<bool> ToggleActiveAsync(int teamId);
    Task<(bool Success, string? Error)> AddMemberAsync(int teamId, int userId, string teamRole, int callerId, bool isSysAdmin);
    Task<(bool Success, string? Error)> RemoveMemberAsync(int teamId, int userId, int callerId, bool isSysAdmin);
    Task<(bool Success, string? Error, AppUser? CreatedUser)> CreateTeamUserAsync(int teamId, TeamUserCreateRequest req, int callerId, bool isSysAdmin, string? callerRole);
    Task<(bool Success, string? Error)> UpdateRoleAsync(int teamId, int userId, string teamRole, int callerId, bool isSysAdmin);
    Task<(Team? Team, string TeamRole)> GetSwitchDataAsync(int teamId, int userId, bool isSysAdmin);
    Task<AppUser?> GetUserForAuthAsync(int userId);
    Task<string?> GetCallerTeamRoleAsync(int teamId, int userId);
}

public class TeamPickItem
{
    public Team   Team     { get; set; } = null!;
    public string TeamRole { get; set; } = "";
}

public class TeamIndexStat
{
    public Team Team        { get; set; } = null!;
    public int  Total       { get; set; }
    public int  Done        { get; set; }
    public int  Active      { get; set; }
    public int  Overdue     { get; set; }
    public int  MemberCount { get; set; }
    public int  CompletePct { get; set; }
}

public class TeamIndexData
{
    public List<Team>          Teams { get; set; } = [];
    public List<TeamIndexStat> Stats { get; set; } = [];
}

public class TeamManageData
{
    public Team             Team           { get; set; } = null!;
    public List<MemberItem> Members        { get; set; } = [];
    public List<AppUser>    NonMembers     { get; set; } = [];
    public string           CallerTeamRole { get; set; } = "Member";
}

public class MemberItem
{
    public int      UserId      { get; set; }
    public string   DisplayName { get; set; } = "";
    public string   Username    { get; set; } = "";
    public string   SystemRole  { get; set; } = "";
    public string?  Color       { get; set; }
    public string?  ImagePath   { get; set; }
    public string   TeamRole    { get; set; } = "";
    public DateTime JoinedAt    { get; set; }
}

public class TeamUserCreateRequest
{
    public string  DisplayName { get; set; } = "";
    public string  Username    { get; set; } = "";
    public string? Email       { get; set; }
    public string  Password    { get; set; } = "";
    public string  TeamRole    { get; set; } = "Member";
}
