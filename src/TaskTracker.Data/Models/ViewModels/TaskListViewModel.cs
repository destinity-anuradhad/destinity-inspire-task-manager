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
    public string Sprint    { get; set; } = "";   // "" = all, sprint id, or "__none__" = backlog

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

    // Sum of estimate (Weight) and actual hours across the ENTIRE filtered set (all pages),
    // so the table header total reflects the current filters, not just the visible page.
    public decimal TotalEstimateHours { get; set; }
    public decimal TotalActualHours   { get; set; }

    // Comment counts for displayed tasks (table view)
    public Dictionary<int, int> CommentCounts { get; set; } = [];

    public List<TaskTracker.Models.WorkState> WorkStates { get; set; } = [];

    public static readonly string[] AllStatuses   = ["New", "To Do", "Active", "In Progress", "Resolved", "Done", "Closed"];
    public static readonly string[] AllPriorities = ["Critical", "High", "Medium", "Low"];
}
