using TaskTracker.Models;

namespace TaskTracker.Services;

public interface IUserService
{
    Task<AppUser?> GetByIdAsync(int uid);
    Task<ProfileStats> GetProfileStatsAsync(int uid, string displayName);
    Task<(bool Success, string? Error)> UpdateProfileAsync(int uid, ProfileUpdateRequest req);
    Task<(bool Success, string? Error)> ChangePasswordAsync(int uid, string currentPassword, string newPassword);
    Task UpdateWorkPreferencesAsync(int uid, decimal? dailyCapacityHours, string workingDays);
    Task UpdateNotificationsAsync(int uid, bool notifyOnAssigned, bool notifyOnComment, bool notifyOnBlocked);

    Task<MasterIndexData> GetMasterDataAsync();
    Task<(bool Success, string? Error)> EditUserAsync(int id, string username, string displayName, string role, string color);
    Task<(bool Success, string? Error)> CreateUserAsync(MasterCreateUserRequest req);

    Task<AppUser?> GetByUsernameAsync(string username);

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
    public List<AppUser>                     Users       { get; set; } = [];
    public List<Team>                        ActiveTeams { get; set; } = [];
    public Dictionary<int, List<UserTeamInfo>> UserTeams { get; set; } = [];
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
