using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class UserService(AppDbContext db) : IUserService
{
    private readonly AppDbContext _db = db;

    private static void RevokeSessions(AppUser user) => user.SessionVersion++;

    public async Task<AppUser?> GetByIdAsync(int uid) => await _db.AppUsers.FindAsync(uid);

    public async Task<AppUser?> GetByUsernameAsync(string username)
        => await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == username);

    public async Task<ProfileStats> GetProfileStatsAsync(int uid, string displayName)
    {
        return new ProfileStats {
            TasksAssigned  = await _db.TaskOwners.CountAsync(o => o.UserId == uid),
            TasksCompleted = await _db.TaskOwners.CountAsync(o =>
                o.UserId == uid && _db.Tasks.Any(t => t.Id == o.TaskId && t.ActualEndDate.HasValue)),
            TasksWatching  = await _db.TaskWatchers.CountAsync(w => w.UserId == uid),
        };
    }

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
        RevokeSessions(user);
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

    public async Task<(bool Success, string? Error)> AddTimeOffAsync(
        int userId, DateTime startDate, DateTime endDate, string? reason, string createdBy)
    {
        if (endDate.Date < startDate.Date) return (false, "End date cannot be before start date.");
        if (!await _db.AppUsers.AnyAsync(u => u.Id == userId)) return (false, "User not found.");
        _db.UserTimeOff.Add(new UserTimeOff {
            UserId = userId, StartDate = startDate.Date, EndDate = endDate.Date,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CreatedAt = DateTime.UtcNow, CreatedBy = createdBy
        });
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public Task<List<UserTimeOff>> GetTimeOffAsync(int userId, DateTime? fromDate = null)
    {
        var query = _db.UserTimeOff.Where(t => t.UserId == userId);
        if (fromDate.HasValue)
            query = query.Where(t => t.EndDate >= fromDate.Value.Date);
        return query.OrderBy(t => t.StartDate).ThenBy(t => t.Id).ToListAsync();
    }

    public async Task<(bool Success, string? Error)> RemoveTimeOffAsync(int id, int userId)
    {
        var timeOff = await _db.UserTimeOff.FindAsync(id);
        if (timeOff == null || timeOff.UserId != userId) return (false, "Time off entry not found.");
        _db.UserTimeOff.Remove(timeOff);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<MasterIndexData> GetMasterDataAsync()
    {
        var users  = await _db.AppUsers.OrderBy(u => u.DisplayName).ToListAsync();
        var teams  = await _db.Teams.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync();
        var memberships = await _db.TeamMembers
            .Join(_db.Teams, m => m.TeamId, t => t.Id,
                (m, t) => new UserTeamInfo { UserId = m.UserId, TeamName = t.Name, TeamColor = t.Color, TeamRole = m.TeamRole })
            .ToListAsync();
        var workStates = await _db.WorkStates.OrderBy(w => w.SortOrder).ThenBy(w => w.Id).ToListAsync();
        return new MasterIndexData {
            Users       = users,
            ActiveTeams = teams,
            UserTeams   = memberships.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.ToList()),
            WorkStates  = workStates,
        };
    }

    public async Task<List<WorkState>> GetWorkStatesAsync()
        => await _db.WorkStates.OrderBy(w => w.SortOrder).ThenBy(w => w.Id).ToListAsync();

    public async Task<(bool Success, string? Error)> SaveWorkStateAsync(int? id, string name, string color, int? wipLimit, string? itemTypes)
    {
        name = name?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) return (false, "Column name is required.");
        if (await _db.WorkStates.AnyAsync(w => w.Name == name && w.Id != (id ?? 0)))
            return (false, $"A column named '{name}' already exists.");

        var normalizedTypes = string.IsNullOrWhiteSpace(itemTypes) ? null : itemTypes.Trim();

        if (id.HasValue && id > 0)
        {
            var ws = await _db.WorkStates.FindAsync(id.Value);
            if (ws == null) return (false, "Column not found.");
            ws.Name      = name;
            ws.Color     = string.IsNullOrEmpty(color) ? ws.Color : color;
            ws.WipLimit  = wipLimit;
            ws.ItemTypes = normalizedTypes;
        }
        else
        {
            var maxOrder = await _db.WorkStates.MaxAsync(w => (int?)w.SortOrder) ?? 0;
            _db.WorkStates.Add(new WorkState {
                Name      = name,
                Color     = string.IsNullOrEmpty(color) ? "#094f70" : color,
                WipLimit  = wipLimit,
                ItemTypes = normalizedTypes,
                SortOrder = maxOrder + 10,
            });
        }
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteWorkStateAsync(int id)
    {
        var ws = await _db.WorkStates.FindAsync(id);
        if (ws == null) return (false, "Column not found.");
        var inUse = await _db.Tasks.AnyAsync(t => !t.IsDeleted && t.Status == ws.Name);
        if (inUse) return (false, $"Cannot delete '{ws.Name}' — it has active tasks. Move them first.");
        _db.WorkStates.Remove(ws);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task ReorderWorkStatesAsync(int[] orderedIds)
    {
        var states = await _db.WorkStates.Where(w => orderedIds.Contains(w.Id)).ToListAsync();
        for (int i = 0; i < orderedIds.Length; i++)
        {
            var ws = states.FirstOrDefault(s => s.Id == orderedIds[i]);
            if (ws != null) ws.SortOrder = (i + 1) * 10;
        }
        await _db.SaveChangesAsync();
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
        var oldUsername = user.Username;
        var oldRole = user.Role;
        var oldColor = user.Color;
        if (!string.IsNullOrWhiteSpace(username))    user.Username    = trimmedUsername;
        if (!string.IsNullOrWhiteSpace(displayName)) user.DisplayName = displayName.Trim();
        if (role is "Admin" or "Member")             user.Role        = role;
        if (!string.IsNullOrEmpty(color))            user.Color       = color;
        if (oldUsername != user.Username || oldName != user.DisplayName
            || oldRole != user.Role || oldColor != user.Color)
        {
            RevokeSessions(user);
        }
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
        try { await _db.SaveChangesAsync(); }
        catch (Exception ex) { return (false, ex.InnerException?.Message ?? ex.Message); }

        if (req.TeamId.HasValue && !string.IsNullOrEmpty(req.TeamRole))
        {
            _db.TeamMembers.Add(new TeamMember { TeamId = req.TeamId.Value, UserId = user.Id, TeamRole = req.TeamRole, JoinedAt = DateTime.Now });
            try { await _db.SaveChangesAsync(); }
            catch { /* user created; team assignment non-critical */ }
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

        // Update Owner, Reporter, and CreatedBy string columns when a display name changes.
        var tasks = await _db.Tasks.Where(t =>
            (t.Owner     != null && t.Owner.Contains(oldName)) ||
            (t.Reporter  != null && t.Reporter  == oldName)    ||
            (t.CreatedBy != null && t.CreatedBy == oldName)).ToListAsync();

        foreach (var task in tasks)
        {
            if (!string.IsNullOrEmpty(task.Owner) && task.Owner.Contains(oldName, StringComparison.OrdinalIgnoreCase))
            {
                var parts = task.Owner.Split(',').Select(p => p.Trim())
                    .Select(p => string.Equals(p, oldName, StringComparison.OrdinalIgnoreCase) ? newName : p);
                task.Owner = string.Join(", ", parts);
            }
            if (string.Equals(task.Reporter,  oldName, StringComparison.OrdinalIgnoreCase)) task.Reporter  = newName;
            if (string.Equals(task.CreatedBy, oldName, StringComparison.OrdinalIgnoreCase)) task.CreatedBy = newName;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<List<AppUser>> GetPendingUsersAsync()
        => await _db.AppUsers.Where(u => u.Status == "Pending").OrderBy(u => u.CreatedAt).ToListAsync();

    public async Task<(bool Success, string? Error)> ApproveUserAsync(int id)
    {
        var user = await _db.AppUsers.FindAsync(id);
        if (user == null) return (false, "User not found.");
        user.Status = "Active";
        RevokeSessions(user);
        await _db.SaveChangesAsync();
        if (user.RequestedTeamId.HasValue)
        {
            var already = await _db.TeamMembers.AnyAsync(m => m.TeamId == user.RequestedTeamId.Value && m.UserId == id);
            if (!already)
            {
                _db.TeamMembers.Add(new TeamMember { TeamId = user.RequestedTeamId.Value, UserId = id, TeamRole = "Member", JoinedAt = DateTime.Now });
                try { await _db.SaveChangesAsync(); } catch { }
            }
        }
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RejectUserAsync(int id)
    {
        var user = await _db.AppUsers.FindAsync(id);
        if (user == null) return (false, "User not found.");
        _db.AppUsers.Remove(user);
        await _db.SaveChangesAsync();
        return (true, null);
    }
}
