using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class UserService(AppDbContext db) : IUserService
{
    private readonly AppDbContext _db = db;

    public async Task<AppUser?> GetByIdAsync(int uid) => await _db.AppUsers.FindAsync(uid);

    public async Task<AppUser?> GetByUsernameAsync(string username)
        => await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == username);

    public async Task<ProfileStats> GetProfileStatsAsync(int uid, string displayName)
        => new ProfileStats {
            TasksAssigned  = await _db.Tasks.CountAsync(t => t.Owner != null && t.Owner.Contains(displayName)),
            TasksCompleted = await _db.Tasks.CountAsync(t => t.Owner != null && t.Owner.Contains(displayName) && t.ActualEndDate.HasValue),
            TasksWatching  = await _db.TaskWatchers.CountAsync(w => w.UserId == uid),
        };

    public async Task<(bool Success, string? Error)> UpdateProfileAsync(int uid, ProfileUpdateRequest req)
    {
        var user = await _db.AppUsers.FindAsync(uid);
        if (user == null) return (false, "User not found");
        var oldName = user.DisplayName;
        user.DisplayName = req.DisplayName.Trim();
        if (!string.IsNullOrEmpty(req.NewAvatarPath)) user.ProfileImagePath = req.NewAvatarPath;
        if (!string.IsNullOrEmpty(req.Color))         user.Color       = req.Color;
        user.JobTitle   = string.IsNullOrWhiteSpace(req.JobTitle)   ? null : req.JobTitle.Trim();
        user.Department = string.IsNullOrWhiteSpace(req.Department) ? null : req.Department.Trim();
        user.Email      = string.IsNullOrWhiteSpace(req.Email)      ? null : req.Email.Trim();
        await _db.SaveChangesAsync();
        if (!string.Equals(oldName, user.DisplayName, StringComparison.Ordinal))
            await SyncDisplayNameChangeAsync(oldName, user.DisplayName);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(int uid, string currentPassword, string newPassword)
    {
        var user = await _db.AppUsers.FindAsync(uid);
        if (user == null) return (false, "User not found");
        var hasher = new PasswordHasher<AppUser>();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            return (false, "Current password is incorrect");
        user.PasswordHash        = hasher.HashPassword(user, newPassword);
        user.MustChangePassword  = false;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task ClearMustChangePasswordAsync(int uid)
    {
        var user = await _db.AppUsers.FindAsync(uid);
        if (user == null) return;
        user.MustChangePassword = false;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateWorkPreferencesAsync(int uid, decimal? dailyCapacityHours, string workingDays)
    {
        var user = await _db.AppUsers.FindAsync(uid);
        if (user == null) return;
        user.DailyCapacityHours = dailyCapacityHours;
        user.WorkingDays        = workingDays;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateNotificationsAsync(int uid, bool notifyOnAssigned, bool notifyOnComment, bool notifyOnBlocked)
    {
        var user = await _db.AppUsers.FindAsync(uid);
        if (user == null) return;
        user.NotifyOnAssigned = notifyOnAssigned;
        user.NotifyOnComment  = notifyOnComment;
        user.NotifyOnBlocked  = notifyOnBlocked;
        await _db.SaveChangesAsync();
    }

    public async Task<MasterIndexData> GetMasterDataAsync()
    {
        var users  = await _db.AppUsers.OrderBy(u => u.DisplayName).ToListAsync();
        var teams  = await _db.Teams.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync();
        var memberships = await _db.TeamMembers
            .Join(_db.Teams, m => m.TeamId, t => t.Id,
                (m, t) => new UserTeamInfo { UserId = m.UserId, TeamName = t.Name, TeamColor = t.Color, TeamRole = m.TeamRole })
            .ToListAsync();
        return new MasterIndexData {
            Users       = users,
            ActiveTeams = teams,
            UserTeams   = memberships.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.ToList())
        };
    }

    public async Task<(bool Success, string? Error)> EditUserAsync(int id, string username, string displayName, string role, string color)
    {
        var user = await _db.AppUsers.FindAsync(id);
        if (user == null) return (false, "User not found");

        var trimmedUsername = username?.Trim() ?? user.Username;
        if (!string.IsNullOrWhiteSpace(trimmedUsername) &&
            await _db.AppUsers.AnyAsync(u => u.Username == trimmedUsername && u.Id != id))
            return (false, $"Username '{trimmedUsername}' is already in use.");

        var oldName = user.DisplayName;
        if (!string.IsNullOrWhiteSpace(username))    user.Username    = trimmedUsername;
        if (!string.IsNullOrWhiteSpace(displayName)) user.DisplayName = displayName.Trim();
        if (role is "Admin" or "Member")             user.Role        = role;
        if (!string.IsNullOrEmpty(color))            user.Color       = color;
        await _db.SaveChangesAsync();
        if (!string.Equals(oldName, user.DisplayName, StringComparison.Ordinal))
            await SyncDisplayNameChangeAsync(oldName, user.DisplayName);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateUserAsync(MasterCreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName) || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return (false, "Display name, username and password are required.");
        if (req.Password.Length < 8) return (false, "Password must be at least 8 characters.");
        var uname = req.Username.Trim().ToLower();
        if (await _db.AppUsers.AnyAsync(u => u.Username == uname))
            return (false, $"Username '{uname}' is already taken.");

        var hasher = new PasswordHasher<AppUser>();
        var user = new AppUser {
            Username    = uname,
            DisplayName = req.DisplayName.Trim(),
            Email       = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim().ToLower(),
            Role        = req.SystemRole == "Admin" ? "Admin" : "Member",
            Status      = "Active",
            Color       = string.IsNullOrEmpty(req.Color) ? "#3b82f6" : req.Color,
            CreatedAt   = DateTime.Now,
        };
        user.PasswordHash = hasher.HashPassword(user, req.Password);
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();

        if (req.TeamId.HasValue && !string.IsNullOrEmpty(req.TeamRole))
        {
            _db.TeamMembers.Add(new TeamMember { TeamId = req.TeamId.Value, UserId = user.Id, TeamRole = req.TeamRole, JoinedAt = DateTime.Now });
            await _db.SaveChangesAsync();
        }
        return (true, null);
    }

    public async Task<int> GetNotificationCountAsync(int userId, int? teamId)
    {
        try
        {
            var q = _db.Notifications.Where(n => n.UserId == userId && !n.IsRead);
            if (teamId.HasValue)
                q = q.Where(n => !n.TaskId.HasValue || _db.Tasks.Any(t => t.Id == n.TaskId && t.TeamId == teamId));
            return await q.CountAsync();
        }
        catch { return 0; }
    }

    public async Task<List<Notification>> GetNotificationsAsync(int userId, int? teamId)
    {
        try
        {
            var q = _db.Notifications.Where(n => n.UserId == userId);
            if (teamId.HasValue)
                q = q.Where(n => !n.TaskId.HasValue || _db.Tasks.Any(t => t.Id == n.TaskId && t.TeamId == teamId));
            return await q.OrderByDescending(n => n.CreatedAt).Take(20).ToListAsync();
        }
        catch { return []; }
    }

    public async Task MarkNotificationReadAsync(int id, int userId)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (n != null) { n.IsRead = true; await _db.SaveChangesAsync(); }
    }

    public async Task MarkAllNotificationsReadAsync(int userId, int? teamId)
    {
        var q = _db.Notifications.Where(n => n.UserId == userId && !n.IsRead);
        if (teamId.HasValue)
            q = q.Where(n => !n.TaskId.HasValue || _db.Tasks.Any(t => t.Id == n.TaskId && t.TeamId == teamId));
        var items = await q.ToListAsync();
        foreach (var n in items) n.IsRead = true;
        if (items.Count > 0) await _db.SaveChangesAsync();
    }

    private async Task SyncDisplayNameChangeAsync(string oldName, string newName)
    {
        var owner = await _db.Owners.FirstOrDefaultAsync(o => o.Name == oldName);
        if (owner != null) owner.Name = newName;
        var tasks = await _db.Tasks.Where(t => t.Owner != null && t.Owner.Contains(oldName)).ToListAsync();
        foreach (var task in tasks)
            task.Owner = string.Join(", ", (task.Owner ?? "").Split(',')
                .Select(o => o.Trim())
                .Select(o => string.Equals(o, oldName, StringComparison.OrdinalIgnoreCase) ? newName : o));
        await _db.SaveChangesAsync();
    }
}
