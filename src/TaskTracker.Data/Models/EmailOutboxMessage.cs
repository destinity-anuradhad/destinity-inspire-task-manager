namespace TaskTracker.Models;

public class EmailOutboxMessage
{
    public long     Id             { get; set; }
    public string   ToEmail        { get; set; } = "";
    public string   ToName         { get; set; } = "";
    public string   Subject        { get; set; } = "";
    public string   HtmlBody       { get; set; } = "";
    public string   Status         { get; set; } = "Pending";
    public int      AttemptCount   { get; set; }
    public DateTime AvailableAt    { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessingAt  { get; set; }
    public DateTime? SentAt        { get; set; }
    public string?  LastError      { get; set; }
    public string?  IdempotencyKey { get; set; }
}
