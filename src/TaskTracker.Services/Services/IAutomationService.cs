using TaskTracker.Models;

namespace TaskTracker.Services;

/// <summary>
/// Manages team automation rules and runs them as a best-effort side effect of task lifecycle
/// events. Execution must never fail the originating command; every action (or failure) is audited.
/// </summary>
public interface IAutomationService
{
    // ── Management ──────────────────────────────────────────────────────────
    Task<IReadOnlyList<AutomationRuleSummary>> ListAsync(int? teamId, bool includeGlobal, CancellationToken ct = default);
    Task<AutomationRule> CreateAsync(AutomationRuleInput input, CancellationToken ct = default);
    Task<bool> SetEnabledAsync(int id, bool enabled, int? teamId, bool isSysAdmin, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, int? teamId, bool isSysAdmin, CancellationToken ct = default);
    Task<IReadOnlyList<AutomationRunSummary>> GetRecentRunsAsync(int? teamId, bool includeGlobal, int maxCount = 100, CancellationToken ct = default);

    /// <summary>Dry-runs a rule against an existing task: reports whether the condition matches and what
    /// the action would do, without executing it or writing anything.</summary>
    Task<AutomationPreviewResult> PreviewAsync(int ruleId, int taskId, int? teamId, bool isSysAdmin, CancellationToken ct = default);

    // ── Execution (called from the task write path) ─────────────────────────
    Task RunForEventAsync(TaskItem task, string eventType, UserCtx ctx, CancellationToken ct = default);

    // ── Scheduled execution (called by the background sweep) ────────────────
    /// <summary>Evaluates time-based rules (e.g. "due soon") across all teams. Returns the number of
    /// actions taken.</summary>
    Task<int> RunScheduledAsync(CancellationToken ct = default);
}

public sealed record AutomationRuleInput(
    string Name, int TeamId, string TriggerType,
    string? ConditionField, string? ConditionOperator, string? ConditionValue,
    string ActionType, string? ActionConfig, string? CreatedBy);

public sealed record AutomationRuleSummary(
    int Id, string Name, int TeamId, string? TeamName, bool IsEnabled,
    string TriggerType, string? ConditionField, string? ConditionOperator, string? ConditionValue,
    string ActionType, string? ActionConfig, int RunCount, DateTime? LastRunAt, DateTime CreatedAt, string? CreatedBy);

public sealed record AutomationRunSummary(
    long Id, int RuleId, string RuleName, int TaskId, string EventType,
    string Status, string? Detail, DateTime OccurredAt, string? OccurredBy);

public sealed record AutomationPreviewResult(
    bool RuleFound, bool TaskFound, bool Matched, string? RuleName, string? TaskGoal, string Description);
