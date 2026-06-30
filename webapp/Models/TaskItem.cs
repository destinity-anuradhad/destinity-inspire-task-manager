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
}
