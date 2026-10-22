using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using TaskTracker.Data;

namespace TaskTracker.Services;

/// <summary>Posts one chat reminder for each unfinished task due today.</summary>
public sealed class DueTaskReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<DueTaskReminderBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
                await PostDueRemindersAsync(db, chat, DateTime.Today, stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Daily due-task chat reminders failed.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private static async Task PostDueRemindersAsync(
        AppDbContext db, IChatService chat, DateTime today, CancellationToken ct)
    {
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => !t.IsDeleted && t.TeamId.HasValue
                && t.EndDate >= today && t.EndDate < today.AddDays(1))
            .ToListAsync(ct);
        var dueTasks = tasks.Where(t => !TaskStatusRules.IsDone(t)).ToList();
        if (dueTasks.Count == 0) return;

        var teamIds = dueTasks.Select(t => t.TeamId!.Value).Distinct().ToList();
        var adminMemberships = await db.TeamMembers.AsNoTracking()
            .Where(m => teamIds.Contains(m.TeamId) && m.TeamRole == "TeamAdmin")
            .Select(m => new { m.TeamId, m.UserId }).ToListAsync(ct);
        var allUserIds = adminMemberships.Select(m => m.UserId)
            .Concat(await db.TaskOwners.AsNoTracking()
                .Where(o => dueTasks.Select(t => t.Id).Contains(o.TaskId))
                .Select(o => o.UserId).Distinct().ToListAsync(ct))
            .Distinct().ToList();
        var userNames = await db.AppUsers.AsNoTracking()
            .Where(u => allUserIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName }).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var ownerIdsByTask = await db.TaskOwners.AsNoTracking()
            .Where(o => dueTasks.Select(t => t.Id).Contains(o.TaskId))
            .GroupBy(o => o.TaskId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(o => o.UserId).ToList(), ct);
        var adminsByTeam = adminMemberships.GroupBy(m => m.TeamId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.UserId).Distinct().ToList());
        var dateKey = today.ToString("yyyy-MM-dd");

        foreach (var task in dueTasks)
        {
            ct.ThrowIfCancellationRequested();
            var teamId = task.TeamId!.Value;
            var metadata = new { type = "due-reminder", date = dateKey, taskId = task.Id, url = $"/Tasks?highlight={task.Id}", link = "Open task" };
            var marker = JsonSerializer.Serialize(metadata);
            if (await db.ChatMessages.AsNoTracking().AnyAsync(m => m.TeamId == teamId
                && m.Kind == "event" && m.MetaJson == marker, ct)) continue;

            var mentions = new List<string>();
            if (ownerIdsByTask.TryGetValue(task.Id, out var ownerIds))
                mentions.AddRange(ownerIds.Where(userNames.ContainsKey).Select(id => userNames[id]));
            if (adminsByTeam.TryGetValue(teamId, out var adminIds))
                mentions.AddRange(adminIds.Where(userNames.ContainsKey).Select(id => userNames[id]));
            var mentionText = string.Join(" ", mentions
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => "@@" + n));
            var assignment = string.IsNullOrEmpty(mentionText) ? "Team" : mentionText;
            var title = (task.Goal ?? "Untitled task").Replace("\r", " ").Replace("\n", " ");
            var body = $"⏰ Due today: {assignment} — task #{task.Id} “{title}” is due today.";
            await chat.PostEventAsync(teamId, null, "Task Tracker", body, metadata);
        }
    }
}
