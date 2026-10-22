namespace TaskTracker.Services;

public interface IChatService
{
    /// <summary>Newest-first page of a team's chat, returned in chronological (ascending) order.
    /// Pass beforeId to load older messages. forUserId marks which reactions are the caller's own.</summary>
    Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(int teamId, int? beforeId, int take, int? forUserId = null);

    /// <summary>Inserts the text row and returns its id (attachments are added next via Finalize).</summary>
    Task<int> CreateMessageAsync(int teamId, int? userId, string senderName, string? body);

    /// <summary>Persists attachment records, builds the full DTO, broadcasts it to the team, returns it.</summary>
    Task<ChatMessageDto> FinalizeAndBroadcastAsync(int messageId, int teamId, IReadOnlyList<ChatIncomingAttachment> attachments);

    /// <summary>Metadata for the authorized download action, or null if not in this team.</summary>
    Task<ChatAttachmentFile?> GetAttachmentAsync(int attachmentId, int teamId);

    /// <summary>Soft-deletes a message (own message, or any if the caller is a team admin) and broadcasts it.</summary>
    Task<(bool Ok, string? Error)> DeleteMessageAsync(int messageId, int teamId, int? userId, bool isTeamAdmin);

    /// <summary>Edits the body of the caller's own message, stamps EditedAt, and broadcasts the change.</summary>
    Task<(bool Ok, string? Error)> EditMessageAsync(int messageId, int teamId, int userId, string newBody);

    /// <summary>Adds the emoji reaction if absent, removes it if present, then broadcasts the new count for that emoji.</summary>
    Task<(bool Ok, string? Error)> ToggleReactionAsync(int messageId, int teamId, int userId, string emoji);

    /// <summary>Display names of everyone in the team (for @mention autocomplete).</summary>
    Task<IReadOnlyList<string>> GetMemberNamesAsync(int teamId);

    /// <summary>All non-deleted attachments shared in the team's chat, newest first (for the "Shared files" panel).</summary>
    Task<IReadOnlyList<ChatFileDto>> GetTeamAttachmentsAsync(int teamId, int take = 120);

    /// <summary>Posts an auto-generated activity card (Kind="event") attributed to the actor and broadcasts it.
    /// Never throws — failures are swallowed so they can't break the triggering task/sprint operation.</summary>
    Task PostEventAsync(int teamId, int? actorUserId, string actorName, string body, object? meta = null);
}

public sealed record ChatIncomingAttachment(string FileName, string StoredName, long FileSize, string? ContentType);
public sealed record ChatAttachmentFile(int TeamId, string StoredName, string FileName, string ContentType);

public sealed class ChatAttachmentDto
{
    public int     Id          { get; set; }
    public string  FileName    { get; set; } = "";
    public long    FileSize    { get; set; }
    public string? ContentType { get; set; }
    public bool    IsImage     { get; set; }
    public string  Url         { get; set; } = "";
}

public sealed class ChatFileDto
{
    public int     Id          { get; set; }
    public string  FileName    { get; set; } = "";
    public long    FileSize    { get; set; }
    public string? ContentType { get; set; }
    public bool    IsImage     { get; set; }
    public string  Url         { get; set; } = "";
    public string  UploadedAt  { get; set; } = "";   // ISO-8601
    public string  SenderName  { get; set; } = "";
}

public sealed class ChatReactionDto
{
    public string Emoji { get; set; } = "";
    public int    Count { get; set; }
    public bool   Mine  { get; set; }   // did the requesting user react with this emoji
}

public sealed class ChatMessageDto
{
    public int     Id          { get; set; }
    public int     TeamId      { get; set; }
    public int?    UserId      { get; set; }
    public string  SenderName  { get; set; } = "";
    public string? Body        { get; set; }
    public string  CreatedAt   { get; set; } = "";   // ISO-8601
    public string? EditedAt    { get; set; }          // ISO-8601, null when never edited
    public bool    Deleted     { get; set; }
    public string  Kind        { get; set; } = "user";  // "user" | "event"
    public object? Meta        { get; set; }             // event card payload (icon, url, link), else null
    public string  AvatarColor { get; set; } = "#094f70";
    public string? AvatarImage { get; set; }
    public List<ChatAttachmentDto> Attachments { get; set; } = [];
    public List<ChatReactionDto>   Reactions   { get; set; } = [];
}
