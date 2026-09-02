using TaskTracker.Models;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services;

public interface ITaskService
{
    Task<TaskListResult>      GetListAsync(TaskListFilters f, int? teamId);
    Task<TaskItem?>           GetByIdAsync(int id, int? teamId);
    Task<IEnumerable<object>> GetCommentsAsync(int taskId, int? teamId);
    Task<IEnumerable<object>> GetHistoryAsync(int taskId, int? teamId);
    Task<object?>             GetLinksAsync(int taskId, int? teamId);
    Task<WatcherResult>       GetWatchersAsync(int taskId, int? teamId, string displayName);
    Task<IEnumerable<object>> GetAttachmentsAsync(int taskId);
    Task<AttachmentDownload?> GetDownloadAsync(int id, int? teamId = null);
    Task<ChildStatsResult?>   GetChildStatsAsync(int id, int? teamId);
    Task<List<TaskChecklistItem>?> GetChecklistAsync(int taskId, int? teamId);

    Task<TaskSaveResult>   SaveAsync(TaskItem task, UserCtx ctx, bool forceClose);
    Task<(bool Success, string? Error)> DeleteAsync(int id, UserCtx ctx);
    Task<(bool Success, string? Error)> ClaimTaskAsync(int taskId, UserCtx ctx);
    Task<TaskUpdateResult> UpdateStatusAsync(int id, string status, string? swimlane, UserCtx ctx, bool forceClose, decimal? actualHours = null, byte[]? expectedRowVersion = null);
    Task<TaskSprintResult> AssignSprintAsync(int taskId, int? sprintId, UserCtx ctx);
    Task<TimeEntryResult> LogTimeAsync(int taskId, decimal hours, DateTime workDate, string? note, decimal? remainingWorkHours, UserCtx ctx);
    Task<TimeEntryResult> CorrectTimeEntryAsync(long entryId, string? note, UserCtx ctx);
    Task<(bool Success, string? Error)> AddCommentAsync(int taskId, string comment, UserCtx ctx);
    Task<(bool Success, string? Error)> AddChecklistItemAsync(int taskId, string text, UserCtx ctx);
    Task<(bool Success, string? Error)> ToggleChecklistItemAsync(int id, bool isCompleted, UserCtx ctx);
    Task<(bool Success, string? Error)> DeleteChecklistItemAsync(int id, UserCtx ctx);
    Task<(bool Success, string? Error)> AddLinkAsync(int fromTaskId, int toTaskId, string linkType, UserCtx ctx);
    Task<(bool Success, string? Error)> RemoveLinkAsync(int id, UserCtx ctx);
    Task<WatchToggleResult> ToggleWatchAsync(int taskId, string username, int? teamId);
    Task<(bool Success, string? Error)> QuickSetOwnerAsync(int id, string owner, UserCtx ctx);
    Task ReorderBacklogAsync(int[] ids, int? teamId);

    Task<(bool Success, string? Error)> SaveAttachmentRecordAsync(int taskId, string fileName, string storedName, long fileSize, string contentType, string uploadedBy);
    Task<(bool Success, string? Error)> DeleteAttachmentAsync(int id, string displayName, bool isAdmin);

    Task<BulkOpResult>       BulkDeleteAsync(int[] ids, UserCtx ctx);
    Task<BulkStatusOpResult> BulkChangeStatusAsync(int[] ids, string status, UserCtx ctx);
    Task<BulkOpResult>       BulkChangeOwnerAsync(int[] ids, string? owner, UserCtx ctx);
    Task<BulkSprintOpResult> BulkAssignSprintAsync(int[] ids, int? sprintId, UserCtx ctx);
    Task<BulkOpResult>       BulkChangePriorityAsync(int[] ids, string? priority, UserCtx ctx);

    Task<List<TaskItem>> GetRecycleBinAsync(int? teamId);
    Task<(bool Success, string? Error)> RestoreAsync(int id);
    Task<(bool Success, string? Error)> PermanentDeleteAsync(int id);
    Task<(bool Success, int Count)>     EmptyBinAsync(int? teamId);

    Task<BacklogResult>           GetBacklogAsync(string sprintFilter, int? teamId);
    Task<BurndownResult?>         GetBurndownAsync(int sprintId, int? teamId);
    Task<IEnumerable<object>>     GetSprintsAsync(int? teamId);
    Task<IEnumerable<object>>     GetParentItemsAsync(string itemType, int? teamId);
    Task<IEnumerable<object>>     SearchAsync(string q, int? teamId);
    Task<IEnumerable<string>>     GetLabelsAsync(int? teamId);
    Task<IEnumerable<object>>     GetModulesAsync(string? himCs);
    Task<IEnumerable<object>>     GetClientsAsync(int moduleId, string? himCs, bool includeContactDetails = false);
    Task<(byte[] Bytes, string FileName)> ExportCsvAsync(ExportFilters f, int? teamId);
    Task<TaskTeamViewData> GetTeamViewDataAsync(DateTime now, string? tlfrom, string? tlto, string? tlmode, int calYear, int calMonth, int? teamId);
}

// ── Result / DTO types ────────────────────────────────────────────────────────

public class TaskListFilters
{
    public string  Search   { get; set; } = "";
    public string  Project  { get; set; } = "";
    public string  Property { get; set; } = "";
    public string? Owner    { get; set; }
    public string  Status   { get; set; } = "";
    public string  Priority { get; set; } = "";
    public string  DateFrom { get; set; } = "";
    public string  DateTo   { get; set; } = "";
    public int     Page     { get; set; } = 1;
    public int     PageSize { get; set; } = 20;
    public string  Sort     { get; set; } = "";
    public string  SortDir  { get; set; } = "desc";
    public string  View     { get; set; } = "table";
    public int     CalYear  { get; set; }
    public int     CalMonth { get; set; }
    public string? TlFrom   { get; set; }
    public string? TlTo     { get; set; }
    public string? TlMode   { get; set; }
}

public class TaskListResult
{
    public TaskListViewModel ViewModel    { get; set; } = null!;
    public List<Sprint>      AllSprints   { get; set; } = [];
    public TaskTeamViewData? TeamViewData { get; set; }
}

public class TaskTeamViewData
{
    public string TlFrom            { get; set; } = "";
    public string TlTo              { get; set; } = "";
    public string TlMode            { get; set; } = "";
    public string TlOwners          { get; set; } = "[]";
    public string TlProjectColors   { get; set; } = "{}";
    public string TlPropertyColors  { get; set; } = "{}";
    public string TlTasks           { get; set; } = "[]";
}

public class TaskSaveResult
{
    public bool    Success         { get; set; }
    public string? Error           { get; set; }
    public int?    OpenChildren    { get; set; }
    public string? CapacityWarning { get; set; }
    public int     TaskId          { get; set; }
    public bool    Conflict        { get; set; }   // stale-edit / optimistic-concurrency failure
}

public class TaskUpdateResult
{
    public bool    Success         { get; set; }
    public string? Error           { get; set; }
    public bool    NeedOwner       { get; set; }
    public bool    NeedActualHours { get; set; }
    public int?    OpenChildren    { get; set; }
    public bool    Conflict        { get; set; }   // stale-edit / optimistic-concurrency failure
}

public class TaskSprintResult
{
    public bool    Success         { get; set; }
    public string? Error           { get; set; }
    public int?    SprintId        { get; set; }
    public string? CapacityWarning { get; set; }
}

public class WatcherResult
{
    public List<object> Watchers { get; set; } = [];
    public bool         Watching { get; set; }
    public int          Count    { get; set; }
}

public class WatchToggleResult
{
    public bool    Success  { get; set; }
    public bool    Watching { get; set; }
    public int     Count    { get; set; }
    public string? Error    { get; set; }
}

public class AttachmentDownload
{
    public string FilePath    { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string FileName    { get; set; } = "";
}

public class ChildStatsResult
{
    public int    Total    { get; set; }
    public int    Done     { get; set; }
    public int    Pct      { get; set; }
    public string LeafType { get; set; } = "items";
}

public class BulkOpResult
{
    public bool    Success { get; set; }
    public int     Count   { get; set; }
    public int     Denied  { get; set; }
    public string? Error   { get; set; }
}

public class BulkStatusOpResult : BulkOpResult
{
    public int NoOwner { get; set; }
    public int InvalidTransition { get; set; }
    public int NeedActualHours { get; set; }
    public int OpenChildren { get; set; }
}

public class TimeEntryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public decimal ActualHours { get; set; }
    public decimal? RemainingWorkHours { get; set; }
}
public class BulkSprintOpResult : BulkOpResult { public int Skipped { get; set; } }

public class BacklogResult
{
    public object Tasks   { get; set; } = null!;
    public object Sprints { get; set; } = null!;
}

public class BurndownResult
{
    public string       SprintName  { get; set; } = "";
    public int          Total       { get; set; }
    public decimal      TotalWeight { get; set; }
    public List<object> Days        { get; set; } = [];
    public List<object> Ideal       { get; set; } = [];
    public List<object> WeightDays  { get; set; } = [];
    public List<object> WeightIdeal { get; set; } = [];
    public bool IsHistoricalSnapshot { get; set; }
    public int CarriedForwardCount { get; set; }
}

public class ExportFilters
{
    public string  Search   { get; set; } = "";
    public string  Project  { get; set; } = "";
    public string  Property { get; set; } = "";
    public string? Owner    { get; set; }
    public string  Status   { get; set; } = "";
    public string  Priority { get; set; } = "";
    public string  DateFrom { get; set; } = "";
    public string  DateTo   { get; set; } = "";
}

// Bulk request bodies (moved from TasksController)
public class BulkStatusRequest   { public int[] Ids { get; set; } = []; public string  Status   { get; set; } = ""; }
public class BulkOwnerRequest    { public int[] Ids { get; set; } = []; public string? Owner    { get; set; } }
public class BulkSprintRequest   { public int[] Ids { get; set; } = []; public int?    SprintId { get; set; } }
public class BulkPriorityRequest { public int[] Ids { get; set; } = []; public string? Priority { get; set; } }
