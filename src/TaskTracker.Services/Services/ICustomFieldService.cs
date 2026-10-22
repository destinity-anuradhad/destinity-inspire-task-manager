using TaskTracker.Models;

namespace TaskTracker.Services;

public interface ICustomFieldService
{
    Task<List<CustomFieldDefinition>> GetDefinitionsAsync(int? teamId, bool includeInactive = false);
    Task<Dictionary<int, string>> GetValuesAsync(int taskId, int? teamId);
    Task<(bool Success, string? Error)> CreateDefinitionAsync(CustomFieldDefinition definition, IEnumerable<string>? selectOptions, UserCtx ctx);
    Task<(bool Success, string? Error)> SetValuesAsync(int taskId, IReadOnlyDictionary<int, string?> values, UserCtx ctx);
}
