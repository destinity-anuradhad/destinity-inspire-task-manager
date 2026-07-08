namespace TaskTracker.Models;

public class EmailLog
{
    public int      Id           { get; set; }
    public string   ToEmail      { get; set; } = "";
    public string   ToName       { get; set; } = "";
    public string   Subject      { get; set; } = "";
    public string   HtmlBody     { get; set; } = "";
    public DateTime SentAt       { get; set; }
    public bool     IsSuccess    { get; set; }
    public string?  ErrorMessage { get; set; }
}
