namespace TaskTracker.Services;

public interface IChartService
{
    Task<ChartData> GetChartDataAsync(int? teamId);
}

public class ChartData
{
    public int    TotalTasks   { get; set; }
    public string VelocityJson { get; set; } = "[]";
    public string StatusJson   { get; set; } = "{}";
    public string PriorityJson { get; set; } = "{}";
    public string OwnerJson    { get; set; } = "{}";
    public string PropertyJson { get; set; } = "{}";
    public string ProjectJson  { get; set; } = "{}";
}
