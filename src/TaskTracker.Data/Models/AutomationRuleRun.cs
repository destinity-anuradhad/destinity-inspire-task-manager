using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>
/// One audited execution of an <see cref="AutomationRule"/> against a task. Written whenever a rule
/// actually acts (or fails), giving administrators a trail of what automation did and why.
/// </summary>
[Table("AutomationRuleRuns")]
public class AutomationRuleRun
{
    public long Id { get; set; }
    public int RuleId { get; set; }
    public int TaskId { get; set; }
    [MaxLength(50)]   public string EventType { get; set; } = "";
    /// <summary><c>Executed</c> or <c>Failed</c>.</summary>
    [MaxLength(20)]   public string Status { get; set; } = "";
    [MaxLength(1000)] public string? Detail { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)]  public string? OccurredBy { get; set; }
}
