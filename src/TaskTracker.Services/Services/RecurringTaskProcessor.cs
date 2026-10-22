using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class RecurringTaskProcessor(AppDbContext db, ITaskService tasks, ILogger<RecurringTaskProcessor> logger)
{
    public async Task<int> ProcessDueAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var definitions = await db.RecurringTaskDefinitions
            .Where(d => d.IsActive && d.NextRunAt <= utcNow && (!d.EndsAt.HasValue || d.NextRunAt.Date <= d.EndsAt.Value.Date))
            .OrderBy(d => d.NextRunAt).Take(25).ToListAsync(ct);
        var created = 0;
        foreach (var definition in definitions)
        {
            var currentDefinition = definition;
            var scheduledFor = currentDefinition.NextRunAt;
            var run = new RecurringTaskRun { RecurringTaskDefinitionId = currentDefinition.Id, ScheduledFor = scheduledFor };
            db.RecurringTaskRuns.Add(run);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                continue;
            }

            var template = await db.TaskTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == currentDefinition.TaskTemplateId && t.TeamId == currentDefinition.TeamId, ct);
            if (template == null)
            {
                await RemoveFailedRunAsync(run.Id, ct);
                currentDefinition = await db.RecurringTaskDefinitions.FindAsync([currentDefinition.Id], ct) ?? currentDefinition;
                currentDefinition.IsActive = false;
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Paused recurring definition {DefinitionId}; its template is unavailable.", currentDefinition.Id);
                continue;
            }
            var owner = currentDefinition.OwnerUserId.HasValue ? await db.AppUsers.FindAsync([currentDefinition.OwnerUserId.Value], ct) : null;
            var ctx = new UserCtx(0, "Scheduler", "scheduler", true, "", currentDefinition.TeamId);
            var save = await tasks.SaveAsync(new TaskItem {
                Goal = currentDefinition.Goal, Owner = owner?.DisplayName, Status = "New", ItemType = template.ItemType,
                Priority = template.Priority, Weight = template.Estimate, EstimateUnit = "Hours",
                Notes = template.Notes, Labels = template.Labels, AcceptanceCriteria = template.AcceptanceCriteria, RowVersion = []
            }, ctx, false);
            if (!save.Success)
            {
                await RemoveFailedRunAsync(run.Id, ct);
                logger.LogError("Recurring definition {DefinitionId} failed to create task: {Error}", currentDefinition.Id, save.Error);
                continue;
            }
            var seeds = await db.TaskTemplateChecklistItems.Where(i => i.TaskTemplateId == template.Id).OrderBy(i => i.SortOrder).Select(i => i.Text).ToListAsync(ct);
            foreach (var seed in seeds) await tasks.AddChecklistItemAsync(save.TaskId, seed, ctx);
            run = await db.RecurringTaskRuns.FindAsync([run.Id], ct) ?? run;
            run.TaskId = save.TaskId;
            currentDefinition = await db.RecurringTaskDefinitions.FindAsync([currentDefinition.Id], ct) ?? currentDefinition;
            currentDefinition.LastRunAt = utcNow;
            currentDefinition.NextRunAt = currentDefinition.Frequency == "Monthly" ? scheduledFor.AddMonths(1) : scheduledFor.AddDays(7);
            if (currentDefinition.EndsAt.HasValue && currentDefinition.NextRunAt.Date > currentDefinition.EndsAt.Value.Date) currentDefinition.IsActive = false;
            await db.SaveChangesAsync(ct);
            created++;
        }
        return created;
    }

    private async Task RemoveFailedRunAsync(long id, CancellationToken ct)
    {
        var run = await db.RecurringTaskRuns.FindAsync([id], ct);
        if (run != null) { db.RecurringTaskRuns.Remove(run); await db.SaveChangesAsync(ct); }
    }
}

public sealed class RecurringTaskBackgroundService(IServiceScopeFactory scopeFactory, ILogger<RecurringTaskBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<RecurringTaskProcessor>();
                await processor.ProcessDueAsync(DateTime.UtcNow, stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Recurring task processing failed.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
