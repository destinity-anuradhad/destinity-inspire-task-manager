using TaskTracker.Models;

namespace TaskTracker.Services;

public interface ITaskTemplateService
{
    Task<List<TaskTemplate>> GetForTeamAsync(int? teamId);
    Task<TaskTemplate?> GetByIdAsync(int id, int? teamId);
    Task<List<TaskTemplateChecklistItem>> GetChecklistAsync(int templateId, int? teamId);
    Task<(bool Success, string? Error)> SaveAsync(TaskTemplate template, IEnumerable<string>? checklistItems, UserCtx ctx);
    Task<(bool Success, string? Error)> DeleteAsync(int id, UserCtx ctx);
}
