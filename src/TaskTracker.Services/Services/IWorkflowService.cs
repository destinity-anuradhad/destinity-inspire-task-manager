using TaskTracker.Models;

namespace TaskTracker.Services;

public interface IWorkflowService
{
    Task<WorkflowPublishResult> PublishAsync(WorkflowDraft draft, UserCtx ctx);
    Task<IReadOnlyList<WorkflowDefinition>> GetPublishedAsync(int? teamId, string itemType);
}

public sealed class WorkflowDraft
{
    public int? TeamId { get; init; }
    public string ItemType { get; init; } = "Task";
    public string Name { get; init; } = "Workflow";
    public IReadOnlyList<WorkflowStateDraft> States { get; init; } = [];
    public IReadOnlyList<WorkflowTransitionDraft> Transitions { get; init; } = [];
}

public sealed class WorkflowStateDraft
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "NotStarted";
    public int SortOrder { get; init; }
    public bool IsInitial { get; init; }
    public bool IsTerminal { get; init; }
    public bool RequiresOwner { get; init; }
    public bool RequiresActualHours { get; init; }
}

public sealed class WorkflowTransitionDraft
{
    public string FromStateKey { get; init; } = "";
    public string ToStateKey { get; init; } = "";
    public bool MemberCanTransition { get; init; } = true;
}

public sealed class WorkflowPublishResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int WorkflowDefinitionId { get; init; }
    public int Version { get; init; }
}
