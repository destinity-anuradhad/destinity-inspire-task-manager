using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class TeamService(AppDbContext db) : ITeamService
{
    private readonly AppDbContext _db = db;

    public async Task<List<TeamPickItem>> GetUserMembershipsAsync(int userId)
        => await _db.TeamMembers.Where(m => m.UserId == userId)
            .Join(_db.Teams.Where(t => t.IsActive), m => m.TeamId, t => t.Id,
                  (m, t) => new TeamPickItem { Team = t, TeamRole = m.TeamRole })
            .ToListAsync();

    public async Task<List<Team>> GetAllActiveTeamsAsync()
        => await _db.Teams.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync();

    public async Task<Dictionary<int, int>> GetMemberCountsAsync()
        => await _db.TeamMembers.GroupBy(m => m.TeamId)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count);

    public async Task<TeamIndexData> GetIndexDataAsync()
    {
        var teams    = await _db.Teams.AsNoTracking().OrderBy(t => t.Name).ToListAsync();
        var allTasks = await _db.Tasks.AsNoTracking().Where(t => !t.IsDeleted).ToListAsync();
        var members  = await _db.TeamMembers.AsNoTracking().ToListAsync();
        var today    = DateTime.Today;

        var stats = teams.Select(team => {
            var tasks   = allTasks.Where(t => t.TeamId == team.Id).ToList();
            var done    = tasks.Count(TaskStatusRules.IsDone);
            var active  = tasks.Count(TaskStatusRules.IsActive);
            var overdue = tasks.Count(t => !TaskStatusRules.IsDone(t) && t.TargetDate.HasValue && t.TargetDate.Value.Date < today);
            var mCount  = members.Count(m => m.TeamId == team.Id);
            return new TeamIndexStat {
                Team = team, Total = tasks.Count, Done = done, Active = active,
                Overdue = overdue, MemberCount = mCount,
                CompletePct = tasks.Count > 0 ? (int)Math.Round((double)done / tasks.Count * 100) : 0
            };
        }).ToList();

        return new TeamIndexData { Teams = teams, Stats = stats };
    }

    public async Task<Team?> CreateTeamAsync(string name, string? description, string? color, string? createdBy)
    {
        var team = new Team {
            Name        = name.Trim(),
            Description = description?.Trim(),
            Color       = string.IsNullOrEmpty(color) ? "#094f70" : color,
            CreatedBy   = createdBy,
            CreatedAt   = DateTime.Now,
        };
        _db.Teams.Add(team);
        await _db.SaveChangesAsync();
        return team;
    }

    public async Task<TeamManageData?> GetManageDataAsync(int teamId, int callerId, bool isSysAdmin)
    {
        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) return null;
        var myMembership = await _db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == callerId);
        if (!isSysAdmin && myMembership?.TeamRole != "TeamAdmin") return null;

        var members = await _db.TeamMembers.Where(m => m.TeamId == teamId)
            .Join(_db.AppUsers, m => m.UserId, u => u.Id, (m, u) => new MemberItem {
                UserId = u.Id, DisplayName = u.DisplayName, Username = u.Username,
                SystemRole = u.Role, Color = u.Color, ImagePath = u.ProfileImagePath,
                TeamRole = m.TeamRole, JoinedAt = m.JoinedAt
            }).OrderBy(x => x.DisplayName).ToListAsync();

        var memberIds  = members.Select(m => m.UserId).ToList();
        var nonMembers = await _db.AppUsers
            .Where(u => !memberIds.Contains(u.Id) && u.Status == "Active")
            .OrderBy(u => u.DisplayName).ToListAsync();

        var callerTeamRole = myMembership?.TeamRole ?? (isSysAdmin ? "TeamAdmin" : "Member");
        return new TeamManageData { Team = team, Members = members, NonMembers = nonMembers, CallerTeamRole = callerTeamRole };
    }

    public async Task<(bool Success, string? Error)> EditTeamAsync(int teamId, string name, string? description, string? color, int callerId, bool isSysAdmin)
    {
        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) return (false, "Not found");
        var myMembership = await _db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == callerId);
        if (!isSysAdmin && myMembership?.TeamRole != "TeamAdmin") return (false, "Forbidden");
        team.Name        = name.Trim();
        team.Description = description?.Trim();
        team.Color       = string.IsNullOrEmpty(color) ? "#094f70" : color;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<bool> ToggleActiveAsync(int teamId)
    {
        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) return false;
        team.IsActive = !team.IsActive;
        await _db.SaveChangesAsync();
        return true;
    }

    private static readonly string[] ValidTeamRoles = ["TeamAdmin", "TeamLead", "Member"];

    public async Task<(bool Success, string? Error)> AddMemberAsync(int teamId, int userId, string teamRole, int callerId, bool isSysAdmin)
    {
        if (!ValidTeamRoles.Contains(teamRole)) return (false, "Invalid team role.");
        var myMembership = await _db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == callerId);
        if (!isSysAdmin && myMembership?.TeamRole != "TeamAdmin") return (false, "Forbidden");
        var exists = await _db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == userId);
        if (!exists)
        {
            _db.TeamMembers.Add(new TeamMember { TeamId = teamId, UserId = userId, TeamRole = teamRole, JoinedAt = DateTime.Now });
            var user = await _db.AppUsers.FindAsync(userId);
            if (user != null) user.SessionVersion++;
            await _db.SaveChangesAsync();
        }
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RemoveMemberAsync(int teamId, int userId, int callerId, bool isSysAdmin)
    {
        var myMembership = await _db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == callerId);
        if (!isSysAdmin && myMembership?.TeamRole != "TeamAdmin") return (false, "Forbidden");
        var target = await _db.AppUsers.FindAsync(userId);
        if (!isSysAdmin && target?.Role == "Admin")
            return (false, "Only a System Administrator can remove another System Administrator.");
        var m = await _db.TeamMembers.FirstOrDefaultAsync(x => x.TeamId == teamId && x.UserId == userId);
        if (m != null)
        {
            _db.TeamMembers.Remove(m);
            if (target != null) target.SessionVersion++;
            await _db.SaveChangesAsync();
        }
        return (true, null);
    }

    public async Task<(bool Success, string? Error, AppUser? CreatedUser)> CreateTeamUserAsync(
        int teamId, TeamUserCreateRequest req, int callerId, bool isSysAdmin, string? callerRole)
    {
        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) return (false, "Team not found", null);
        var myMembership = await _db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == callerId);
        if (!isSysAdmin && myMembership?.TeamRole != "TeamAdmin" && myMembership?.TeamRole != "TeamLead")
            return (false, "Forbidden", null);

        if (!ValidTeamRoles.Contains(req.TeamRole)) return (false, "Invalid team role.", null);
        var teamRole = req.TeamRole;
        if (!isSysAdmin)
        {
            if (callerRole == "TeamLead") teamRole = "Member";         // TeamLead can only create Members
            else if (teamRole == "TeamAdmin") teamRole = "TeamLead";   // TeamAdmin cannot create another TeamAdmin
        }

        if (string.IsNullOrWhiteSpace(req.DisplayName) || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return (false, "Display name, username and password are required.", null);
        if (req.Password.Length < 8) return (false, "Password must be at least 8 characters.", null);
        var uname = req.Username.Trim().ToLower();
        if (await _db.AppUsers.AnyAsync(u => u.Username == uname))
            return (false, $"Username '{uname}' is already taken.", null);

        var hasher = new PasswordHasher<AppUser>();
        var user = new AppUser {
            Username    = uname,
            DisplayName = req.DisplayName.Trim(),
            Email       = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim().ToLower(),
            Role        = "Member", Status = "Active", CreatedAt = DateTime.Now,
        };
        user.PasswordHash = hasher.HashPassword(user, req.Password);
        _db.AppUsers.Add(user);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            return (false, $"Could not create user: {ex.InnerException?.Message ?? ex.Message}", null);
        }
        _db.TeamMembers.Add(new TeamMember { TeamId = teamId, UserId = user.Id, TeamRole = teamRole, JoinedAt = DateTime.Now });
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _db.AppUsers.Remove(user);
            await _db.SaveChangesAsync();
            return (false, $"User created but could not add to team: {ex.InnerException?.Message ?? ex.Message}", null);
        }
        return (true, null, user);
    }

    public async Task<(bool Success, string? Error)> UpdateRoleAsync(int teamId, int userId, string teamRole, int callerId, bool isSysAdmin)
    {
        if (!ValidTeamRoles.Contains(teamRole)) return (false, "Invalid team role.");
        var myMembership = await _db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == callerId);
        if (!isSysAdmin && myMembership?.TeamRole != "TeamAdmin") return (false, "Forbidden");
        var target = await _db.AppUsers.FindAsync(userId);
        if (!isSysAdmin && target?.Role == "Admin")
            return (false, "Only a System Administrator can change another System Administrator's role.");
        var m = await _db.TeamMembers.FirstOrDefaultAsync(x => x.TeamId == teamId && x.UserId == userId);
        if (m != null && m.TeamRole != teamRole)
        {
            m.TeamRole = teamRole;
            if (target != null) target.SessionVersion++;
            await _db.SaveChangesAsync();
        }
        return (true, null);
    }

    public async Task<(Team? Team, string TeamRole)> GetSwitchDataAsync(int teamId, int userId, bool isSysAdmin)
    {
        var team = await _db.Teams.FindAsync(teamId);
        if (team == null || !team.IsActive) return (null, "");
        var membership = await _db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId);
        if (membership == null && !isSysAdmin) return (null, "");
        return (team, membership?.TeamRole ?? "TeamAdmin");
    }

    public async Task<AppUser?> GetUserForAuthAsync(int userId) => await _db.AppUsers.FindAsync(userId);

    public async Task<string?> GetCallerTeamRoleAsync(int teamId, int userId)
        => await _db.TeamMembers
            .Where(m => m.TeamId == teamId && m.UserId == userId)
            .Select(m => m.TeamRole)
            .FirstOrDefaultAsync();
}
