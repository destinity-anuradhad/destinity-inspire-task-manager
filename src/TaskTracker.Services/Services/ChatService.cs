using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Hubs;
using TaskTracker.Models;

namespace TaskTracker.Services;

/// <summary>
/// Team group chat. Every message is scoped to a team (no direct messages). Real-time delivery reuses
/// the existing <see cref="TaskHub"/> "team-{id}" group that every connection already joins on connect.
/// Attachment blobs are stored via <see cref="IAttachmentStorage"/> keyed by TeamId; only metadata is in the DB.
/// </summary>
public sealed class ChatService(AppDbContext db, IHubContext<TaskHub> hub) : IChatService
{
    private readonly AppDbContext _db = db;
    private readonly IHubContext<TaskHub> _hub = hub;

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(int teamId, int? beforeId, int take, int? forUserId = null)
    {
        take = Math.Clamp(take, 1, 100);
        var q = _db.ChatMessages.AsNoTracking().Where(m => m.TeamId == teamId);
        if (beforeId is > 0) q = q.Where(m => m.Id < beforeId);
        var msgs = await q.OrderByDescending(m => m.Id).Take(take).ToListAsync();
        msgs.Reverse();   // return oldest → newest for display
        return await BuildDtosAsync(teamId, msgs, forUserId);
    }

    public async Task<int> CreateMessageAsync(int teamId, int? userId, string senderName, string? body)
    {
        var m = new ChatMessage {
            TeamId = teamId, UserId = userId, SenderName = senderName,
            Body = string.IsNullOrWhiteSpace(body) ? null : body.Trim(),
            CreatedAt = DateTime.Now
        };
        _db.ChatMessages.Add(m);
        await _db.SaveChangesAsync();
        return m.Id;
    }

    public async Task<ChatMessageDto> FinalizeAndBroadcastAsync(int messageId, int teamId, IReadOnlyList<ChatIncomingAttachment> attachments)
    {
        if (attachments.Count > 0)
        {
            foreach (var a in attachments)
                _db.ChatAttachments.Add(new ChatAttachment {
                    MessageId = messageId, TeamId = teamId, FileName = a.FileName, StoredName = a.StoredName,
                    FileSize = a.FileSize, ContentType = a.ContentType, UploadedAt = DateTime.Now
                });
            await _db.SaveChangesAsync();
        }

        var msg = await _db.ChatMessages.AsNoTracking().FirstAsync(m => m.Id == messageId);
        var dto = (await BuildDtosAsync(teamId, [msg]))[0];
        await _hub.Clients.Group("team-" + teamId).SendAsync("ChatMessage", dto);
        return dto;
    }

    public async Task<ChatAttachmentFile?> GetAttachmentAsync(int attachmentId, int teamId)
    {
        var a = await _db.ChatAttachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.TeamId == teamId);
        return a == null ? null
            : new ChatAttachmentFile(a.TeamId, a.StoredName, a.FileName, a.ContentType ?? "application/octet-stream");
    }

    public async Task<(bool Ok, string? Error)> DeleteMessageAsync(int messageId, int teamId, int? userId, bool isTeamAdmin)
    {
        var m = await _db.ChatMessages.FirstOrDefaultAsync(x => x.Id == messageId && x.TeamId == teamId);
        if (m == null) return (false, "Message not found.");
        if (!isTeamAdmin && m.UserId != userId) return (false, "You can only delete your own messages.");
        m.IsDeleted = true;
        m.Body = null;
        await _db.SaveChangesAsync();
        await _hub.Clients.Group("team-" + teamId).SendAsync("ChatMessageDeleted", messageId);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> EditMessageAsync(int messageId, int teamId, int userId, string newBody)
    {
        newBody = (newBody ?? "").Trim();
        if (newBody.Length == 0)  return (false, "Message can't be empty — delete it instead.");
        if (newBody.Length > 4000) return (false, "Message is too long.");

        var m = await _db.ChatMessages.FirstOrDefaultAsync(x => x.Id == messageId && x.TeamId == teamId);
        if (m == null || m.IsDeleted) return (false, "Message not found.");
        if (m.UserId != userId)       return (false, "You can only edit your own messages.");

        m.Body = newBody;
        m.EditedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _hub.Clients.Group("team-" + teamId)
            .SendAsync("ChatMessageEdited", new { id = m.Id, body = m.Body, editedAt = m.EditedAt.Value.ToString("o") });
        return (true, null);
    }

    // Emoji reactions are limited to a fixed palette so the column stays small and inputs stay safe.
    public static readonly IReadOnlyList<string> AllowedEmojis =
        ["👍", "❤️", "😂", "🎉", "😮", "😢", "🙏", "👀", "✅", "🔥"];

    public async Task<(bool Ok, string? Error)> ToggleReactionAsync(int messageId, int teamId, int userId, string emoji)
    {
        if (userId <= 0)                    return (false, "Sign-in required.");
        if (!AllowedEmojis.Contains(emoji)) return (false, "Unsupported reaction.");

        var msgExists = await _db.ChatMessages.AnyAsync(x => x.Id == messageId && x.TeamId == teamId && !x.IsDeleted);
        if (!msgExists) return (false, "Message not found.");

        var existing = await _db.ChatReactions
            .FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == userId && r.Emoji == emoji);
        if (existing != null) _db.ChatReactions.Remove(existing);
        else _db.ChatReactions.Add(new ChatReaction { MessageId = messageId, TeamId = teamId, UserId = userId, Emoji = emoji, CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();

        var count = await _db.ChatReactions.CountAsync(r => r.MessageId == messageId && r.Emoji == emoji);
        await _hub.Clients.Group("team-" + teamId)
            .SendAsync("ChatReaction", new { messageId, emoji, count });
        return (true, null);
    }

    public async Task PostEventAsync(int teamId, int? actorUserId, string actorName, string body, object? meta = null)
    {
        try
        {
            var m = new ChatMessage {
                TeamId = teamId, UserId = actorUserId, SenderName = actorName,
                Body = body, Kind = "event",
                MetaJson = meta == null ? null : JsonSerializer.Serialize(meta),
                CreatedAt = DateTime.Now
            };
            _db.ChatMessages.Add(m);
            await _db.SaveChangesAsync();
            var dto = (await BuildDtosAsync(teamId, [m]))[0];
            await _hub.Clients.Group("team-" + teamId).SendAsync("ChatMessage", dto);
        }
        catch { /* an activity post must never break the task/sprint operation that triggered it */ }
    }

    public async Task<IReadOnlyList<ChatFileDto>> GetTeamAttachmentsAsync(int teamId, int take = 120)
    {
        take = Math.Clamp(take, 1, 300);
        var rows = await (from a in _db.ChatAttachments.AsNoTracking()
                          join m in _db.ChatMessages.AsNoTracking() on a.MessageId equals m.Id
                          where a.TeamId == teamId && !m.IsDeleted
                          orderby a.Id descending
                          select new { a.Id, a.FileName, a.FileSize, a.ContentType, a.UploadedAt, m.SenderName })
                         .Take(take).ToListAsync();
        return rows.Select(a => new ChatFileDto {
            Id = a.Id, FileName = a.FileName, FileSize = a.FileSize, ContentType = a.ContentType,
            IsImage = (a.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase),
            Url = "/Chat/File/" + a.Id, UploadedAt = a.UploadedAt.ToString("o"), SenderName = a.SenderName
        }).ToList();
    }

    public async Task<IReadOnlyList<string>> GetMemberNamesAsync(int teamId)
    {
        // Display names of everyone in the team, sourced from the membership table joined to users.
        var ids = await _db.TeamMembers.AsNoTracking()
            .Where(tm => tm.TeamId == teamId).Select(tm => tm.UserId).ToListAsync();
        var names = await _db.AppUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => u.DisplayName)
            .ToListAsync();
        return names.Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────
    private async Task<List<ChatMessageDto>> BuildDtosAsync(int teamId, List<ChatMessage> msgs, int? forUserId = null)
    {
        if (msgs.Count == 0) return [];
        var ids = msgs.Select(m => m.Id).ToList();

        var atts = await _db.ChatAttachments.AsNoTracking()
            .Where(a => ids.Contains(a.MessageId)).ToListAsync();
        var attByMsg = atts.GroupBy(a => a.MessageId).ToDictionary(g => g.Key, g => g.ToList());

        var userIds = msgs.Where(m => m.UserId.HasValue).Select(m => m.UserId!.Value).Distinct().ToList();
        var users = await _db.AppUsers.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Color, u.ProfileImagePath }).ToListAsync();
        var userMap = users.ToDictionary(u => u.Id);

        // Reactions for this page, grouped by message → emoji → count (+ whether the caller reacted).
        var reacts = await _db.ChatReactions.AsNoTracking()
            .Where(r => ids.Contains(r.MessageId)).ToListAsync();
        var reactByMsg = reacts.GroupBy(r => r.MessageId).ToDictionary(
            g => g.Key,
            g => g.GroupBy(r => r.Emoji)
                  .Select(e => new ChatReactionDto {
                      Emoji = e.Key,
                      Count = e.Count(),
                      Mine  = forUserId.HasValue && e.Any(r => r.UserId == forUserId.Value)
                  })
                  .OrderByDescending(d => d.Count).ThenBy(d => d.Emoji)
                  .ToList());

        return msgs.Select(m => {
            userMap.TryGetValue(m.UserId ?? 0, out var u);
            var mAtts = attByMsg.TryGetValue(m.Id, out var list) ? list : [];
            return new ChatMessageDto {
                Id = m.Id, TeamId = m.TeamId, UserId = m.UserId, SenderName = m.SenderName,
                Body = m.IsDeleted ? null : m.Body,
                CreatedAt = m.CreatedAt.ToString("o"),
                EditedAt = m.EditedAt?.ToString("o"),
                Deleted = m.IsDeleted,
                Kind = m.Kind,
                Meta = m.IsDeleted || string.IsNullOrEmpty(m.MetaJson) ? null : JsonSerializer.Deserialize<JsonElement>(m.MetaJson),
                AvatarColor = u?.Color ?? "#094f70",
                AvatarImage = u?.ProfileImagePath,
                Attachments = m.IsDeleted ? [] : mAtts.Select(a => new ChatAttachmentDto {
                    Id = a.Id, FileName = a.FileName, FileSize = a.FileSize, ContentType = a.ContentType,
                    IsImage = (a.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase),
                    Url = "/Chat/File/" + a.Id
                }).ToList(),
                Reactions = m.IsDeleted ? [] : (reactByMsg.TryGetValue(m.Id, out var rl) ? rl : [])
            };
        }).ToList();
    }
}
