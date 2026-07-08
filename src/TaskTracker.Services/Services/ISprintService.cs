using TaskTracker.Models;

namespace TaskTracker.Services;

public interface ISprintService
{
    Task<SprintIndexData> GetIndexDataAsync(int? teamId);
    Task<(bool Success, string? Error)> SaveAsync(Sprint sprint, string createdBy, int? teamId);
    Task SetActiveAsync(int id, int? teamId);
    Task DeactivateAsync(int? teamId);
    Task<SprintIncompleteResult?> GetIncompleteAsync(int sprintId, int? teamId);
    Task<(bool Found, bool Success, int Moved, string MovedTo, string? Error)> CompleteAsync(int id, string moveTo, int? teamId);
    Task<(bool Success, string? Error)> DeleteAsync(int id, int? teamId);
    Task<SprintCapacityData?> GetCapacityAsync(int sprintId, int? teamId);
    Task<bool> SaveCapacityAsync(SprintCapacityRequest req, int? teamId);
}

public class SprintIndexData
{
    public List<Sprint>         Sprints    { get; set; } = [];
    public Dictionary<int, int> TaskCounts { get; set; } = [];
    public List<string>         OwnerNames { get; set; } = [];
}

public class SprintIncompleteResult
{
    public int Total      { get; set; }
    public int Incomplete { get; set; }
    public int Done       { get; set; }
}

public class SprintCapacityData
{
    public int     SprintId       { get; set; }
    public string  SprintName     { get; set; } = "";
    public string? StartDate      { get; set; }
    public string? EndDate        { get; set; }
    public decimal TotalCapacity  { get; set; }
    public decimal TotalAllocated { get; set; }
    public List<SprintCapacityRow> Rows { get; set; } = [];
}

public class SprintCapacityRow
{
    public string   OwnerName       { get; set; } = "";
    public decimal? CapacityHours   { get; set; }
    public decimal  AllocatedPoints { get; set; }
    public int      TaskCount       { get; set; }
}

public class SprintCapacityRequest
{
    public int     SprintId      { get; set; }
    public string  OwnerName     { get; set; } = "";
    public decimal CapacityHours { get; set; }
}
