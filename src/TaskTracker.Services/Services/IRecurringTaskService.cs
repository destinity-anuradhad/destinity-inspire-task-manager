using TaskTracker.Models;

namespace TaskTracker.Services;

public interface IRecurringTaskService
{
    Task<List<RecurringTaskDefinition>> GetForTeamAsync(int? teamId);
    Task<(bool Success, string? Error)> CreateAsync(RecurringTaskDefinition definition, UserCtx ctx);
    Task<(bool Success, string? Error)> ToggleAsync(int id, bool isActive, UserCtx ctx);
}
