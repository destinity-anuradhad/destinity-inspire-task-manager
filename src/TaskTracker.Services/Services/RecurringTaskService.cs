using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class RecurringTaskService(AppDbContext db) : IRecurringTaskService
{
    public Task<List<RecurringTaskDefinition>> GetForTeamAsync(int? teamId) => db.RecurringTaskDefinitions.AsNoTracking()
        .Where(d => d.TeamId == teamId).OrderBy(d => d.NextRunAt).ToListAsync();

    public async Task<(bool Success, string? Error)> CreateAsync(RecurringTaskDefinition definition, UserCtx ctx)
    {
        if (!ctx.IsTeamAdmin) return (false, "You do not have permission to manage recurring work.");
        if (!ctx.TeamId.HasValue) return (false, "Select a team before creating recurring work.");
        if (string.IsNullOrWhiteSpace(definition.Goal)) return (false, "Task title is required.");
        if (definition.Frequency is not ("Weekly" or "Monthly")) return (false, "Frequency must be Weekly or Monthly.");
        if (definition.EndsAt.HasValue && definition.EndsAt.Value.Date < definition.NextRunAt.Date) return (false, "End date cannot be before the first run.");
        if (!await db.TaskTemplates.AnyAsync(t => t.Id == definition.TaskTemplateId && t.TeamId == ctx.TeamId)) return (false, "Template not found for the active team.");
        if (definition.OwnerUserId.HasValue && !await db.TeamMembers.AnyAsync(m => m.TeamId == ctx.TeamId && m.UserId == definition.OwnerUserId)) return (false, "Owner must belong to the active team.");
        db.RecurringTaskDefinitions.Add(new RecurringTaskDefinition {
            TeamId = ctx.TeamId.Value, TaskTemplateId = definition.TaskTemplateId, Goal = definition.Goal.Trim(),
            OwnerUserId = definition.OwnerUserId, Frequency = definition.Frequency, NextRunAt = definition.NextRunAt,
            EndsAt = definition.EndsAt?.Date, IsActive = true, CreatedAt = DateTime.UtcNow, CreatedBy = ctx.DisplayName
        });
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleAsync(int id, bool isActive, UserCtx ctx)
    {
        if (!ctx.IsTeamAdmin) return (false, "You do not have permission to manage recurring work.");
        var definition = await db.RecurringTaskDefinitions.FirstOrDefaultAsync(d => d.Id == id && d.TeamId == ctx.TeamId);
        if (definition == null) return (false, "Recurring definition not found.");
        definition.IsActive = isActive;
        await db.SaveChangesAsync();
        return (true, null);
    }
}
