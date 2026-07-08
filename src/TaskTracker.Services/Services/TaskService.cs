using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using TaskTracker.Data;
using TaskTracker.Hubs;
using TaskTracker.Models;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services;

public class TaskService(AppDbContext db, IConfiguration config, IHubContext<TaskHub> hub, IEmailService email) : ITaskService
{
    private readonly AppDbContext         _db     = db;
    private readonly IConfiguration      _config = config;
    private readonly IHubContext<TaskHub> _hub    = hub;
    private readonly IEmailService       _email  = email;

    // ── Permission / scope helpers ─────────────────────────────────────────────

    private static IEnumerable<string> SplitOwners(string? owner) =>
        (owner ?? "").Split(',').Select(o => o.Trim()).Where(o => o.Length > 0);

    private static bool CanModify(TaskItem task, UserCtx ctx)
    {
        if (ctx.IsAdmin) return true;
        if (string.IsNullOrWhiteSpace(task.Owner)) return true;
        var me = ctx.DisplayName;
        return SplitOwners(task.Owner).Contains(me, StringComparer.OrdinalIgnoreCase)
            || string.Equals(task.Reporter,  me, StringComparison.OrdinalIgnoreCase)
            || string.Equals(task.CreatedBy, me, StringComparison.OrdinalIgnoreCase);
    }

    private IQueryable<TaskItem> TeamTasks(int? teamId)
    {
        var q = _db.Tasks.AsQueryable();
        if (teamId.HasValue) q = q.Where(t => t.TeamId == teamId);
        return q;
    }

    private async Task<TaskItem?> FindTaskInTeam(int id, int? teamId)
    {
        var task = await _db.Tasks.FindAsync(id);
        if (task == null) return null;
        return teamId.HasValue && task.TeamId != teamId ? null : task;
    }

    private async Task<Sprint?> FindSprintInTeam(int id, int? teamId)
    {
        var sprint = await _db.Sprints.FindAsync(id);
        if (sprint == null) return null;
        return teamId.HasValue && sprint.TeamId != teamId ? null : sprint;
    }

    private IQueryable<Sprint> TeamSprints(int? teamId)
    {
        var q = _db.Sprints.AsQueryable();
        if (teamId.HasValue) q = q.Where(s => s.TeamId == teamId);
        return q;
    }

    private async Task<string?> GetTeamNameAsync(int? teamId) =>
        teamId.HasValue
            ? await _db.Teams.Where(t => t.Id == teamId.Value).Select(t => t.Name).FirstOrDefaultAsync()
            : null;

    private async Task LogAsync(UserCtx ctx, string action, string details,
        int? taskId = null, string? taskGoal = null, string? project = null)
    {
        try
        {
            int? teamId = null;
            if (taskId.HasValue)
                teamId = await _db.Tasks.Where(t => t.Id == taskId.Value)
                    .Select(t => (int?)t.TeamId).FirstOrDefaultAsync();
            _db.ActivityLog.Add(new ActivityLog {
                TaskId    = taskId,
                TeamId    = teamId,
                TaskGoal  = taskGoal  ?? "",
                Project   = project   ?? "",
                ChangedBy = ctx.DisplayName,
                Action    = action,
                Category  = "Task",
                Details   = details,
                IpAddress = ctx.IpAddress,
                UserAgent = ctx.UserAgent,
                ChangedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }
        catch { }
    }

    // ── Cascade helpers ────────────────────────────────────────────────────────

    private static bool IsDoneForCascade(string? itemType, string? status) =>
        itemType switch {
            "Task"    => status == "Done",
            "Bug"     => status is "Resolved" or "Closed",
            "Story"   => status is "Resolved" or "Closed",
            "Feature" => status is "Resolved" or "Closed",
            _         => status is "Done" or "Resolved" or "Closed"
        };

    private static bool IsActiveCascade(string? status) =>
        status is "In Progress" or "Active" or "Resolved" or "Done" or "Closed";

    private static List<TaskItem> GetAllDescendants(List<TaskItem> all, int parentId)
    {
        var result  = new List<TaskItem>();
        var queue   = new Queue<int>();
        var visited = new HashSet<int> { parentId };
        queue.Enqueue(parentId);
        while (queue.Count > 0)
        {
            var current  = queue.Dequeue();
            var children = all.Where(t => t.ParentId == current).ToList();
            result.AddRange(children);
            foreach (var child in children)
            {
                if (visited.Add(child.Id)) queue.Enqueue(child.Id);
            }
        }
        return result;
    }

    private Task<int> CountOpenChildrenAsync(int parentId) =>
        _db.Tasks.CountAsync(c => c.ParentId == parentId && !c.IsDeleted &&
                             !((c.ItemType == "Task"    && c.Status == "Done") ||
                               (c.ItemType == "Bug"     && (c.Status == "Resolved" || c.Status == "Closed")) ||
                               (c.ItemType == "Story"   && (c.Status == "Resolved" || c.Status == "Closed")) ||
                               (c.ItemType == "Feature" && (c.Status == "Resolved" || c.Status == "Closed")) ||
                               (c.ItemType == null      && (c.Status == "Done" || c.Status == "Resolved" || c.Status == "Closed"))));

    private async Task CascadeParentStatusAsync(int? parentId, UserCtx ctx)
    {
        if (parentId == null) return;
        var parent = await _db.Tasks.FindAsync(parentId.Value);
        if (parent == null) return;

        var children = await _db.Tasks.Where(t => t.ParentId == parentId && !t.IsDeleted).ToListAsync();
        if (!children.Any()) return;

        string? autoStatus = null;
        if (children.All(c => IsDoneForCascade(c.ItemType, c.Status)))
        {
            autoStatus = parent.ItemType switch {
                "Story"   => "Resolved",
                "Feature" => "Closed",
                "Epic"    => "Closed",
                _         => null
            };
        }
        else if (parent.Status == "New" && children.Any(c => IsActiveCascade(c.Status)))
        {
            autoStatus = "Active";
        }

        if (autoStatus == null || autoStatus == parent.Status) return;

        parent.Status = autoStatus;
        await _db.SaveChangesAsync();

        await LogAsync(ctx, "Auto-Updated",
            autoStatus == "Active"
                ? "Status auto-set to 'Active' — a child item was started"
                : $"Status auto-set to '{autoStatus}' via child completion cascade",
            parent.Id, parent.Goal, parent.Project);

        await _hub.Clients.Group("team-" + (parent.TeamId?.ToString() ?? "0")).SendAsync("TaskUpdated", new {
            id        = parent.Id,
            goal      = parent.Goal,
            status    = parent.Status,
            priority  = parent.Priority,
            owner     = parent.Owner,
            itemType  = parent.ItemType,
            sprintId  = parent.SprintId,
            updatedBy = "auto-cascade"
        });

        await CascadeParentStatusAsync(parent.ParentId, ctx);
    }

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
            foreach (var ownerName in newOwners)
            {
                var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.DisplayName == ownerName);
                if (user == null || string.Equals(user.Username, ctx.Username, StringComparison.OrdinalIgnoreCase)) continue;
                if (user.NotifyOnAssigned)
                    _db.Notifications.Add(new Notification {
                        UserId = user.Id, TaskId = task.Id,
                        Message = $"You were assigned to: {task.Goal}",
                        CreatedAt = DateTime.Now
                    });
                if (user.NotifyOnAssigned && !string.IsNullOrWhiteSpace(user.Email))
                    _ = _email.SendAsync(user.Email, user.DisplayName,
                        $"[Task Manager] Assigned: {task.Goal}",
                        EmailHtml("You've been assigned to a new task.",
                            TaskCard(task.Goal, task.Priority, task.Status, task.TargetDate),
                            task.Id, "Open Task", user.DisplayName, "assigned", teamName));
            }
            await _db.SaveChangesAsync();
        }
        catch { }
    }

    // Fire-and-forget: must NOT access _db (request scope may be disposed by then).
    // All data needed is passed as parameters from the awaited portion.
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
                await _email.SendAsync(user.Email, user.DisplayName,
                    $"[Task Manager] {commenter} mentioned you on: {task.Goal}",
                    EmailHtml($"{HtmlEnc(commenter)} mentioned you in a comment.",
                        CommentQuote(comment, task.Goal ?? "", commenter, "#7c3aed", "#faf5ff"),
                        task.Id, "View Comment", user.DisplayName, "mention", teamName));
            }
            foreach (var ownerName in SplitOwners(task.Owner))
            {
                if (string.Equals(ownerName, commenter, StringComparison.OrdinalIgnoreCase)) continue;
                if (emailed.Contains(ownerName)) continue;
                var user = allUsers.FirstOrDefault(u => u.DisplayName == ownerName);
                if (user == null || !user.NotifyOnComment || string.IsNullOrWhiteSpace(user.Email)) continue;
                emailed.Add(ownerName);
                await _email.SendAsync(user.Email, user.DisplayName,
                    $"[Task Manager] New comment on: {task.Goal}",
                    EmailHtml($"{HtmlEnc(commenter)} commented on your task.",
                        CommentQuote(comment, task.Goal ?? "", commenter, "#2563eb", "#eff6ff"),
                        task.Id, "View Comment", user.DisplayName, "comment", teamName));
            }
            foreach (var user in watchers)
            {
                if (emailed.Contains(user.DisplayName) || string.IsNullOrWhiteSpace(user.Email)) continue;
                await _email.SendAsync(user.Email, user.DisplayName,
                    $"[Task Manager] New comment on watched task: {task.Goal}",
                    EmailHtml($"{HtmlEnc(commenter)} commented on a task you're watching.",
                        CommentQuote(comment, task.Goal ?? "", commenter, "#0891b2", "#ecfeff"),
                        task.Id, "View Comment", user.DisplayName, "comment-watcher", teamName));
            }
        }
        catch { }
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
            _ = SendWatcherEmailsAsync(task.Id, task.Goal, targets, actor, statusHtml, teamName);
        }
        catch { }
    }

    // Fire-and-forget: must NOT access _db.
    private async Task SendWatcherEmailsAsync(int taskId, string? goal, List<AppUser> users,
        string actor, string detailHtml, string? teamName)
    {
        try
        {
            foreach (var u in users)
            {
                if (string.IsNullOrWhiteSpace(u.Email)) continue;
                await _email.SendAsync(u.Email, u.DisplayName,
                    $"[Task Manager] Status update on watched task: {goal}",
                    EmailHtml($"{HtmlEnc(actor)} updated a task you're watching.",
                        $"<p style='margin:0 0 14px;color:#52525b;font-size:14px;'>The status of a watched task has changed:</p>{detailHtml}",
                        taskId, "View Task", u.DisplayName, "status", teamName));
            }
        }
        catch { }
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
                    await _email.SendAsync(user.Email, user.DisplayName,
                        $"[Task Manager] Task blocked: {blocked.Goal}",
                        EmailHtml("Your task has been marked as Blocked.", blockedBody,
                            blocked.Id, "View Blocked Task", user.DisplayName, "blocked", blockedTeamName));
                }
            }
            if (saved) await _db.SaveChangesAsync();
        }
        catch { }
    }

    // ── ITaskService: query methods ────────────────────────────────────────────

    public async Task<TaskListResult> GetListAsync(TaskListFilters f, int? teamId)
    {
        var now = DateTime.Now;
        var vm = new TaskListViewModel {
            Search   = f.Search,    Project  = f.Project,   Property = f.Property,
            Owner    = f.Owner ?? "", Status = f.Status,    Priority = f.Priority,
            DateFrom = f.DateFrom,  DateTo   = f.DateTo,
            Page     = f.Page,      PageSize = f.PageSize,
            Sort     = f.Sort,      SortDir  = f.SortDir,
            ViewMode = f.View,
            CalYear  = f.CalYear  > 0 ? f.CalYear  : now.Year,
            CalMonth = f.CalMonth > 0 ? f.CalMonth : now.Month,
        };

        var query = _db.Tasks.AsQueryable();
        if (teamId.HasValue) query = query.Where(t => t.TeamId == teamId);

        if (!string.IsNullOrEmpty(vm.Search))
            query = query.Where(t =>
                (t.Goal    ?? "").Contains(vm.Search) || (t.Property ?? "").Contains(vm.Search) ||
                (t.Owner   ?? "").Contains(vm.Search) || (t.Project  ?? "").Contains(vm.Search) ||
                (t.Labels  ?? "").Contains(vm.Search));

        if (!string.IsNullOrEmpty(vm.Project))
            query = vm.Project == "__none__"
                ? query.Where(t => t.Project == "")
                : query.Where(t => t.Project == vm.Project);

        if (!string.IsNullOrEmpty(vm.Property))
            query = query.Where(t => t.Property == vm.Property);

        if (!string.IsNullOrEmpty(vm.Owner))
            query = query.Where(t => (t.Owner ?? "").Contains(vm.Owner));

        query = (vm.Sort, vm.SortDir) switch {
            ("goal",       "asc")  => query.OrderBy(t => t.Goal),
            ("goal",       _)      => query.OrderByDescending(t => t.Goal),
            ("project",    "asc")  => query.OrderBy(t => t.Project),
            ("project",    _)      => query.OrderByDescending(t => t.Project),
            ("property",   "asc")  => query.OrderBy(t => t.Property),
            ("property",   _)      => query.OrderByDescending(t => t.Property),
            ("priority",   "asc")  => query.OrderBy(t => t.Priority),
            ("priority",   _)      => query.OrderByDescending(t => t.Priority),
            ("owner",      "asc")  => query.OrderBy(t => t.Owner),
            ("owner",      _)      => query.OrderByDescending(t => t.Owner),
            ("status",     "asc")  => query.OrderBy(t => t.Status),
            ("status",     _)      => query.OrderByDescending(t => t.Status),
            ("targetDate", "asc")  => query.OrderBy(t => t.TargetDate),
            ("targetDate", _)      => query.OrderByDescending(t => t.TargetDate),
            _                      => query.OrderByDescending(t => t.CreatedAt),
        };

        var allTasks = await query.ToListAsync();

        if (DateTime.TryParse(vm.DateFrom, out var dfrom))
            allTasks = allTasks.Where(t => DateTime.TryParse(t.TargetDate, out var d) && d.Date >= dfrom.Date).ToList();
        if (DateTime.TryParse(vm.DateTo, out var dto))
            allTasks = allTasks.Where(t => DateTime.TryParse(t.TargetDate, out var d) && d.Date <= dto.Date).ToList();

        vm.StatusCounts = allTasks.GroupBy(t => t.Status ?? "").ToDictionary(g => g.Key, g => g.Count());

        if (!string.IsNullOrEmpty(vm.Status))
            allTasks = vm.Status == "__none__"
                ? allTasks.Where(t => t.Status == "").ToList()
                : allTasks.Where(t => t.Status == vm.Status).ToList();

        if (!string.IsNullOrEmpty(vm.Priority))
            allTasks = allTasks.Where(t => t.Priority == vm.Priority).ToList();

        vm.TotalCount = allTasks.Count;

        vm.Tasks = vm.ViewMode is "kanban" or "calendar"
            ? allTasks
            : allTasks.Skip((vm.Page - 1) * vm.PageSize).Take(vm.PageSize).ToList();

        if (vm.ViewMode == "table")
        {
            var pageIds = vm.Tasks.Select(t => t.Id).ToList();
            vm.CommentCounts = await _db.ActivityLog
                .Where(a => a.TaskId.HasValue && pageIds.Contains(a.TaskId.Value) && a.Action == "Comment")
                .GroupBy(a => a.TaskId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Count());
        }

        vm.Projects   = await _db.Projects.OrderBy(p => p.Name).ToListAsync();
        vm.Properties = await _db.Properties.OrderBy(p => p.Name).ToListAsync();

        IQueryable<AppUser> ownerQ = _db.AppUsers.OrderBy(u => u.DisplayName);
        if (teamId.HasValue)
        {
            var memberIds = await _db.TeamMembers.Where(m => m.TeamId == teamId).Select(m => m.UserId).ToListAsync();
            ownerQ = ownerQ.Where(u => memberIds.Contains(u.Id));
        }
        vm.Owners = (await ownerQ.ToListAsync())
            .Select(u => new OwnerItem { Id = u.Id, Name = u.DisplayName, Color = u.Color ?? "#094f70", ImagePath = u.ProfileImagePath })
            .ToList();

        vm.WorkStates = await _db.WorkStates.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        if (!vm.WorkStates.Any())
            vm.WorkStates = TaskListViewModel.AllStatuses
                .Select((s, i) => new WorkState { Id = i, Name = s, Color = "#094f70", SortOrder = i })
                .ToList();

        var sprintQ = _db.Sprints.AsQueryable();
        if (teamId.HasValue) sprintQ = sprintQ.Where(s => s.TeamId == teamId);
        var allSprints = await sprintQ.OrderBy(s => s.Name).ToListAsync();

        TaskTeamViewData? teamViewData = null;
        if (f.View == "team")
            teamViewData = await GetTeamViewDataAsync(now, f.TlFrom, f.TlTo, f.TlMode, vm.CalYear, vm.CalMonth, teamId);

        return new TaskListResult { ViewModel = vm, AllSprints = allSprints, TeamViewData = teamViewData };
    }

    public async Task<TaskTeamViewData> GetTeamViewDataAsync(DateTime now, string? tlfrom, string? tlto,
        string? tlmode, int calYear, int calMonth, int? teamId)
    {
        var tlQuery = _db.Tasks.AsQueryable();
        if (teamId.HasValue) tlQuery = tlQuery.Where(t => t.TeamId == teamId);
        var allForTl = await tlQuery.OrderBy(t => t.Id).ToListAsync();

        DateTime tlFrom, tlTo;
        string resolvedMode;
        if (tlmode == "today")
        {
            tlFrom = tlTo = now.Date;
            resolvedMode = "today";
        }
        else if (!string.IsNullOrEmpty(tlfrom) && DateTime.TryParse(tlfrom, out var fd) &&
                 !string.IsNullOrEmpty(tlto)   && DateTime.TryParse(tlto,   out var td))
        {
            tlFrom = fd <= td ? fd : td;
            tlTo   = fd <= td ? td : fd;
            resolvedMode = "range";
        }
        else if (calYear > 0 && calMonth >= 1 && calMonth <= 12)
        {
            tlFrom = new DateTime(calYear, calMonth, 1);
            tlTo   = tlFrom.AddMonths(1).AddDays(-1);
            resolvedMode = "range";
        }
        else
        {
            int dow = (int)now.DayOfWeek;
            tlFrom = now.Date.AddDays(dow == 0 ? -6 : 1 - dow);
            tlTo   = tlFrom.AddDays(4);
            resolvedMode = "week";
        }

        var appUsers    = await _db.AppUsers.OrderBy(u => u.DisplayName).ToListAsync();
        var regOwners   = appUsers.Select(u => u.DisplayName).Where(n => !string.IsNullOrEmpty(n)).ToList();
        var extraOwners = allForTl
            .Where(t => !string.IsNullOrEmpty(t.Owner))
            .SelectMany(t => t.Owner!.Split(',').Select(o => o.Trim()))
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(o => !regOwners.Any(r => string.Equals(r, o, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var projects   = await _db.Projects.ToListAsync();
        var properties = await _db.Properties.ToListAsync();
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        return new TaskTeamViewData {
            TlFrom           = tlFrom.ToString("yyyy-MM-dd"),
            TlTo             = tlTo.ToString("yyyy-MM-dd"),
            TlMode           = resolvedMode,
            TlOwners         = JsonSerializer.Serialize(regOwners.Concat(extraOwners).ToList()),
            TlProjectColors  = JsonSerializer.Serialize(
                projects.Where(p => !string.IsNullOrEmpty(p.Color))
                        .ToDictionary(p => p.Name ?? "", p => p.Color ?? "#6b7280")),
            TlPropertyColors = JsonSerializer.Serialize(
                properties.Where(p => !string.IsNullOrEmpty(p.Color))
                          .ToDictionary(p => p.Name ?? "", p => p.Color ?? "#6b7280")),
            TlTasks = JsonSerializer.Serialize(allForTl.Select(t => new {
                id                 = t.Id,
                goal               = t.Goal     ?? "",
                owner              = t.Owner    ?? "",
                status             = t.Status   ?? "",
                priority           = t.Priority ?? "",
                itemType           = t.ItemType ?? "",
                project            = t.Project  ?? "",
                property           = t.Property ?? "",
                startDate          = t.StartDate ?? "",
                endDate            = t.EndDate   ?? "",
                weight             = t.Weight,
                swimlane           = t.Swimlane ?? "",
                reporter           = t.Reporter ?? "",
                createdBy          = t.CreatedBy ?? "",
                labels             = t.Labels   ?? "",
                notes              = t.Notes    ?? "",
                acceptanceCriteria = t.AcceptanceCriteria ?? "",
                parentId           = t.ParentId,
                sprintId           = t.SprintId,
            }), opts)
        };
    }

    public async Task<TaskItem?> GetByIdAsync(int id, int? teamId) => await FindTaskInTeam(id, teamId);

    public async Task<IEnumerable<object>> GetCommentsAsync(int taskId, int? teamId)
    {
        if (teamId.HasValue && !await _db.Tasks.AnyAsync(t => t.Id == taskId && t.TeamId == teamId))
            return [];
        var list = await _db.ActivityLog
            .Where(a => a.TaskId == taskId && a.Action == "Comment")
            .OrderBy(a => a.ChangedAt)
            .Select(a => new {
                a.ChangedBy, a.Details,
                changedAt    = a.ChangedAt.ToString("dd MMM yyyy HH:mm"),
                changedAtIso = a.ChangedAt.ToString("o")
            })
            .ToListAsync();
        return list.Cast<object>();
    }

    public async Task<IEnumerable<object>> GetHistoryAsync(int taskId, int? teamId)
    {
        if (teamId.HasValue && !await _db.Tasks.AnyAsync(t => t.Id == taskId && t.TeamId == teamId))
            return [];
        var list = await _db.ActivityLog
            .Where(a => a.TaskId == taskId && a.Action != "Comment")
            .OrderByDescending(a => a.ChangedAt)
            .Select(a => new {
                a.Action, a.Details, a.ChangedBy,
                changedAt    = a.ChangedAt.ToString("dd MMM yyyy HH:mm"),
                changedAtIso = a.ChangedAt.ToString("o")
            })
            .ToListAsync();
        return list.Cast<object>();
    }

    public async Task<object?> GetLinksAsync(int taskId, int? teamId)
    {
        if (teamId.HasValue && !await _db.Tasks.AnyAsync(t => t.Id == taskId && t.TeamId == teamId))
            return null;
        var links = await _db.TaskLinks
            .Where(l => l.FromTaskId == taskId || l.ToTaskId == taskId)
            .ToListAsync();
        var linkedIds = links.SelectMany(l => new[] { l.FromTaskId, l.ToTaskId })
            .Distinct().Where(x => x != taskId).ToList();
        var tasks = await _db.Tasks
            .Where(t => linkedIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Goal, t.Status, t.ItemType })
            .ToDictionaryAsync(t => t.Id);
        return links.Select(l => {
            var isFrom   = l.FromTaskId == taskId;
            var linkedId = isFrom ? l.ToTaskId : l.FromTaskId;
            tasks.TryGetValue(linkedId, out var linked);
            return new {
                id = l.Id, linkType = l.LinkType, isFrom, linkedTaskId = linkedId,
                linkedTaskGoal     = linked?.Goal     ?? "Unknown",
                linkedTaskStatus   = linked?.Status   ?? "",
                linkedTaskItemType = linked?.ItemType ?? ""
            };
        }).ToList<object>();
    }

    public async Task<WatcherResult> GetWatchersAsync(int taskId, int? teamId, string displayName)
    {
        if (teamId.HasValue && !await _db.Tasks.AnyAsync(t => t.Id == taskId && t.TeamId == teamId))
            return new WatcherResult();
        var watchers = await _db.TaskWatchers
            .Where(w => w.TaskId == taskId)
            .Join(_db.AppUsers, w => w.UserId, u => u.Id, (w, u) => new { id = u.Id, name = u.DisplayName })
            .ToListAsync();
        return new WatcherResult {
            Watchers = watchers.Cast<object>().ToList(),
            Watching = watchers.Any(w => w.name == displayName),
            Count    = watchers.Count
        };
    }

    public async Task<IEnumerable<object>> GetAttachmentsAsync(int taskId)
    {
        var list = await _db.TaskAttachments
            .Where(a => a.TaskId == taskId)
            .OrderByDescending(a => a.UploadedAt)
            .Select(a => new {
                a.Id, a.FileName, a.FileSize, a.ContentType, a.UploadedBy,
                uploadedAt = a.UploadedAt.ToString("dd MMM yyyy HH:mm")
            })
            .ToListAsync();
        return list.Cast<object>();
    }

    public async Task<AttachmentDownload?> GetDownloadAsync(int id, int? teamId = null)
    {
        var att = await _db.TaskAttachments.FindAsync(id);
        if (att == null) return null;
        if (teamId.HasValue)
        {
            var parentTask = await _db.Tasks.FindAsync(att.TaskId);
            if (parentTask == null || parentTask.TeamId != teamId) return null;
        }
        var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "attachments",
            att.TaskId.ToString(), att.StoredName);
        if (!File.Exists(path)) return null;
        return new AttachmentDownload {
            FilePath    = path,
            ContentType = att.ContentType ?? "application/octet-stream",
            FileName    = att.FileName
        };
    }

    public async Task<ChildStatsResult> GetChildStatsAsync(int id, int? teamId)
    {
        var all         = await TeamTasks(teamId).ToListAsync();
        var descendants = GetAllDescendants(all, id);

        var leaves = descendants.Where(t => t.ItemType is "Task" or "Bug").ToList();
        if (leaves.Any())
        {
            var done = leaves.Count(t => IsDoneForCascade(t.ItemType, t.Status));
            return new ChildStatsResult {
                Total = leaves.Count, Done = done,
                Pct = (int)Math.Round(100.0 * done / leaves.Count), LeafType = "tasks"
            };
        }

        var direct = descendants.Where(t => t.ParentId == id).ToList();
        if (!direct.Any()) return new ChildStatsResult { LeafType = "items" };
        var directDone = direct.Count(t => IsDoneForCascade(t.ItemType, t.Status));
        return new ChildStatsResult {
            Total = direct.Count, Done = directDone,
            Pct = (int)Math.Round(100.0 * directDone / direct.Count), LeafType = "items"
        };
    }

    public async Task<IEnumerable<object>> GetSprintsAsync(int? teamId)
    {
        var raw = await TeamSprints(teamId)
            .OrderByDescending(s => s.IsActive).ThenByDescending(s => s.CreatedAt)
            .Select(s => new { s.Id, s.Name, s.IsActive, s.StartDate, s.EndDate })
            .ToListAsync();
        return raw.Select(s => (object)new {
            id        = s.Id,
            name      = s.Name,
            isActive  = s.IsActive,
            startDate = s.StartDate?.ToString("yyyy-MM-dd"),
            endDate   = s.EndDate?.ToString("yyyy-MM-dd")
        }).ToList();
    }

    public async Task<IEnumerable<object>> GetParentItemsAsync(string itemType, int? teamId)
    {
        var parentType = itemType switch {
            "Feature" => "Epic",
            "Story"   => "Feature",
            "Task"    => "Story",
            "Bug"     => "Story",
            _         => ""
        };
        if (string.IsNullOrEmpty(parentType)) return [];
        var q = _db.Tasks.Where(t => t.ItemType == parentType);
        if (teamId.HasValue) q = q.Where(t => t.TeamId == teamId);
        var list = await q.OrderBy(t => t.Goal)
            .Select(t => new {
                id = t.Id, goal = t.Goal, status = t.Status,
                moduleId = t.ModuleId, clientId = t.ClientId,
                project  = t.Project,  property = t.Property, priority = t.Priority
            }).ToListAsync();
        return list.Cast<object>();
    }

    public async Task<IEnumerable<object>> SearchAsync(string q, int? teamId)
    {
        if (string.IsNullOrWhiteSpace(q)) return [];
        IQueryable<TaskItem> query = _db.Tasks;
        if (teamId.HasValue) query = query.Where(t => t.TeamId == teamId);
        query = int.TryParse(q.Trim(), out var id)
            ? query.Where(t => t.Id == id || (t.Goal != null && t.Goal.Contains(q)))
            : query.Where(t => t.Goal != null && t.Goal.Contains(q));
        var list = await query.OrderBy(t => t.Goal).Take(10)
            .Select(t => new { id = t.Id, goal = t.Goal, status = t.Status, itemType = t.ItemType })
            .ToListAsync();
        return list.Cast<object>();
    }

    public async Task<IEnumerable<string>> GetLabelsAsync(int? teamId)
    {
        var raw = await TeamTasks(teamId)
            .Where(t => !string.IsNullOrEmpty(t.Labels) && !t.IsDeleted)
            .Select(t => t.Labels!)
            .ToListAsync();
        return raw
            .SelectMany(l => l.Split(',').Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0))
            .Distinct().OrderBy(l => l).ToList();
    }

    public async Task<IEnumerable<object>> GetModulesAsync(string? himCs)
    {
        var list = new List<object>();
        try
        {
            await using var con = new SqlConnection(himCs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT Id, Code, Name FROM Projects WHERE IsActive = 1 ORDER BY Name", con);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                list.Add(new { id = rdr.GetInt32(0), code = rdr.GetString(1), name = rdr.GetString(2) });
        }
        catch { }
        return list;
    }

    public async Task<IEnumerable<object>> GetClientsAsync(int moduleId, string? himCs, bool includeContactDetails = false)
    {
        var list = new List<object>();
        try
        {
            await using var con = new SqlConnection(himCs);
            await con.OpenAsync();
            var sql = moduleId > 0
                ? @"SELECT DISTINCT cd.Id,cd.Name,cd.ContactPerson,cd.ContactNo01,cd.Email01,1 AS FromCompany
                    FROM Clients c JOIN CompanyDetails cd ON cd.Id=c.CompanyId
                    WHERE c.IsActive=1 AND NULLIF(NULLIF(cd.Name,''),'N/A') IS NOT NULL
                      AND EXISTS(SELECT 1 FROM ClientWiseProjects cwp WHERE cwp.ClientId=c.Id AND cwp.ProjectId=@mid)
                    UNION
                    SELECT c.Id,c.Name,c.ContactPerson,c.ContactNo01,c.Email01,0
                    FROM Clients c LEFT JOIN CompanyDetails cd ON cd.Id=c.CompanyId
                    WHERE c.IsActive=1 AND NULLIF(NULLIF(cd.Name,''),'N/A') IS NULL
                      AND EXISTS(SELECT 1 FROM ClientWiseProjects cwp WHERE cwp.ClientId=c.Id AND cwp.ProjectId=@mid)
                    ORDER BY Name"
                : @"SELECT DISTINCT cd.Id,cd.Name,cd.ContactPerson,cd.ContactNo01,cd.Email01,1 AS FromCompany
                    FROM Clients c JOIN CompanyDetails cd ON cd.Id=c.CompanyId
                    WHERE c.IsActive=1 AND NULLIF(NULLIF(cd.Name,''),'N/A') IS NOT NULL
                    UNION
                    SELECT c.Id,c.Name,c.ContactPerson,c.ContactNo01,c.Email01,0
                    FROM Clients c LEFT JOIN CompanyDetails cd ON cd.Id=c.CompanyId
                    WHERE c.IsActive=1 AND NULLIF(NULLIF(cd.Name,''),'N/A') IS NULL
                    ORDER BY Name";
            await using var cmd = new SqlCommand(sql, con);
            if (moduleId > 0) cmd.Parameters.AddWithValue("@mid", moduleId);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var id   = rdr.GetInt32(0);
                var name = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
                if (includeContactDetails)
                    list.Add(new {
                        id,
                        name,
                        contactPerson = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                        contactNo     = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                        email         = rdr.IsDBNull(4) ? "" : rdr.GetString(4),
                        fromCompany   = rdr.GetInt32(5) == 1
                    });
                else
                    list.Add(new { id, name });
            }
        }
        catch { }
        return list;
    }

    public async Task<BacklogResult> GetBacklogAsync(string sprintFilter, int? teamId)
    {
        var query = _db.Tasks.AsQueryable();
        if (teamId.HasValue) query = query.Where(t => t.TeamId == teamId);

        if (sprintFilter == "active")
        {
            var spQ = _db.Sprints.AsQueryable();
            if (teamId.HasValue) spQ = spQ.Where(s => s.TeamId == teamId);
            var active = await spQ.FirstOrDefaultAsync(s => s.IsActive);
            query = active != null
                ? query.Where(t => t.SprintId == active.Id)
                : query.Where(t => t.SprintId == null);
        }
        else if (sprintFilter == "backlog")
            query = query.Where(t => t.SprintId == null);
        else if (int.TryParse(sprintFilter, out var sid) && sid > 0)
            query = query.Where(t => t.SprintId == sid);

        var tasks   = await query
            .OrderBy(t => t.BacklogOrder == null).ThenBy(t => t.BacklogOrder).ThenBy(t => t.CreatedAt)
            .ToListAsync();
        var sprints = await TeamSprints(teamId)
            .OrderByDescending(s => s.IsActive).ThenByDescending(s => s.CreatedAt)
            .ToListAsync();

        return new BacklogResult { Tasks = tasks, Sprints = sprints };
    }

    public async Task<BurndownResult?> GetBurndownAsync(int sprintId, int? teamId)
    {
        var sprint = await FindSprintInTeam(sprintId, teamId);
        if (sprint == null) return null;

        var taskQ = _db.Tasks.Where(t => t.SprintId == sprintId);
        if (teamId.HasValue) taskQ = taskQ.Where(t => t.TeamId == teamId);
        var taskIds = await taskQ.Select(t => t.Id).ToListAsync();
        var total   = taskIds.Count;

        DateTime start = sprint.StartDate?.Date ?? DateTime.Today.AddDays(-14);
        DateTime end   = sprint.EndDate?.Date   ?? DateTime.Today;
        if (end < start) end = start.AddDays(14);

        var completions = await _db.ActivityLog
            .Where(a => a.TaskId.HasValue && taskIds.Contains(a.TaskId.Value)
                     && a.Action == "Status Changed"
                     && (a.Details.Contains("'Done'") || a.Details.Contains("'Resolved'") || a.Details.Contains("'Closed'")))
            .Select(a => new { a.TaskId, a.ChangedAt })
            .ToListAsync();

        var firstDone = completions
            .GroupBy(c => c.TaskId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(c => c.ChangedAt).Date);

        var days = new List<object>(); var ideal = new List<object>();
        int totalDays = Math.Max(1, (end - start).Days + 1);
        for (int i = 0; i < totalDays; i++)
        {
            var day = start.AddDays(i);
            int doneSoFar = firstDone.Values.Count(d => d <= day);
            double idealRemaining = total - (total * i / (double)(totalDays - 1 < 1 ? 1 : totalDays - 1));
            days.Add(new  { date = day.ToString("MMM dd"), remaining = total - doneSoFar });
            ideal.Add(new { date = day.ToString("MMM dd"), remaining = Math.Round(idealRemaining, 1) });
        }

        return new BurndownResult { SprintName = sprint.Name, Total = total, Days = days, Ideal = ideal };
    }

    public async Task<(byte[] Bytes, string FileName)> ExportCsvAsync(ExportFilters f, int? teamId)
    {
        var query = _db.Tasks.AsQueryable();
        if (teamId.HasValue) query = query.Where(t => t.TeamId == teamId);

        if (!string.IsNullOrEmpty(f.Search))
            query = query.Where(t => (t.Goal ?? "").Contains(f.Search) || (t.Property ?? "").Contains(f.Search) ||
                                     (t.Owner ?? "").Contains(f.Search) || (t.Project  ?? "").Contains(f.Search));
        if (!string.IsNullOrEmpty(f.Project))
            query = f.Project == "__none__" ? query.Where(t => t.Project == "") : query.Where(t => t.Project == f.Project);
        if (!string.IsNullOrEmpty(f.Property)) query = query.Where(t => t.Property == f.Property);
        if (!string.IsNullOrEmpty(f.Owner))    query = query.Where(t => (t.Owner ?? "").Contains(f.Owner));
        if (!string.IsNullOrEmpty(f.Status))
            query = f.Status == "__none__" ? query.Where(t => t.Status == "") : query.Where(t => t.Status == f.Status);
        if (!string.IsNullOrEmpty(f.Priority)) query = query.Where(t => t.Priority == f.Priority);

        var tasks = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();
        if (DateTime.TryParse(f.DateFrom, out var dfrom))
            tasks = tasks.Where(t => DateTime.TryParse(t.TargetDate, out var d) && d.Date >= dfrom.Date).ToList();
        if (DateTime.TryParse(f.DateTo, out var dto))
            tasks = tasks.Where(t => DateTime.TryParse(t.TargetDate, out var d) && d.Date <= dto.Date).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("Id,Goal,Project,Client,Priority,Owner,Status,TargetDate,ExpectedStartDate,ExpectedEndDate,ActualStartDate,ActualEndDate,Weight,RelatedFiles,Notes,CreatedAt");
        foreach (var t in tasks)
            sb.AppendLine($"{t.Id},{Csv(t.Goal)},{Csv(t.Project)},{Csv(t.Property)},{Csv(t.Priority)},{Csv(t.Owner)},{Csv(t.Status)},{Csv(t.TargetDate)},{Csv(t.StartDate)},{Csv(t.EndDate)},{Csv(t.ActualStartDate)},{Csv(t.ActualEndDate)},{t.Weight},{Csv(t.RelatedFiles)},{Csv(t.Notes)},{t.CreatedAt:yyyy-MM-dd HH:mm}");

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return (bytes, $"tasks-{DateTime.Now:yyyyMMdd}.csv");
    }

    // ── ITaskService: write methods ────────────────────────────────────────────

    public async Task<TaskSaveResult> SaveAsync(TaskItem task, UserCtx ctx, bool forceClose)
    {
        try
        {
            string action, details;
            string? capacityWarning = null;

            if (task.Id == 0)
            {
                // Auto-assign member as owner BEFORE the status guard so a member
                // can create a task directly in a started status.
                if (ctx.IsMember) task.Owner = ctx.DisplayName;

                if (task.Status is "Active" or "In Progress" or "Resolved" or "Done" or "Closed"
                    && string.IsNullOrWhiteSpace(task.Owner))
                    return new TaskSaveResult { Error = $"Assign an owner before moving this to {task.Status}." };

                task.CreatedAt = DateTime.Now;
                task.CreatedBy = ctx.DisplayName;
                task.Reporter  = ctx.DisplayName;
                task.TeamId    = ctx.TeamId;
                if (task.ItemType is "Epic" or "Feature") task.SprintId = null;
                _db.Tasks.Add(task);
                await _db.SaveChangesAsync();
                await CascadeParentStatusAsync(task.ParentId, ctx);
                action  = "Created";
                details = $"Goal: {task.Goal}";
                await NotifyAssignedAsync(task, null, ctx);
            }
            else
            {
                if (task.Status is "Active" or "In Progress" or "Resolved" or "Done" or "Closed"
                    && string.IsNullOrWhiteSpace(task.Owner))
                    return new TaskSaveResult { Error = $"Assign an owner before moving this to {task.Status}." };

                var existing = await FindTaskInTeam(task.Id, ctx.TeamId);
                if (existing == null) return new TaskSaveResult { Error = "Task not found" };
                if (!ctx.IsAdmin && !CanModify(existing, ctx))
                    return new TaskSaveResult { Error = "You don't have permission to edit this task." };

                var changes   = new List<string>();
                var oldOwner  = existing.Owner;
                var oldStatus = existing.Status;
                if (existing.Goal     != task.Goal)     changes.Add("Goal");
                if (existing.Status   != task.Status)   changes.Add($"Status: '{existing.Status}' → '{task.Status}'");
                if (existing.Priority != task.Priority) changes.Add($"Priority: '{existing.Priority}' → '{task.Priority}'");
                if (existing.Owner    != task.Owner)    changes.Add($"Owner: '{existing.Owner}' → '{task.Owner}'");

                var isDoneStatus = IsDoneForCascade(task.ItemType, task.Status);
                var wasDone      = IsDoneForCascade(existing.ItemType, existing.Status);
                if (isDoneStatus && !wasDone && !forceClose)
                {
                    var openCount = await CountOpenChildrenAsync(existing.Id);
                    if (openCount > 0)
                        return new TaskSaveResult {
                            OpenChildren = openCount,
                            Error = $"Cannot close: {openCount} child item(s) are still open."
                        };
                }

                if (ctx.IsMember) task.Owner = existing.Owner;
                existing.Goal = task.Goal; existing.Project = task.Project;
                existing.Property = task.Property; existing.Priority = task.Priority;
                existing.Owner = task.Owner; existing.Status = task.Status;
                existing.EndDate = task.EndDate; existing.Notes = task.Notes;
                existing.Weight = task.Weight; existing.Labels = task.Labels;
                existing.AcceptanceCriteria = task.AcceptanceCriteria;
                existing.Swimlane = task.Swimlane;
                existing.ModuleId = task.ModuleId; existing.ClientId = task.ClientId;
                existing.ItemType = task.ItemType; existing.ParentId = task.ParentId;
                var oldSprintId = existing.SprintId;
                existing.SprintId = task.ItemType is "Epic" or "Feature" ? null : task.SprintId;
                if (existing.SprintId.HasValue && existing.SprintId != oldSprintId
                    && string.IsNullOrWhiteSpace(existing.StartDate))
                {
                    var sp = await _db.Sprints.FindAsync(existing.SprintId.Value);
                    if (sp?.StartDate != null)
                        existing.StartDate = sp.StartDate.Value.ToString("yyyy-MM-dd");
                }
                if (!string.IsNullOrWhiteSpace(task.Reporter)) existing.Reporter = task.Reporter;

                await _db.SaveChangesAsync();
                await CascadeParentStatusAsync(existing.ParentId, ctx);
                action  = "Updated";
                details = changes.Count > 0 ? string.Join("; ", changes) : "Minor update";
                await NotifyAssignedAsync(existing, oldOwner, ctx);
                if (oldStatus != existing.Status)
                {
                    await NotifyWatchersStatusAsync(existing, oldStatus, existing.Status, ctx);
                    // Log a canonical "Status Changed" entry so GetBurndownAsync can count completions.
                    await LogAsync(ctx, "Status Changed", $"'{oldStatus}' → '{existing.Status}'",
                        existing.Id, existing.Goal, existing.Project);
                }

                if (existing.SprintId.HasValue && existing.SprintId != oldSprintId
                    && !string.IsNullOrWhiteSpace(existing.Owner))
                {
                    var used = await _db.Tasks
                        .Where(t => t.SprintId == existing.SprintId && !t.IsDeleted && t.Owner == existing.Owner)
                        .SumAsync(t => t.Weight ?? 0);
                    var cap = await _db.SprintCapacities
                        .Where(c => c.SprintId == existing.SprintId.Value && c.OwnerName == existing.Owner)
                        .Select(c => (decimal?)c.CapacityHours).FirstOrDefaultAsync();
                    if (cap.HasValue && cap.Value > 0 && used > cap.Value)
                        capacityWarning = $"{existing.Owner} is over capacity ({used:0.##}/{cap.Value:0.##}h)";
                }

                task = existing;
            }

            await LogAsync(ctx, action, details, task.Id, task.Goal, task.Project);
            await _hub.Clients.Group("team-" + (task.TeamId?.ToString() ?? "0")).SendAsync("TaskUpdated", new {
                id        = task.Id,
                goal      = task.Goal,
                status    = task.Status,
                priority  = task.Priority,
                owner     = task.Owner,
                itemType  = task.ItemType,
                sprintId  = task.SprintId,
                updatedBy = ctx.DisplayName
            });
            return new TaskSaveResult { Success = true, TaskId = task.Id, CapacityWarning = capacityWarning };
        }
        catch (Exception ex) { return new TaskSaveResult { Error = ex.Message }; }
    }

    public async Task<TaskUpdateResult> UpdateStatusAsync(int id, string status, string? swimlane, UserCtx ctx, bool forceClose)
    {
        var task = await FindTaskInTeam(id, ctx.TeamId);
        if (task == null) return new TaskUpdateResult { Error = "Not found" };
        if (!CanModify(task, ctx))
            return new TaskUpdateResult { Error = "You don't have permission to change this task's status." };
        if (status is "Active" or "In Progress" or "Resolved" or "Done" or "Closed"
            && string.IsNullOrWhiteSpace(task.Owner))
            return new TaskUpdateResult { NeedOwner = true, Error = $"Assign an owner before moving this to {status}." };
        if (IsDoneForCascade(task.ItemType, status) && !IsDoneForCascade(task.ItemType, task.Status) && !forceClose)
        {
            var openCount = await CountOpenChildrenAsync(task.Id);
            if (openCount > 0)
                return new TaskUpdateResult { OpenChildren = openCount, Error = $"Cannot close: {openCount} child item(s) are still open." };
        }
        var old = task.Status;
        task.Status = status;
        if (swimlane != null)
            task.Swimlane = swimlane is "Default" or "" ? null : swimlane;
        await _db.SaveChangesAsync();
        await CascadeParentStatusAsync(task.ParentId, ctx);
        if (old != status) await NotifyWatchersStatusAsync(task, old, status, ctx);
        var logDetails = $"'{old}' → '{status}'" + (swimlane != null ? $"; Swimlane: {swimlane}" : "");
        await LogAsync(ctx, "Status Changed", logDetails, id, task.Goal, task.Project);
        await _hub.Clients.Group("team-" + (task.TeamId?.ToString() ?? "0")).SendAsync("TaskUpdated", new {
            id        = task.Id,
            goal      = task.Goal,
            status    = task.Status,
            swimlane  = task.Swimlane,
            priority  = task.Priority,
            owner     = task.Owner,
            itemType  = task.ItemType,
            sprintId  = task.SprintId,
            updatedBy = ctx.DisplayName
        });
        return new TaskUpdateResult { Success = true };
    }

    public async Task<TaskSprintResult> AssignSprintAsync(int taskId, int? sprintId, UserCtx ctx)
    {
        var task = await FindTaskInTeam(taskId, ctx.TeamId);
        if (task == null) return new TaskSprintResult { Error = "Task not found" };
        if (!CanModify(task, ctx)) return new TaskSprintResult { Error = "You don't have permission to move this task." };
        if (task.ItemType is "Epic" or "Feature")
            return new TaskSprintResult { Error = "Epics and Features cannot be assigned to a sprint" };
        if (sprintId.HasValue && string.IsNullOrWhiteSpace(task.Owner))
            return new TaskSprintResult { Error = "Assign an owner to this task before adding it to a sprint." };

        var oldSprintId = task.SprintId;
        task.SprintId   = sprintId;
        Sprint? sprint  = null;
        if (sprintId.HasValue)
        {
            sprint = await FindSprintInTeam(sprintId.Value, ctx.TeamId);
            if (sprint == null) return new TaskSprintResult { Error = "Sprint not found." };
            if (sprint.StartDate != null && string.IsNullOrWhiteSpace(task.StartDate))
                task.StartDate = sprint.StartDate.Value.ToString("yyyy-MM-dd");
        }

        await _db.SaveChangesAsync();

        var oldName = oldSprintId.HasValue
            ? (await _db.Sprints.FindAsync(oldSprintId))?.Name ?? $"Sprint {oldSprintId}"
            : "Backlog";
        var newName = sprint?.Name ?? (sprintId.HasValue ? $"Sprint {sprintId}" : "Backlog");
        await LogAsync(ctx, "Sprint Changed", $"'{oldName}' → '{newName}'", taskId, task.Goal, task.Project);

        string? capacityWarning = null;
        if (sprintId.HasValue && !string.IsNullOrWhiteSpace(task.Owner))
        {
            var used = await _db.Tasks
                .Where(t => t.SprintId == sprintId && !t.IsDeleted && t.Owner == task.Owner)
                .SumAsync(t => t.Weight ?? 0);
            var cap = await _db.SprintCapacities
                .Where(c => c.SprintId == sprintId.Value && c.OwnerName == task.Owner)
                .Select(c => (decimal?)c.CapacityHours).FirstOrDefaultAsync();
            if (cap.HasValue && cap.Value > 0 && used > cap.Value)
                capacityWarning = $"{task.Owner} is over capacity ({used:0.##}/{cap.Value:0.##}h)";
        }
        return new TaskSprintResult { Success = true, SprintId = task.SprintId, CapacityWarning = capacityWarning };
    }

    public async Task<(bool Success, string? Error)> AddCommentAsync(int taskId, string comment, UserCtx ctx)
    {
        if (string.IsNullOrWhiteSpace(comment)) return (false, "Comment cannot be empty.");
        var task = await FindTaskInTeam(taskId, ctx.TeamId);
        if (task == null) return (false, "Task not found.");

        await LogAsync(ctx, "Comment", comment.Trim(), taskId, task.Goal, task.Project);
        await _hub.Clients.Group("task-" + taskId).SendAsync("CommentAdded", taskId);

        var commenter = ctx.DisplayName;
        var allUsers  = await _db.AppUsers.ToListAsync();
        var mentioned = allUsers
            .Where(u => !string.IsNullOrWhiteSpace(u.DisplayName)
                     && comment.Contains("@" + u.DisplayName, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(u.DisplayName, commenter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var user in mentioned)
            _db.Notifications.Add(new Notification {
                UserId = user.Id, TaskId = taskId,
                Message = $"{commenter} mentioned you on: {task.Goal}", CreatedAt = DateTime.Now
            });

        var mentionedIds   = mentioned.Select(m => m.Id).ToHashSet();
        var watcherTargets = (await GetWatcherUsersAsync(taskId))
            .Where(u => !mentionedIds.Contains(u.Id)
                     && !string.Equals(u.DisplayName, commenter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var user in watcherTargets)
            _db.Notifications.Add(new Notification {
                UserId = user.Id, TaskId = taskId,
                Message = $"{commenter} commented on watched task: {task.Goal}", CreatedAt = DateTime.Now
            });

        var notifiedIds = mentionedIds.Union(watcherTargets.Select(u => u.Id)).ToHashSet();
        foreach (var ownerName in SplitOwners(task.Owner))
        {
            if (string.Equals(ownerName, commenter, StringComparison.OrdinalIgnoreCase)) continue;
            var owner = allUsers.FirstOrDefault(u => string.Equals(u.DisplayName, ownerName, StringComparison.OrdinalIgnoreCase));
            if (owner == null || notifiedIds.Contains(owner.Id) || !owner.NotifyOnComment) continue;
            _db.Notifications.Add(new Notification {
                UserId = owner.Id, TaskId = taskId,
                Message = $"{commenter} commented on: {task.Goal}", CreatedAt = DateTime.Now
            });
        }

        await _db.SaveChangesAsync();
        var teamName = await GetTeamNameAsync(task.TeamId);
        _ = NotifyCommentAsync(task, comment.Trim(), commenter, allUsers, mentioned, watcherTargets, teamName);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> AddLinkAsync(int fromTaskId, int toTaskId, string linkType, UserCtx ctx)
    {
        if (fromTaskId == toTaskId) return (false, "Cannot link a task to itself.");
        var fromTask = await FindTaskInTeam(fromTaskId, ctx.TeamId);
        if (fromTask == null) return (false, "Task not found.");
        if (!CanModify(fromTask, ctx)) return (false, "You don't have permission to link this task.");
        var exists = await _db.TaskLinks.AnyAsync(l =>
            (l.FromTaskId == fromTaskId && l.ToTaskId == toTaskId && l.LinkType == linkType) ||
            (l.FromTaskId == toTaskId   && l.ToTaskId == fromTaskId && l.LinkType == linkType));
        if (exists) return (false, "This link already exists.");
        _db.TaskLinks.Add(new TaskLink {
            FromTaskId = fromTaskId, ToTaskId = toTaskId, LinkType = linkType,
            CreatedAt = DateTime.Now, CreatedBy = ctx.DisplayName
        });
        await _db.SaveChangesAsync();
        await _hub.Clients.Group("task-" + fromTaskId).SendAsync("LinkUpdated", fromTaskId);
        await _hub.Clients.Group("task-" + toTaskId).SendAsync("LinkUpdated", toTaskId);
        if (linkType == "Blocks") await NotifyBlockedAsync(fromTaskId, toTaskId);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RemoveLinkAsync(int id, UserCtx ctx)
    {
        var link = await _db.TaskLinks.FindAsync(id);
        if (link == null) return (false, "Not found.");
        var fromTask = await _db.Tasks.FindAsync(link.FromTaskId);
        if (fromTask != null && !CanModify(fromTask, ctx))
            return (false, "You don't have permission to remove this link.");
        int from = link.FromTaskId, to = link.ToTaskId;
        _db.TaskLinks.Remove(link);
        await _db.SaveChangesAsync();
        await _hub.Clients.Group("task-" + from).SendAsync("LinkUpdated", from);
        await _hub.Clients.Group("task-" + to).SendAsync("LinkUpdated", to);
        return (true, null);
    }

    public async Task<WatchToggleResult> ToggleWatchAsync(int taskId, string username, int? teamId)
    {
        if (teamId.HasValue && !await _db.Tasks.AnyAsync(t => t.Id == taskId && t.TeamId == teamId))
            return new WatchToggleResult { Error = "Task not found" };
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null) return new WatchToggleResult { Error = "User not found" };
        var existing = await _db.TaskWatchers.FirstOrDefaultAsync(w => w.TaskId == taskId && w.UserId == user.Id);
        bool watching;
        if (existing != null) { _db.TaskWatchers.Remove(existing); watching = false; }
        else { _db.TaskWatchers.Add(new TaskWatcher { TaskId = taskId, UserId = user.Id, CreatedAt = DateTime.Now }); watching = true; }
        await _db.SaveChangesAsync();
        var count = await _db.TaskWatchers.CountAsync(w => w.TaskId == taskId);
        return new WatchToggleResult { Success = true, Watching = watching, Count = count };
    }

    public async Task<(bool Success, string? Error)> QuickSetOwnerAsync(int id, string owner, UserCtx ctx)
    {
        var task = await FindTaskInTeam(id, ctx.TeamId);
        if (task == null) return (false, "Task not found");
        if (!CanModify(task, ctx)) return (false, "You don't have permission to change this task's owner.");
        var old = task.Owner;
        task.Owner = owner?.Trim();
        await _db.SaveChangesAsync();
        await LogAsync(ctx, "Owner Changed", $"'{old}' → '{task.Owner}'", id, task.Goal, task.Project);
        return (true, null);
    }

    public async Task ReorderBacklogAsync(int[] ids, int? teamId)
    {
        if (ids == null || ids.Length == 0) return;
        var tasks    = await TeamTasks(teamId).Where(t => ids.Contains(t.Id)).ToListAsync();
        var orderMap = ids.Select((id, idx) => new { id, ord = idx + 1 }).ToDictionary(x => x.id, x => x.ord);
        foreach (var t in tasks)
            if (orderMap.TryGetValue(t.Id, out var ord)) t.BacklogOrder = ord;
        await _db.SaveChangesAsync();
    }

    public async Task<(bool Success, string? Error)> SaveAttachmentRecordAsync(
        int taskId, string fileName, string storedName, long fileSize, string contentType, string uploadedBy)
    {
        try
        {
            _db.TaskAttachments.Add(new TaskAttachment {
                TaskId = taskId, FileName = fileName, StoredName = storedName,
                FileSize = fileSize, ContentType = contentType,
                UploadedBy = uploadedBy, UploadedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool Success, string? Error)> DeleteAttachmentAsync(int id, string displayName, bool isAdmin)
    {
        var att = await _db.TaskAttachments.FindAsync(id);
        if (att == null) return (false, "Not found.");
        if (!isAdmin && att.UploadedBy != displayName) return (false, "Permission denied.");
        var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "attachments",
            att.TaskId.ToString(), att.StoredName);
        if (File.Exists(path)) File.Delete(path);
        _db.TaskAttachments.Remove(att);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int id, UserCtx ctx)
    {
        var task = await FindTaskInTeam(id, ctx.TeamId);
        if (task == null) return (true, null); // nothing to delete — treat as success (redirect to index)
        if (!ctx.IsAdmin && !CanModify(task, ctx))
            return (false, "You don't have permission to delete this task.");
        task.IsDeleted = true;
        task.DeletedAt = DateTime.Now;
        task.DeletedBy = ctx.DisplayName;
        await _db.SaveChangesAsync();
        await LogAsync(ctx, "Deleted", "Moved to Recycle Bin", id, task.Goal, task.Project);
        return (true, null);
    }

    public async Task<List<TaskItem>> GetRecycleBinAsync(int? teamId)
    {
        var q = _db.Tasks.IgnoreQueryFilters().Where(t => t.IsDeleted);
        if (teamId.HasValue) q = q.Where(t => t.TeamId == teamId);
        return await q.OrderByDescending(t => t.DeletedAt).ToListAsync();
    }

    public async Task<(bool Success, string? Error)> RestoreAsync(int id)
    {
        var task = await _db.Tasks.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id && t.IsDeleted);
        if (task == null) return (false, "Task not found");
        task.IsDeleted = false; task.DeletedAt = null; task.DeletedBy = null;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> PermanentDeleteAsync(int id)
    {
        var task = await _db.Tasks.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id && t.IsDeleted);
        if (task == null) return (false, "Not found");
        _db.Tasks.Remove(task);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, int Count)> EmptyBinAsync(int? teamId)
    {
        var q = _db.Tasks.IgnoreQueryFilters().Where(t => t.IsDeleted);
        if (teamId.HasValue) q = q.Where(t => t.TeamId == teamId);
        var tasks = await q.ToListAsync();
        _db.Tasks.RemoveRange(tasks);
        await _db.SaveChangesAsync();
        return (true, tasks.Count);
    }

    public async Task<BulkOpResult> BulkDeleteAsync(int[] ids, UserCtx ctx)
    {
        if (ids == null || ids.Length == 0) return new BulkOpResult { Error = "No tasks selected" };
        var now   = DateTime.Now;
        var all   = await TeamTasks(ctx.TeamId).Where(t => ids.Contains(t.Id)).ToListAsync();
        var tasks = all.Where(t => CanModify(t, ctx)).ToList();
        var denied = all.Count - tasks.Count;
        foreach (var t in tasks) { t.IsDeleted = true; t.DeletedAt = now; t.DeletedBy = ctx.DisplayName; }
        await _db.SaveChangesAsync();
        return new BulkOpResult { Success = true, Count = tasks.Count, Denied = denied };
    }

    public async Task<BulkStatusOpResult> BulkChangeStatusAsync(int[] ids, string status, UserCtx ctx)
    {
        if (ids == null || ids.Length == 0 || string.IsNullOrEmpty(status))
            return new BulkStatusOpResult { Error = "Invalid request" };
        var all    = await TeamTasks(ctx.TeamId).Where(t => ids.Contains(t.Id)).ToListAsync();
        var tasks  = all.Where(t => CanModify(t, ctx)).ToList();
        var denied = all.Count - tasks.Count;
        var noOwner = 0;
        if (status is "Active" or "In Progress" or "Resolved" or "Done" or "Closed")
        {
            noOwner = tasks.Count(t => string.IsNullOrWhiteSpace(t.Owner));
            tasks   = tasks.Where(t => !string.IsNullOrWhiteSpace(t.Owner)).ToList();
        }
        foreach (var t in tasks) t.Status = status;
        await _db.SaveChangesAsync();
        foreach (var pid in tasks.Select(t => t.ParentId).Where(p => p != null).Distinct())
            await CascadeParentStatusAsync(pid, ctx);
        return new BulkStatusOpResult { Success = true, Count = tasks.Count, Denied = denied, NoOwner = noOwner };
    }

    public async Task<BulkOpResult> BulkChangeOwnerAsync(int[] ids, string? owner, UserCtx ctx)
    {
        if (ids == null || ids.Length == 0) return new BulkOpResult { Error = "Invalid request" };
        var all   = await TeamTasks(ctx.TeamId).Where(t => ids.Contains(t.Id)).ToListAsync();
        var tasks = all.Where(t => CanModify(t, ctx)).ToList();
        var denied = all.Count - tasks.Count;
        foreach (var t in tasks) t.Owner = owner;
        await _db.SaveChangesAsync();
        return new BulkOpResult { Success = true, Count = tasks.Count, Denied = denied };
    }

    public async Task<BulkSprintOpResult> BulkAssignSprintAsync(int[] ids, int? sprintId, UserCtx ctx)
    {
        if (ids == null || ids.Length == 0) return new BulkSprintOpResult { Error = "Invalid request" };
        var all = await TeamTasks(ctx.TeamId)
            .Where(t => ids.Contains(t.Id) && t.ItemType != "Epic" && t.ItemType != "Feature")
            .ToListAsync();
        var tasks  = all.Where(t => CanModify(t, ctx)).ToList();
        var denied = all.Count - tasks.Count;
        var skipped = 0;
        foreach (var t in tasks)
        {
            if (sprintId.HasValue && string.IsNullOrWhiteSpace(t.Owner)) { skipped++; continue; }
            t.SprintId = sprintId;
        }
        await _db.SaveChangesAsync();
        return new BulkSprintOpResult { Success = true, Count = tasks.Count - skipped, Skipped = skipped, Denied = denied };
    }

    public async Task<BulkOpResult> BulkChangePriorityAsync(int[] ids, string? priority, UserCtx ctx)
    {
        if (ids == null || ids.Length == 0) return new BulkOpResult { Error = "Invalid request" };
        var all   = await TeamTasks(ctx.TeamId).Where(t => ids.Contains(t.Id)).ToListAsync();
        var tasks = all.Where(t => CanModify(t, ctx)).ToList();
        var denied = all.Count - tasks.Count;
        foreach (var t in tasks) t.Priority = priority;
        await _db.SaveChangesAsync();
        return new BulkOpResult { Success = true, Count = tasks.Count, Denied = denied };
    }
}
