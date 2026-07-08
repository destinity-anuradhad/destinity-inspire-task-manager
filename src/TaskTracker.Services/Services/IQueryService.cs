using TaskTracker.Models;

namespace TaskTracker.Services;

public interface IQueryService
{
    Task<List<SavedQuery>> GetQueriesAsync(string displayName, int? teamId);
    Task<(bool Success, string? Error)> SaveQueryAsync(string name, string? description, bool isShared, string filters, string displayName, int? teamId);
    Task<(bool Success, string? Error, bool IsShared)> ToggleShareAsync(int id, string displayName, bool isAdmin, int? teamId);
    Task<(bool Success, string? Error)> DeleteAsync(int id, string displayName, bool isAdmin, int? teamId);
    Task<Dictionary<string, string>?> GetFiltersAsync(int id, string? displayName = null, bool isAdmin = false, int? teamId = null);
}
