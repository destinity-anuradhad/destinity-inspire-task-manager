using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>A message in a team's group chat. Team-scoped only — there are no direct/1:1 messages.</summary>
[Table("ChatMessages")]
public class ChatMessage
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public int? UserId { get; set; }
    public string SenderName { get; set; } = "";   // denormalized display name at send time
    public string? Body { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>"user" for a typed message, "event" for an auto-posted activity card (assignment, sprint, …).</summary>
    public string Kind { get; set; } = "user";
    /// <summary>JSON payload for event cards (icon, link url) — null for normal messages.</summary>
    public string? MetaJson { get; set; }
}
