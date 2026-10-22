using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class TaskTemplateService(AppDbContext db) : ITaskTemplateService
{
    public Task<List<TaskTemplate>> GetForTeamAsync(int? teamId) => db.TaskTemplates.AsNoTracking()
        .Where(t => t.TeamId == teamId).OrderBy(t => t.Name).ToListAsync();

    public Task<TaskTemplate?> GetByIdAsync(int id, int? teamId) => db.TaskTemplates.AsNoTracking()
        .FirstOrDefaultAsync(t => t.Id == id && t.TeamId == teamId);

    public async Task<List<TaskTemplateChecklistItem>> GetChecklistAsync(int templateId, int? teamId)
    {
        if (!await db.TaskTemplates.AnyAsync(t => t.Id == templateId && t.TeamId == teamId)) return [];
        return await db.TaskTemplateChecklistItems.Where(i => i.TaskTemplateId == templateId).OrderBy(i => i.SortOrder).ToListAsync();
    }

    public async Task<(bool Success, string? Error)> SaveAsync(TaskTemplate template, IEnumerable<string>? checklistItems, UserCtx ctx)
    {
        if (!ctx.IsTeamAdmin) return (false, "You do not have permission to manage templates.");
        if (!ctx.TeamId.HasValue) return (false, "Select a team before managing templates.");
        var name = template.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return (false, "Template name is required.");
        if (template.Estimate is null or < 0.166667m) return (false, "Estimate must be at least 10 minutes.");
        if (Math.Abs(template.Estimate.Value * 60m / 10m - decimal.Round(template.Estimate.Value * 60m / 10m, 0)) > 0.001m)
            return (false, "Estimate must use 10-minute increments (for example, 0.166667 hours for 10 minutes).");
        if (await db.TaskTemplates.AnyAsync(t => t.TeamId == ctx.TeamId && t.Name == name)) return (false, "A template with that name already exists for this team.");
        var entity = new TaskTemplate {
            TeamId = ctx.TeamId, Name = name, ItemType = template.ItemType, Priority = template.Priority,
            Estimate = template.Estimate, EstimateUnit = "Hours", Notes = template.Notes,
            Labels = template.Labels, AcceptanceCriteria = template.AcceptanceCriteria,
            CreatedAt = DateTime.UtcNow, CreatedBy = ctx.DisplayName
        };
        db.TaskTemplates.Add(entity);
        var items = (checklistItems ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Take(50).ToList();
        for (var i = 0; i < items.Count; i++) db.TaskTemplateChecklistItems.Add(new TaskTemplateChecklistItem { TaskTemplate = entity, Text = items[i], SortOrder = i });
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int id, UserCtx ctx)
    {
        if (!ctx.IsTeamAdmin) return (false, "You do not have permission to manage templates.");
        var template = await db.TaskTemplates.FirstOrDefaultAsync(t => t.Id == id && t.TeamId == ctx.TeamId);
        if (template == null) return (false, "Template not found.");
        db.TaskTemplates.Remove(template);
        await db.SaveChangesAsync();
        return (true, null);
    }
}
