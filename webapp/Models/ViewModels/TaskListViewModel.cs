namespace TaskTracker.Models.ViewModels;

public class TaskListViewModel
{
    public List<TaskItem>     Tasks      { get; set; } = [];
    public List<ProjectItem>  Projects   { get; set; } = [];
    public List<PropertyItem> Properties { get; set; } = [];
    public List<OwnerItem>    Owners     { get; set; } = [];

    // Filters
    public string Search    { get; set; } = "";
    public string Project   { get; set; } = "";
    public string Property  { get; set; } = "";
    public string Owner     { get; set; } = "";
    public string Status    { get; set; } = "";
    public string Priority  { get; set; } = "";
    public string DateFrom  { get; set; } = "";
    public string DateTo    { get; set; } = "";

    // Pagination
    public int Page         { get; set; } = 1;
    public int PageSize     { get; set; } = 20;
    public int TotalCount   { get; set; }
    public int TotalPages   => (int)Math.Ceiling((double)TotalCount / PageSize);

    // Sorting
    public string Sort      { get; set; } = "";
    public string SortDir   { get; set; } = "desc";

    // View mode: table | kanban | calendar
    public string ViewMode  { get; set; } = "table";

    // Calendar
    public int CalYear      { get; set; } = DateTime.Now.Year;
    public int CalMonth     { get; set; } = DateTime.Now.Month;

    // Status counts (computed before status filter)
    public Dictionary<string, int> StatusCounts { get; set; } = [];

    public List<TaskTracker.Models.WorkState> WorkStates { get; set; } = [];

    public static readonly string[] AllStatuses   = ["Not Started", "On track", "Testing", "On hold", "Delayed", "Done"];
    public static readonly string[] AllPriorities = ["P0", "P1", "P2", "P3"];
}
