using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("Tasks")]
public class TaskItem
{
    public int Id { get; set; }
    public string? Goal         { get; set; } = "";
    public string? Project      { get; set; } = "";
    public string? Property     { get; set; } = "";
    public string? TargetDate   { get; set; } = "";
    public string? Priority     { get; set; } = "";
    public string? Owner        { get; set; } = "";
    public string? Status       { get; set; } = "";
    public string? StartDate    { get; set; } = "";
    public string? EndDate      { get; set; } = "";
    public string? ActualStartDate { get; set; } = "";
    public string? ActualEndDate   { get; set; } = "";
    public string? RelatedFiles    { get; set; } = "";
    public string? Notes           { get; set; } = "";
    public DateTime CreatedAt   { get; set; } = DateTime.Now;
    public string? CreatedBy    { get; set; }
    public decimal? Weight      { get; set; }
    public int? ModuleId        { get; set; }
    public int? ClientId        { get; set; }
    public string? ItemType     { get; set; }   // Epic | Feature | Story | Task | Bug
    public int?    ParentId     { get; set; }   // self-reference for hierarchy
    public int?    SprintId     { get; set; }   // null = Backlog
    public int?    ExternalId   { get; set; }   // ADO Work Item ID (future sync)
    public string? Reporter            { get; set; }   // auto-set on create
    public string? Labels              { get; set; }   // comma-separated tags
    public string? AcceptanceCriteria  { get; set; }   // for Story type
    public string? Swimlane            { get; set; }   // "Expedite" | null (= Default)
    public int?    BacklogOrder        { get; set; }   // manual sort order in backlog view
    public bool      IsDeleted  { get; set; }
    public DateTime? DeletedAt  { get; set; }
    public string?   DeletedBy  { get; set; }
    public int?      TeamId     { get; set; }
}
