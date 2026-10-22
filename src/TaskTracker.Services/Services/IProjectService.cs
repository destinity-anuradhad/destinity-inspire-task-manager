using TaskTracker.Models;

namespace TaskTracker.Services;

public interface IProjectService
{
    Task<List<ProjectItem>> GetForTeamAsync(int? teamId, bool includeArchived = false);
    Task<List<AppUser>> GetTeamMembersAsync(int teamId);
    Task<List<ProjectRelease>> GetReleasesAsync(IEnumerable<int> projectIds);
    Task<(bool Success, string? Error, ProjectItem? Project)> SaveAsync(ProjectItem project, UserCtx ctx);
    Task<(bool Success, string? Error)> ArchiveAsync(int id, UserCtx ctx);
    Task<(bool Success, string? Error)> CreateReleaseAsync(ProjectRelease release, UserCtx ctx);
    Task<(bool Success, string? Error)> MarkReleasedAsync(int id, UserCtx ctx);
}
