using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>One emoji reaction by one user on one chat message. Unique per (MessageId, UserId, Emoji).</summary>
[Table("ChatReactions")]
public class ChatReaction
{
    public int      Id        { get; set; }
    public int      MessageId { get; set; }
    public int      TeamId    { get; set; }
    public int      UserId    { get; set; }
    public string   Emoji     { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
