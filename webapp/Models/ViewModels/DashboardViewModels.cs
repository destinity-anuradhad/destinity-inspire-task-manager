namespace TaskTracker.Models.ViewModels;

public class OwnerStat
{
    public string Name       { get; set; } = "";
    public int    Total      { get; set; }
    public int    Done       { get; set; }
    public int    InProgress { get; set; }
    public int    Overdue    { get; set; }
    public int    CompletionPct => Total > 0 ? (int)Math.Round((double)Done / Total * 100) : 0;
}
