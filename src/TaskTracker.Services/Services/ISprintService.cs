using TaskTracker.Models;

namespace TaskTracker.Services;

public interface ISprintService
{
    Task<SprintIndexData> GetIndexDataAsync(int? teamId);
    Task<(bool Success, int SprintId, string? Error)> SaveAsync(Sprint sprint, string createdBy, int? teamId);
    Task SetActiveAsync(int id, int? teamId);
    Task DeactivateAsync(int? teamId);
    Task<SprintIncompleteResult?> GetIncompleteAsync(int sprintId, int? teamId);
    Task<(bool Found, bool Success, int Moved, string MovedTo, string? Error)> CompleteAsync(int id, string moveTo, int? teamId, string completedBy);
    Task<(bool Success, string? Error)> DeleteAsync(int id, int? teamId);
    Task<SprintCapacityData?> GetCapacityAsync(int sprintId, int? teamId);
    Task<bool> SaveCapacityAsync(SprintCapacityRequest req, int? teamId);
}

public class SprintIndexData
{
    public List<Sprint>                                  Sprints           { get; set; } = [];
    public Dictionary<int, int>                          TaskCounts        { get; set; } = [];
    public List<string>                                  OwnerNames        { get; set; } = [];
    public List<string>                                  TeamMemberNames   { get; set; } = [];
    public Dictionary<int, decimal>                      VelocityPoints    { get; set; } = [];
    public Dictionary<int, int>                          SpilloverCounts   { get; set; } = [];
    public Dictionary<int, decimal>                      ActualHours       { get; set; } = [];
    public Dictionary<int, decimal>                      EstimateHours     { get; set; } = [];
    public Dictionary<int, decimal>                      SprintHours       { get; set; } = [];
    public Dictionary<int, List<SprintUserHoursRow>>     UserHours         { get; set; } = [];
}

public class SprintUserHoursRow
{
    public string   Name     { get; set; } = "";
    public decimal  Estimate { get; set; }
    public decimal  Actual   { get; set; }
    public decimal? Sprint   { get; set; }
    public int HrPct => Sprint is > 0 ? (int)Math.Round((double)(Actual / Sprint.Value) * 100) : 0;
}

public class SprintIncompleteResult
{
    public int Total      { get; set; }
    public int Incomplete { get; set; }
    public int Done       { get; set; }
}

public class SprintCapacityData
{
    public int     SprintId         { get; set; }
    public string  SprintName       { get; set; } = "";
    public string? StartDate        { get; set; }
    public string? EndDate          { get; set; }
    public decimal TotalCapacity    { get; set; }
    public decimal TotalAllocated   { get; set; }
    public decimal TotalActualHours { get; set; }
    public List<SprintCapacityRow> Rows { get; set; } = [];
}

public class SprintCapacityRow
{
    public string   OwnerName          { get; set; } = "";
    public decimal? CapacityHours      { get; set; }
    public decimal  AllocatedPoints    { get; set; }
    public decimal  AllocatedHours     { get; set; }
    public decimal  ActualHours        { get; set; }
    public int      TaskCount          { get; set; }
    public int      DoneCount          { get; set; }
    public int      CompletionPct      => TaskCount > 0 ? (int)Math.Round((double)DoneCount / TaskCount * 100) : 0;
    public int      HoursCompletionPct
    {
        get
        {
            var denom = CapacityHours is > 0 ? CapacityHours.Value
                      : AllocatedHours > 0   ? AllocatedHours
                      : 0m;
            return denom > 0 ? (int)Math.Round((double)(ActualHours / denom) * 100) : 0;
        }
    }
}

public class SprintCapacityRequest
{
    public int     SprintId      { get; set; }
    public string  OwnerName     { get; set; } = "";
    public decimal CapacityHours { get; set; }
}
