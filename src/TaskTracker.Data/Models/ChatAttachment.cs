using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>A file attached to a chat message. Blobs live outside the web root (via IAttachmentStorage,
/// keyed by TeamId); only this metadata is in the DB. Served only through the authorized download action.</summary>
[Table("ChatAttachments")]
public class ChatAttachment
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public int TeamId { get; set; }               // storage partition key (matches the message's team)
    public string FileName { get; set; } = "";    // original name shown to users
    public string StoredName { get; set; } = "";  // server GUID name on disk
    public long FileSize { get; set; }
    public string? ContentType { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.Now;
}
