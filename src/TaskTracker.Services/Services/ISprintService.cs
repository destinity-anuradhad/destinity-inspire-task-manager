using TaskTracker.Models;

namespace TaskTracker.Services;

public interface ISprintService
{
    Task<SprintIndexData> GetIndexDataAsync(int? teamId);
    Task<(bool Success, int SprintId, string? Error)> SaveAsync(Sprint sprint, string createdBy, int? teamId);
    Task SetActiveAsync(int id, int? teamId, string? by = null);
    Task DeactivateAsync(int? teamId);
    Task<SprintIncompleteResult?> GetIncompleteAsync(int sprintId, int? teamId);
    Task<(bool Found, bool Success, int Moved, string MovedTo, string? Error)> CompleteAsync(int id, string moveTo, List<SprintCarryItem>? carryItems, int? teamId, string completedBy);
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
    public Dictionary<int, decimal>                      VelocityHours     { get; set; } = [];
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
    public int Excluded   { get; set; }
    public List<SprintIncompleteTask> Tasks { get; set; } = [];
}

/// <summary>One unfinished task shown in the sprint-close dialog, with its estimate/actual/remaining hours.</summary>
public class SprintIncompleteTask
{
    public int     Id        { get; set; }
    public string  Goal      { get; set; } = "";
    public string  ItemType  { get; set; } = "Task";
    public string  Priority  { get; set; } = "";
    public string  Owner     { get; set; } = "";
    public decimal Estimate  { get; set; }   // Weight (original estimate hours) — never mutated
    public decimal Actual    { get; set; }   // ActualHours logged so far
    public decimal Remaining { get; set; }   // RemainingWorkHours, or estimate − actual if unset
}

/// <summary>Per-task carry-forward instruction submitted when a sprint is completed.</summary>
public class SprintCarryItem
{
    public int      TaskId     { get; set; }
    public string   MoveTo     { get; set; } = "backlog";   // "backlog" or a target sprint id
    public decimal  HoursSpent { get; set; }                // extra hours worked this sprint, logged at close
    public decimal? Remaining  { get; set; }                // re-estimated remaining work to carry forward
    public string   Mode       { get; set; } = "move";      // "move" = whole task moves; "split" = leave a done slice, carry the remainder
}

public class SprintCapacityData
{
    public int     SprintId         { get; set; }
    public string  SprintName       { get; set; } = "";
    public string? StartDate        { get; set; }
    public string? EndDate          { get; set; }
    public bool    IsCompleted      { get; set; }
    public decimal TotalCapacity    { get; set; }
    public decimal TotalAllocated   { get; set; }
    public decimal TotalActualHours { get; set; }
    public List<SprintCapacityRow> Rows { get; set; } = [];
}

public class SprintCapacityRow
{
    public string   OwnerName          { get; set; } = "";
    public decimal? CapacityHours      { get; set; }
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
