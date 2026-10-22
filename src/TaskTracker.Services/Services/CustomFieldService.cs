using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class CustomFieldService(AppDbContext db) : ICustomFieldService
{
    private static readonly HashSet<string> FieldTypes = new(StringComparer.Ordinal) { "Text", "Number", "Date", "Boolean", "Select" };

    public Task<List<CustomFieldDefinition>> GetDefinitionsAsync(int? teamId, bool includeInactive = false) => db.CustomFieldDefinitions.AsNoTracking()
        .Where(d => d.TeamId == teamId && (includeInactive || d.IsActive)).OrderBy(d => d.SortOrder).ThenBy(d => d.Name).ToListAsync();

    public async Task<Dictionary<int, string>> GetValuesAsync(int taskId, int? teamId)
    {
        if (!await db.Tasks.AnyAsync(t => t.Id == taskId && t.TeamId == teamId)) return [];
        return await db.TaskCustomFieldValues.AsNoTracking().Where(v => v.TaskId == taskId)
            .ToDictionaryAsync(v => v.CustomFieldDefinitionId, v => v.Value);
    }

    public async Task<(bool Success, string? Error)> CreateDefinitionAsync(CustomFieldDefinition definition, IEnumerable<string>? selectOptions, UserCtx ctx)
    {
        if (!ctx.IsTeamAdmin || !ctx.TeamId.HasValue) return (false, "Only team administrators can manage custom fields.");
        var name = definition.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) return (false, "A custom-field name of up to 100 characters is required.");
        if (!FieldTypes.Contains(definition.FieldType)) return (false, "Unsupported custom-field type.");
        if (await db.CustomFieldDefinitions.AnyAsync(d => d.TeamId == ctx.TeamId && d.Name == name)) return (false, "A custom field with that name already exists.");
        var options = (selectOptions ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Take(50).ToList();
        if (options.Any(x => x.Length > 100)) return (false, "Select options may not exceed 100 characters.");
        if (definition.FieldType == "Select" && options.Count == 0) return (false, "A select field needs at least one option.");
        db.CustomFieldDefinitions.Add(new CustomFieldDefinition {
            TeamId = ctx.TeamId.Value, Name = name, FieldType = definition.FieldType, IsRequired = definition.IsRequired,
            OptionsJson = definition.FieldType == "Select" ? JsonSerializer.Serialize(options) : null,
            SortOrder = definition.SortOrder, CreatedBy = ctx.DisplayName
        });
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SetValuesAsync(int taskId, IReadOnlyDictionary<int, string?> values, UserCtx ctx)
    {
        if (!ctx.IsTeamLead || !ctx.TeamId.HasValue) return (false, "You do not have permission to update custom fields.");
        if (!await db.Tasks.AnyAsync(t => t.Id == taskId && t.TeamId == ctx.TeamId)) return (false, "Task not found.");
        var definitions = await db.CustomFieldDefinitions.Where(d => d.TeamId == ctx.TeamId && d.IsActive).ToDictionaryAsync(d => d.Id);
        foreach (var (definitionId, submitted) in values)
        {
            if (!definitions.TryGetValue(definitionId, out var definition)) return (false, "Custom field not found.");
            var canonical = Canonicalize(definition, submitted, out var error);
            if (error != null) return (false, error);
            var existing = await db.TaskCustomFieldValues.FirstOrDefaultAsync(v => v.TaskId == taskId && v.CustomFieldDefinitionId == definitionId);
            if (canonical == null) { if (existing != null) db.TaskCustomFieldValues.Remove(existing); continue; }
            if (existing == null) db.TaskCustomFieldValues.Add(new TaskCustomFieldValue { TaskId = taskId, CustomFieldDefinitionId = definitionId, Value = canonical, UpdatedBy = ctx.DisplayName });
            else { existing.Value = canonical; existing.UpdatedAt = DateTime.UtcNow; existing.UpdatedBy = ctx.DisplayName; }
        }
        foreach (var required in definitions.Values.Where(d => d.IsRequired))
            if (!await db.TaskCustomFieldValues.AnyAsync(v => v.TaskId == taskId && v.CustomFieldDefinitionId == required.Id) && !values.ContainsKey(required.Id)) return (false, $"{required.Name} is required.");
        await db.SaveChangesAsync();
        return (true, null);
    }

    private static string? Canonicalize(CustomFieldDefinition definition, string? submitted, out string? error)
    {
        error = null;
        var value = submitted?.Trim();
        if (string.IsNullOrEmpty(value)) { if (definition.IsRequired) error = $"{definition.Name} is required."; return null; }
        if (value.Length > 1000) { error = $"{definition.Name} is too long."; return null; }
        if (definition.FieldType == "Number") { if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)) error = $"{definition.Name} must be a number."; else return number.ToString(CultureInfo.InvariantCulture); return null; }
        if (definition.FieldType == "Date") { if (!DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) error = $"{definition.Name} must be a date."; else return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); return null; }
        if (definition.FieldType == "Boolean") { if (!bool.TryParse(value, out var boolean)) error = $"{definition.Name} must be true or false."; else return boolean ? "true" : "false"; return null; }
        if (definition.FieldType == "Select" && !JsonSerializer.Deserialize<List<string>>(definition.OptionsJson ?? "[]")!.Contains(value, StringComparer.Ordinal)) error = $"{definition.Name} has an invalid option.";
        return error == null ? value : null;
    }
}
