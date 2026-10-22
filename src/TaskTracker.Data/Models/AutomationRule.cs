using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

/// <summary>
/// A team-scoped "WHEN &lt;trigger&gt; (and optional condition) THEN &lt;action&gt;" rule. Rules are
/// evaluated as a best-effort side effect of task lifecycle events, on the same event set that
/// drives webhooks. The condition is a fixed field/operator/value triple (never arbitrary code),
/// and the action is drawn from a small, safe, allow-listed set — so a rule can never run away.
/// </summary>
[Table("AutomationRules")]
public class AutomationRule
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    public bool IsEnabled { get; set; } = true;

    /// <summary>The lifecycle event that fires this rule (e.g. <c>task.status_changed</c>).</summary>
    [MaxLength(50)] public string TriggerType { get; set; } = "";

    // Optional condition — an allow-listed field compared to a literal value. A null/"any"
    // operator (or "Any" field) makes the rule unconditional.
    [MaxLength(50)]  public string? ConditionField { get; set; }
    [MaxLength(20)]  public string? ConditionOperator { get; set; }
    [MaxLength(200)] public string? ConditionValue { get; set; }

    /// <summary>The action to perform when the trigger fires and the condition matches.</summary>
    [MaxLength(50)]  public string ActionType { get; set; } = "";
    /// <summary>Action parameter (notification text, priority value, label, …).</summary>
    [MaxLength(500)] public string? ActionConfig { get; set; }

    public int SortOrder { get; set; }
    public int RunCount { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)] public string? CreatedBy { get; set; }
}
