using TaskTracker.Models;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services;

public interface IDashboardService
{
    Task<PersonalDashboardData> GetPersonalAsync(string displayName, int? teamId);
    Task<TeamDashboardData>     GetTeamAsync(int? teamId);
    Task<List<TeamStat>>        GetAllTeamsStatsAsync();
}

public class TeamStat
{
    public Team Team        { get; set; } = null!;
    public int  MemberCount { get; set; }
    public int  Total       { get; set; }
    public int  Done        { get; set; }
    public int  Active      { get; set; }
    public int  Overdue     { get; set; }
    public int  DueToday    { get; set; }
    public int  CompletePct { get; set; }
}

public class PersonalDashboardData
{
    public int Total         { get; set; }
    public int Done          { get; set; }
    public int InProgress    { get; set; }
    public int Open          { get; set; }
    public int Overdue       { get; set; }
    public int DueToday      { get; set; }
    public int ActiveSprintDueToday { get; set; }
    public int CompletionPct { get; set; }
    public List<TaskItem> Pending      { get; set; } = [];
    public List<TaskItem> WorkingOn    { get; set; } = [];
    public Dictionary<string, int> ByType     { get; set; } = [];
    public Dictionary<string, int> ByPriority { get; set; } = [];
    public string?      BestProject      { get; set; }
    public int          BestProjectCount  { get; set; }
    public int          BestProjectDone   { get; set; }
    public List<ActivityLog> Activity   { get; set; } = [];
    public TaskItem?         LastTask   { get; set; }
    public ActivityLog?      LastActLog { get; set; }
}

public class TeamDashboardData
{
    public int Total      { get; set; }
    public int Done       { get; set; }
    public int Overdue    { get; set; }
    public int InProgress { get; set; }
    public int DueToday { get; set; }
    public string StatusLabels    { get; set; } = "[]";
    public string StatusValues    { get; set; } = "[]";
    public string ProjectLabels   { get; set; } = "[]";
    public string ProjectValues   { get; set; } = "[]";
    public string TypeLabels      { get; set; } = "[]";
    public string TypeValues      { get; set; } = "[]";
    public string PriorityLabels  { get; set; } = "[]";
    public string PriorityValues  { get; set; } = "[]";
    public List<OwnerStat>            OwnerStats    { get; set; } = [];
    public Dictionary<string, string> OwnerColorMap { get; set; } = [];
    public Dictionary<string, string> OwnerImageMap { get; set; } = [];
    public string OwnerNames    { get; set; } = "[]";
    public string OwnerDone     { get; set; } = "[]";
    public string OwnerPending  { get; set; } = "[]";
    public List<ActivityLog> RecentActivity { get; set; } = [];
}
