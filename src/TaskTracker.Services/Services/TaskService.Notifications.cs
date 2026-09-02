using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text;
using TaskTracker.Data;
using TaskTracker.Hubs;
using TaskTracker.Models;

namespace TaskTracker.Services;

public partial class TaskService
{
    // ── Email template helpers ─────────────────────────────────────────────────

    private static string HtmlEnc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    private string EmailHtml(string heading, string body, int taskId, string ctaLabel,
        string toName = "", string notifyType = "default", string? teamName = null)
    {
        var appUrl  = (_config["AppUrl"] ?? "https://teams.destinity.lk").TrimEnd('/');
        var taskUrl = $"{appUrl}/Tasks?highlight={taskId}";

        var (accent, badge) = notifyType switch {
            "assigned"        => ("#059669", "Task Assignment"),
            "mention"         => ("#7c3aed", "&#64; Mention"),
            "comment"         => ("#2563eb", "New Comment"),
            "comment-watcher" => ("#0891b2", "Watched Task"),
            "status"          => ("#d97706", "Status Update"),
            "blocked"         => ("#dc2626", "Task Blocked"),
            _                 => ("#094f70", "Notification"),
        };

        var greeting = string.IsNullOrEmpty(toName) ? "" :
            $"<p style='margin:0 0 22px;color:#71717a;font-size:14px;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Arial,sans-serif;'>Hi <strong style='color:#09090b;font-weight:600;'>{HtmlEnc(toName)}</strong>,</p>";

        var teamRow = string.IsNullOrEmpty(teamName) ? "" :
            $"<p style='margin:5px 0 0;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Arial,sans-serif;'><span style='display:inline-block;background:rgba(255,255,255,0.10);color:rgba(255,255,255,0.75);font-size:10px;font-weight:500;padding:3px 10px 3px 8px;border-radius:4px;letter-spacing:0.01em;'>&#9670;&nbsp; {HtmlEnc(teamName)}</span></p>";

        return $@"<!DOCTYPE html>
<html lang='en'>
<head>
  <meta charset='UTF-8'/>
  <meta name='viewport' content='width=device-width,initial-scale=1.0'/>
</head>
<body style='margin:0;padding:0;background:#f4f4f5;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Arial,sans-serif;'>
  <table width='100%' cellpadding='0' cellspacing='0' border='0' style='background:#f4f4f5;'>
    <tr><td align='center' style='padding:36px 16px 48px;'>
      <table width='600' cellpadding='0' cellspacing='0' border='0' style='max-width:600px;width:100%;'>
        <tr><td style='background:#09090b;border-radius:16px 16px 0 0;padding:20px 28px 22px;'>
          <table width='100%' cellpadding='0' cellspacing='0' border='0'><tr>
            <td style='vertical-align:middle;'>
              <p style='margin:0;color:#fafafa;font-size:15px;font-weight:700;letter-spacing:-0.3px;'>Destinity Inspire</p>
              <p style='margin:3px 0 0;color:rgba(255,255,255,0.32);font-size:9px;font-weight:500;text-transform:uppercase;letter-spacing:0.16em;'>Task Manager &nbsp;&middot;&nbsp; Scienter PMS</p>
              {teamRow}
            </td>
            <td align='right' style='vertical-align:top;padding-top:2px;'>
              <span style='display:inline-block;background:{accent};color:#ffffff;font-size:9.5px;font-weight:700;text-transform:uppercase;letter-spacing:0.1em;padding:5px 12px;border-radius:6px;'>{badge}</span>
            </td>
          </tr></table>
        </td></tr>
        <tr><td style='background:{accent};height:2px;font-size:0;line-height:0;'>&nbsp;</td></tr>
        <tr><td style='background:#ffffff;padding:36px 28px 32px;border-left:1px solid #e4e4e7;border-right:1px solid #e4e4e7;'>
          {greeting}
          <h2 style='margin:0 0 16px;color:#09090b;font-size:22px;font-weight:700;line-height:1.28;letter-spacing:-0.5px;'>{heading}</h2>
          <div style='margin:0 0 28px;color:#3f3f46;font-size:14px;line-height:1.7;'>{body}</div>
          <table cellpadding='0' cellspacing='0' border='0'>
            <tr><td style='background:{accent};border-radius:8px;'>
              <a href='{taskUrl}' target='_blank'
                 style='display:inline-block;padding:12px 24px;color:#ffffff;font-size:13.5px;font-weight:600;text-decoration:none;letter-spacing:0.01em;'>
                {ctaLabel} &nbsp;&#8594;
              </a>
            </td></tr>
          </table>
        </td></tr>
        <tr><td style='background:#fafafa;border:1px solid #e4e4e7;border-top:none;border-radius:0 0 16px 16px;padding:16px 28px;'>
          <p style='margin:0;color:#a1a1aa;font-size:11px;line-height:1.9;'>
            Sent by <strong style='color:#71717a;'>Scienter Task Manager</strong> because you are involved with this task.&nbsp;
            <a href='{appUrl}/Profile/Index' style='color:{accent};text-decoration:none;font-weight:600;'>Manage preferences</a>
          </p>
        </td></tr>
        <tr><td style='padding:14px 0 2px;text-align:center;'>
          <p style='margin:0;color:#d4d4d8;font-size:10px;letter-spacing:0.04em;'>&copy; 2026 Scienter PMS &nbsp;&middot;&nbsp; Destinity Inspire</p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";
    }

    private static string TaskCard(string? goal, string? priority = null, string? status = null, string? targetDate = null)
    {
        var enc   = (string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        var chips = new StringBuilder();
        if (!string.IsNullOrEmpty(priority))
            chips.Append($"<span style='display:inline-block;background:#fef9c3;color:#713f12;border:1px solid #fde68a;border-radius:4px;font-size:10.5px;font-weight:600;padding:2px 9px;margin-right:6px;'>{enc(priority)}</span>");
        if (!string.IsNullOrEmpty(status))
            chips.Append($"<span style='display:inline-block;background:#eff6ff;color:#1d4ed8;border:1px solid #bfdbfe;border-radius:4px;font-size:10.5px;font-weight:600;padding:2px 9px;margin-right:6px;'>{enc(status)}</span>");
        if (!string.IsNullOrEmpty(targetDate))
            chips.Append($"<span style='display:inline-block;color:#71717a;font-size:10.5px;padding:2px 0;font-family:SF Mono,Fira Code,Consolas,monospace;'>Due {enc(targetDate)}</span>");
        var metaRow = chips.Length > 0 ? $"<p style='margin:12px 0 0;line-height:1;'>{chips}</p>" : "";
        return $@"<table width='100%' cellpadding='0' cellspacing='0' border='0' style='margin:0 0 4px;'>
  <tr><td style='background:#fafafa;border:1px solid #e4e4e7;border-left:3px solid #059669;border-radius:0 10px 10px 0;padding:16px 18px;'>
    <p style='margin:0 0 2px;color:#a1a1aa;font-size:9px;font-weight:600;text-transform:uppercase;letter-spacing:0.14em;'>Task</p>
    <p style='margin:0;color:#09090b;font-size:15px;font-weight:700;line-height:1.4;letter-spacing:-0.2px;'>{enc(goal)}</p>
    {metaRow}
  </td></tr>
</table>";
    }

    private static string CommentQuote(string comment, string taskGoal, string commenter = "",
        string accentColor = "#094f70", string accentBg = "#fafafa")
    {
        var enc = (string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        var commenterRow = string.IsNullOrEmpty(commenter) ? "" :
            $"<p style='margin:0 0 10px;color:{accentColor};font-size:10px;font-weight:700;text-transform:uppercase;letter-spacing:0.12em;'>{enc(commenter)} wrote</p>";
        return $@"<table width='100%' cellpadding='0' cellspacing='0' border='0' style='margin:0 0 6px;'>
  <tr><td style='background:#fafafa;border:1px solid #e4e4e7;border-radius:10px;padding:6px 14px;'>
    <p style='margin:0;color:#a1a1aa;font-size:9px;font-weight:600;text-transform:uppercase;letter-spacing:0.14em;'>Task</p>
    <p style='margin:2px 0 0;color:#3f3f46;font-size:13px;font-weight:600;letter-spacing:-0.1px;'>{enc(taskGoal)}</p>
  </td></tr>
</table>
<table width='100%' cellpadding='0' cellspacing='0' border='0'>
  <tr><td style='background:{accentBg};border:1px solid #e4e4e7;border-left:3px solid {accentColor};border-radius:0 10px 10px 0;padding:14px 16px;'>
    {commenterRow}
    <p style='margin:0;color:#18181b;font-size:14px;line-height:1.7;'>{enc(comment)}</p>
  </td></tr>
</table>";
    }

    private static string Csv(string? s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";

    // ── Notification helpers ────────────────────────────────────────────────────

    private async Task PushNotifBadgeAsync(IEnumerable<int> userIds)
    {
        foreach (var uid in userIds)
            await _hub.Clients.Group("user-" + uid).SendAsync("NewNotification");
    }

    private async Task<List<AppUser>> GetWatcherUsersAsync(int taskId) =>
        await _db.TaskWatchers
            .Where(w => w.TaskId == taskId)
            .Join(_db.AppUsers, w => w.UserId, u => u.Id, (w, u) => u)
            .ToListAsync();

    private async Task NotifyAssignedAsync(TaskItem task, string? previousOwner, UserCtx ctx)
    {
        try
        {
            var teamName   = await GetTeamNameAsync(task.TeamId);
            var prevOwners = SplitOwners(previousOwner).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newOwners  = SplitOwners(task.Owner).Where(o => !prevOwners.Contains(o)).ToList();
            var pushIds    = new List<int>();
            foreach (var ownerName in newOwners)
            {
                var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.DisplayName == ownerName);
                if (user == null || string.Equals(user.Username, ctx.Username, StringComparison.OrdinalIgnoreCase)) continue;
                if (user.NotifyOnAssigned)
                {
                    _db.Notifications.Add(new Notification {
                        UserId = user.Id, TaskId = task.Id,
                        Message = $"You were assigned to: {task.Goal}",
                        CreatedAt = DateTime.Now
                    });
                    pushIds.Add(user.Id);
                }
                if (user.NotifyOnAssigned && !string.IsNullOrWhiteSpace(user.Email))
                    await _emailQueue.EnqueueAsync(new EmailQueueItem(
                        user.Email, user.DisplayName,
                        $"[Task Manager] Assigned: {task.Goal}",
                        EmailHtml("You've been assigned to a new task.",
                            TaskCard(task.Goal, task.Priority, task.Status, task.TargetDate?.ToString("yyyy-MM-dd")),
                            task.Id, "Open Task", user.DisplayName, "assigned", teamName)));
            }
            await _db.SaveChangesAsync();
            await PushNotifBadgeAsync(pushIds);
        }
        catch (Exception ex) { _log.LogError(ex, "NotifyAssignedAsync failed for task {TaskId}", task.Id); }
    }

    private async Task NotifyCommentAsync(TaskItem task, string comment, string commenter,
        List<AppUser> allUsers, List<AppUser> mentioned, List<AppUser> watchers, string? teamName)
    {
        try
        {
            var emailed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var user in mentioned)
            {
                emailed.Add(user.DisplayName);
                if (string.IsNullOrWhiteSpace(user.Email)) continue;
                await _emailQueue.EnqueueAsync(new EmailQueueItem(
                    user.Email, user.DisplayName,
                    $"[Task Manager] {commenter} mentioned you on: {task.Goal}",
                    EmailHtml($"{HtmlEnc(commenter)} mentioned you in a comment.",
                        CommentQuote(comment, task.Goal ?? "", commenter, "#7c3aed", "#faf5ff"),
                        task.Id, "View Comment", user.DisplayName, "mention", teamName)));
            }
            foreach (var ownerName in SplitOwners(task.Owner))
            {
                if (string.Equals(ownerName, commenter, StringComparison.OrdinalIgnoreCase)) continue;
                if (emailed.Contains(ownerName)) continue;
                var user = allUsers.FirstOrDefault(u => u.DisplayName == ownerName);
                if (user == null || !user.NotifyOnComment || string.IsNullOrWhiteSpace(user.Email)) continue;
                emailed.Add(ownerName);
                await _emailQueue.EnqueueAsync(new EmailQueueItem(
                    user.Email, user.DisplayName,
                    $"[Task Manager] New comment on: {task.Goal}",
                    EmailHtml($"{HtmlEnc(commenter)} commented on your task.",
                        CommentQuote(comment, task.Goal ?? "", commenter, "#2563eb", "#eff6ff"),
                        task.Id, "View Comment", user.DisplayName, "comment", teamName)));
            }
            foreach (var user in watchers)
            {
                if (emailed.Contains(user.DisplayName) || string.IsNullOrWhiteSpace(user.Email)) continue;
                await _emailQueue.EnqueueAsync(new EmailQueueItem(
                    user.Email, user.DisplayName,
                    $"[Task Manager] New comment on watched task: {task.Goal}",
                    EmailHtml($"{HtmlEnc(commenter)} commented on a task you're watching.",
                        CommentQuote(comment, task.Goal ?? "", commenter, "#0891b2", "#ecfeff"),
                        task.Id, "View Comment", user.DisplayName, "comment-watcher", teamName)));
            }
        }
        catch (Exception ex) { _log.LogError(ex, "NotifyCommentAsync failed for task {TaskId}", task.Id); }
    }

    private async Task NotifyWatchersStatusAsync(TaskItem task, string? oldStatus, string? newStatus, UserCtx ctx)
    {
        try
        {
            var actor    = ctx.DisplayName;
            var teamName = await GetTeamNameAsync(task.TeamId);
            var targets  = (await GetWatcherUsersAsync(task.Id))
                .Where(u => !string.Equals(u.DisplayName, actor, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (targets.Count == 0) return;
            foreach (var u in targets)
                _db.Notifications.Add(new Notification {
                    UserId = u.Id, TaskId = task.Id,
                    Message = $"Watched task '{task.Goal}': status '{oldStatus}' → '{newStatus}'",
                    CreatedAt = DateTime.Now
                });
            await _db.SaveChangesAsync();
            await PushNotifBadgeAsync(targets.Select(u => u.Id));
            var statusHtml = $@"<table width='100%' cellpadding='0' cellspacing='0' border='0' style='margin:0 0 4px;'>
  <tr><td style='background:#fafafa;border:1px solid #e4e4e7;border-radius:10px;padding:14px 18px;'>
    <p style='margin:0 0 2px;color:#a1a1aa;font-size:9px;font-weight:600;text-transform:uppercase;letter-spacing:0.14em;'>Task</p>
    <p style='margin:0 0 14px;color:#09090b;font-size:14px;font-weight:700;letter-spacing:-0.2px;'>{HtmlEnc(task.Goal)}</p>
    <table cellpadding='0' cellspacing='0' border='0'><tr>
      <td style='background:#fee2e2;color:#b91c1c;border:1px solid #fca5a5;border-radius:5px;padding:4px 12px;font-size:11.5px;font-weight:700;'>{HtmlEnc(oldStatus)}</td>
      <td style='color:#a1a1aa;padding:0 12px;font-size:18px;font-weight:300;'>&#8594;</td>
      <td style='background:#dcfce7;color:#15803d;border:1px solid #86efac;border-radius:5px;padding:4px 12px;font-size:11.5px;font-weight:700;'>{HtmlEnc(newStatus)}</td>
    </tr></table>
  </td></tr>
</table>";
            await SendWatcherEmailsAsync(task.Id, task.Goal, targets, actor, statusHtml, teamName);
        }
        catch (Exception ex) { _log.LogError(ex, "NotifyWatchersStatusAsync failed for task {TaskId}", task.Id); }
    }

    private async Task SendWatcherEmailsAsync(int taskId, string? goal, List<AppUser> users,
        string actor, string detailHtml, string? teamName)
    {
        try
        {
            foreach (var u in users)
            {
                if (string.IsNullOrWhiteSpace(u.Email)) continue;
                await _emailQueue.EnqueueAsync(new EmailQueueItem(
                    u.Email, u.DisplayName,
                    $"[Task Manager] Status update on watched task: {goal}",
                    EmailHtml($"{HtmlEnc(actor)} updated a task you're watching.",
                        $"<p style='margin:0 0 14px;color:#52525b;font-size:14px;'>The status of a watched task has changed:</p>{detailHtml}",
                        taskId, "View Task", u.DisplayName, "status", teamName)));
            }
        }
        catch (Exception ex) { _log.LogError(ex, "SendWatcherEmailsAsync failed for task {TaskId}", taskId); }
    }

    private async Task NotifyBlockedAsync(int fromId, int toId)
    {
        try
        {
            var blocker = await _db.Tasks.Select(t => new { t.Id, t.Goal }).FirstOrDefaultAsync(t => t.Id == fromId);
            var blocked = await _db.Tasks.Select(t => new { t.Id, t.Goal, t.Owner, t.TeamId }).FirstOrDefaultAsync(t => t.Id == toId);
            if (blocked == null) return;
            var blockedTeamName = await GetTeamNameAsync(blocked.TeamId);
            bool saved = false;
            foreach (var ownerName in SplitOwners(blocked.Owner))
            {
                var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.DisplayName == ownerName);
                if (user == null || !user.NotifyOnBlocked) continue;
                _db.Notifications.Add(new Notification {
                    UserId = user.Id, TaskId = blocked.Id,
                    Message = $"Your task \"{blocked.Goal}\" is blocked by #{blocker?.Id}",
                    CreatedAt = DateTime.Now
                });
                saved = true;
                if (!string.IsNullOrWhiteSpace(user.Email))
                {
                    var blockedBody = $@"<p style='margin:0 0 18px;color:#334155;font-size:13.5px;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Arial,sans-serif;'>One of your tasks cannot proceed until the blocker below is resolved.</p>
<table width='100%' cellpadding='0' cellspacing='0' border='0' style='margin:0 0 10px;'>
  <tr><td style='background:#fef2f2;border:1px solid #fecaca;border-left:4px solid #dc2626;border-radius:0 10px 10px 0;padding:16px 20px;'>
    <p style='margin:0 0 4px;color:#dc2626;font-size:9.5px;font-weight:700;text-transform:uppercase;letter-spacing:0.12em;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Arial,sans-serif;'>&#9888; Blocked Task</p>
    <p style='margin:0;color:#0f172a;font-size:14px;font-weight:700;line-height:1.4;font-family:Georgia,Times New Roman,Times,serif;'>#{blocked.Id} &nbsp;{HtmlEnc(blocked.Goal)}</p>
  </td></tr>
</table>
<table width='100%' cellpadding='0' cellspacing='0' border='0'>
  <tr><td style='background:#f8fafc;border:1px solid #e2e8f0;border-left:4px solid #94a3b8;border-radius:0 10px 10px 0;padding:16px 20px;'>
    <p style='margin:0 0 4px;color:#64748b;font-size:9.5px;font-weight:700;text-transform:uppercase;letter-spacing:0.12em;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Arial,sans-serif;'>Blocker</p>
    <p style='margin:0;color:#334155;font-size:14px;font-weight:700;line-height:1.4;font-family:Georgia,Times New Roman,Times,serif;'>#{blocker?.Id} &nbsp;{HtmlEnc(blocker?.Goal ?? "")}</p>
  </td></tr>
</table>";
                    await _emailQueue.EnqueueAsync(new EmailQueueItem(
                        user.Email, user.DisplayName,
                        $"[Task Manager] Task blocked: {blocked.Goal}",
                        EmailHtml("Your task has been marked as Blocked.", blockedBody,
                            blocked.Id, "View Blocked Task", user.DisplayName, "blocked", blockedTeamName)));
                }
            }
            if (saved) await _db.SaveChangesAsync();
        }
        catch (Exception ex) { _log.LogError(ex, "NotifyBlockedAsync failed: blocker {From} → blocked {To}", fromId, toId); }
    }
}
