using TaskTracker.Models;

namespace TaskTracker.Services;

/// <summary>
/// Manages team SLA policies and reports compliance. Evaluation is read-only over existing task
/// fields (created/priority/type/status/completion) — it never changes a task.
/// </summary>
public interface ISlaService
{
    // ── Management ──────────────────────────────────────────────────────────
    Task<IReadOnlyList<SlaPolicySummary>> ListAsync(int? teamId, bool includeGlobal, CancellationToken ct = default);
    Task<SlaPolicy> CreateAsync(SlaPolicyInput input, CancellationToken ct = default);
    Task<bool> SetEnabledAsync(int id, bool enabled, int? teamId, bool isSysAdmin, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, int? teamId, bool isSysAdmin, CancellationToken ct = default);

    // ── Reporting ───────────────────────────────────────────────────────────
    /// <summary>Compliance snapshot for a team's open work against its SLA policies.</summary>
    Task<SlaReport> GetReportAsync(int teamId, CancellationToken ct = default);
}

/// <summary>The compliance state of one task against its governing policy.</summary>
public static class SlaStatuses
{
    public const string OnTrack  = "OnTrack";   // open, comfortably within target
    public const string AtRisk   = "AtRisk";    // open, target approaching
    public const string Breached = "Breached";  // target already passed
    public const string Met      = "Met";       // done within target
}

public sealed record SlaPolicyInput(
    string Name, int TeamId, string? AppliesToItemType, string? AppliesToPriority, int TargetHours, string? CreatedBy);

public sealed record SlaPolicySummary(
    int Id, string Name, int TeamId, string? TeamName, bool IsEnabled,
    string? AppliesToItemType, string? AppliesToPriority, int TargetHours, DateTime CreatedAt, string? CreatedBy);

/// <summary>Pure evaluation of a task against a policy at a point in time.</summary>
public sealed record SlaEvaluation(string Status, DateTime Deadline, double HoursRemaining, bool IsOpen)
{
    public bool IsBreached => Status == SlaStatuses.Breached;
}

public sealed record SlaReportItem(
    int TaskId, string? Goal, string? ItemType, string? Priority, string? Status, string? Owner,
    string PolicyName, int TargetHours, string SlaStatus, DateTime Deadline, double HoursRemaining);

public sealed record SlaReport(
    int TeamId, int Total, int OnTrack, int AtRisk, int Breached, IReadOnlyList<SlaReportItem> Items);
