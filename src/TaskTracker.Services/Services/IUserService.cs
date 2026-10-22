using TaskTracker.Models;

namespace TaskTracker.Services;

public interface IUserService
{
    Task<AppUser?> GetByIdAsync(int uid);
    Task<ProfileStats> GetProfileStatsAsync(int uid, string displayName);
    Task<(bool Success, string? Error)> UpdateProfileAsync(int uid, ProfileUpdateRequest req);
    Task<(bool Success, string? Error)> ChangePasswordAsync(int uid, string currentPassword, string newPassword);
    Task ClearMustChangePasswordAsync(int uid);
    Task UpdateWorkPreferencesAsync(int uid, decimal? dailyCapacityHours, string workingDays);
    Task UpdateNotificationsAsync(int uid, bool notifyOnAssigned, bool notifyOnComment, bool notifyOnBlocked);
    Task<List<UserTimeOff>> GetTimeOffAsync(int userId, DateTime? fromDate = null);
    Task<(bool Success, string? Error)> AddTimeOffAsync(int userId, DateTime startDate, DateTime endDate, string? reason, string createdBy);
    Task<(bool Success, string? Error)> RemoveTimeOffAsync(int id, int userId);

    Task<MasterIndexData> GetMasterDataAsync();
    Task<(bool Success, string? Error)> EditUserAsync(int id, string username, string displayName, string role, string color);
    Task<(bool Success, string? Error)> CreateUserAsync(MasterCreateUserRequest req);

    Task<List<WorkState>> GetWorkStatesAsync();
    Task<(bool Success, string? Error)> SaveWorkStateAsync(int? id, string name, string color, int? wipLimit, string? itemTypes);
    Task<(bool Success, string? Error)> DeleteWorkStateAsync(int id);
    Task ReorderWorkStatesAsync(int[] orderedIds);

    Task<AppUser?> GetByUsernameAsync(string username);

    Task<List<AppUser>> GetPendingUsersAsync();
    Task<(bool Success, string? Error)> ApproveUserAsync(int id);
    Task<(bool Success, string? Error)> RejectUserAsync(int id);

    Task<int> GetNotificationCountAsync(int userId, int? teamId);
    Task<List<Notification>> GetNotificationsAsync(int userId, int? teamId);
    Task MarkNotificationReadAsync(int id, int userId);
    Task MarkAllNotificationsReadAsync(int userId, int? teamId);
}

public class ProfileStats
{
    public int TasksAssigned  { get; set; }
    public int TasksCompleted { get; set; }
    public int TasksWatching  { get; set; }
}

public class ProfileUpdateRequest
{
    public string  DisplayName   { get; set; } = "";
    public string? Color         { get; set; }
    public string? JobTitle      { get; set; }
    public string? Department    { get; set; }
    public string? Email         { get; set; }
    public string? NewAvatarPath { get; set; }
}

public class MasterIndexData
{
    public List<AppUser>                       Users       { get; set; } = [];
    public List<Team>                          ActiveTeams { get; set; } = [];
    public Dictionary<int, List<UserTeamInfo>> UserTeams   { get; set; } = [];
    public List<WorkState>                     WorkStates  { get; set; } = [];
}

public class UserTeamInfo
{
    public int    UserId    { get; set; }
    public string TeamName  { get; set; } = "";
    public string TeamColor { get; set; } = "#094f70";
    public string TeamRole  { get; set; } = "Member";
}

public class MasterCreateUserRequest
{
    public string  DisplayName { get; set; } = "";
    public string  Username    { get; set; } = "";
    public string? Email       { get; set; }
    public string  Password    { get; set; } = "";
    public string  SystemRole  { get; set; } = "Member";
    public int?    TeamId      { get; set; }
    public string? TeamRole    { get; set; }
    public string? Color       { get; set; }
}
