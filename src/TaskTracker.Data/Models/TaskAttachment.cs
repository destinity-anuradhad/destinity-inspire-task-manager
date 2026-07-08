using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("TaskAttachments")]
public class TaskAttachment
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string FileName { get; set; } = "";
    public string StoredName { get; set; } = "";
    public long FileSize { get; set; }
    public string? ContentType { get; set; }
    public string? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.Now;
}
