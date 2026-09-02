using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class ProjectService(AppDbContext db) : IProjectService
{
    public Task<List<ProjectItem>> GetForTeamAsync(int? teamId, bool includeArchived = false)
    {
        var query = db.Projects.AsNoTracking();
        if (teamId.HasValue) query = query.Where(p => p.TeamId == teamId);
        if (!includeArchived) query = query.Where(p => p.Status != "Archived");
        return query.OrderBy(p => p.Status).ThenBy(p => p.Name).ToListAsync();
    }

    public Task<List<AppUser>> GetTeamMembersAsync(int teamId) => db.TeamMembers
        .Where(m => m.TeamId == teamId)
        .Join(db.AppUsers, m => m.UserId, u => u.Id, (_, u) => u)
        .OrderBy(u => u.DisplayName).ToListAsync();

    public Task<List<ProjectRelease>> GetReleasesAsync(IEnumerable<int> projectIds)
    {
        var ids = projectIds.Distinct().ToArray();
        return db.ProjectReleases.AsNoTracking().Where(r => ids.Contains(r.ProjectId))
            .OrderBy(r => r.TargetDate).ThenBy(r => r.Name).ToListAsync();
    }

    public async Task<(bool Success, string? Error, ProjectItem? Project)> SaveAsync(ProjectItem project, UserCtx ctx)
    {
        if (!ctx.IsTeamAdmin) return (false, "You do not have permission to manage projects.", null);
        var name = project.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return (false, "Project name is required.", null);
        if (!ctx.TeamId.HasValue) return (false, "Select a team before managing projects.", null);
        if (project.LeadUserId.HasValue && !await db.TeamMembers.AnyAsync(m => m.TeamId == ctx.TeamId && m.UserId == project.LeadUserId))
            return (false, "Project lead must belong to the active team.", null);

        ProjectItem entity;
        if (project.Id == 0)
        {
            if (await db.Projects.AnyAsync(p => p.TeamId == ctx.TeamId && p.Name == name))
                return (false, "A project with that name already exists for this team.", null);
            entity = new ProjectItem { TeamId = ctx.TeamId, Name = name, CreatedAt = DateTime.UtcNow, CreatedBy = ctx.DisplayName };
            db.Projects.Add(entity);
        }
        else
        {
            var existing = await db.Projects.FirstOrDefaultAsync(p => p.Id == project.Id && p.TeamId == ctx.TeamId);
            if (existing == null) return (false, "Project not found.", null);
            entity = existing;
            if (entity.Status == "Archived") return (false, "Archived projects cannot be edited.", null);
        }

        entity.Name = name;
        entity.Color = string.IsNullOrWhiteSpace(project.Color) ? null : project.Color.Trim();
        entity.Description = string.IsNullOrWhiteSpace(project.Description) ? null : project.Description.Trim();
        entity.LeadUserId = project.LeadUserId;
        entity.StartDate = project.StartDate?.Date;
        entity.TargetDate = project.TargetDate?.Date;
        entity.Status = "Active";
        await db.SaveChangesAsync();
        return (true, null, entity);
    }

    public async Task<(bool Success, string? Error)> ArchiveAsync(int id, UserCtx ctx)
    {
        if (!ctx.IsTeamAdmin) return (false, "You do not have permission to manage projects.");
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.TeamId == ctx.TeamId);
        if (project == null) return (false, "Project not found.");
        project.Status = "Archived";
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateReleaseAsync(ProjectRelease release, UserCtx ctx)
    {
        if (!ctx.IsTeamAdmin) return (false, "You do not have permission to manage releases.");
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == release.ProjectId && p.TeamId == ctx.TeamId && p.Status == "Active");
        if (project == null) return (false, "Project not found or is archived.");
        var name = release.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return (false, "Release name is required.");
        if (await db.ProjectReleases.AnyAsync(r => r.ProjectId == project.Id && r.Name == name)) return (false, "That release already exists for this project.");
        db.ProjectReleases.Add(new ProjectRelease {
            ProjectId = project.Id, Name = name, Status = "Planned", TargetDate = release.TargetDate?.Date,
            Notes = string.IsNullOrWhiteSpace(release.Notes) ? null : release.Notes.Trim(), CreatedAt = DateTime.UtcNow, CreatedBy = ctx.DisplayName
        });
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> MarkReleasedAsync(int id, UserCtx ctx)
    {
        if (!ctx.IsTeamAdmin) return (false, "You do not have permission to manage releases.");
        var release = await db.ProjectReleases.Join(db.Projects, r => r.ProjectId, p => p.Id, (r, p) => new { r, p })
            .FirstOrDefaultAsync(x => x.r.Id == id && x.p.TeamId == ctx.TeamId);
        if (release == null) return (false, "Release not found.");
        if (release.r.Status == "Released") return (false, "Release is already marked released.");
        release.r.Status = "Released";
        release.r.ReleasedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (true, null);
    }
}
