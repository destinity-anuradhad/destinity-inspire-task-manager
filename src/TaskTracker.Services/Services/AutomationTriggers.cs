namespace TaskTracker.Services;

/// <summary>
/// The vocabulary of the automation rule engine: which lifecycle events can trigger a rule, which
/// task fields a condition may test, the comparison operators, and the safe action set. Everything
/// here is an allow-list — a rule can only reference values defined in this class, which is what
/// keeps automation from becoming an arbitrary code-execution surface.
/// </summary>
public static class AutomationTriggers
{
    // Event triggers mirror the deliverable webhook events (same string values, kept in lock-step).
    // They fire synchronously from the task write path.
    public const string TaskCreated       = WebhookEvents.TaskCreated;
    public const string TaskStatusChanged = WebhookEvents.TaskStatusChanged;
    public const string TaskAssigned      = WebhookEvents.TaskAssigned;
    public const string TaskCommentAdded  = WebhookEvents.TaskCommentAdded;

    // Scheduled triggers are evaluated by the background sweep, not by any single task write.
    public const string TaskDueSoon       = "task.due_soon";

    /// <summary>Triggers fired synchronously by task commands.</summary>
    public static readonly IReadOnlyList<string> EventTriggers =
        [TaskCreated, TaskStatusChanged, TaskAssigned, TaskCommentAdded];

    /// <summary>Triggers evaluated periodically by the automation background sweep.</summary>
    public static readonly IReadOnlyList<string> ScheduledTriggers =
        [TaskDueSoon];

    /// <summary>All selectable triggers, in display order.</summary>
    public static readonly IReadOnlyList<string> Triggers =
        [TaskCreated, TaskStatusChanged, TaskAssigned, TaskCommentAdded, TaskDueSoon];

    /// <summary>How far ahead a task's target date counts as "due soon".</summary>
    public const int DueSoonWindowDays = 1;

    // Condition fields — read from the task through a fixed switch, never reflection.
    public const string FieldAny      = "Any";
    public const string FieldPriority = "Priority";
    public const string FieldStatus   = "Status";
    public const string FieldItemType = "ItemType";
    public const string FieldOwner    = "Owner";

    public static readonly IReadOnlyList<string> Fields =
        [FieldAny, FieldPriority, FieldStatus, FieldItemType, FieldOwner];

    // Condition operators.
    public const string OpAny       = "any";
    public const string OpEquals    = "eq";
    public const string OpNotEquals = "neq";
    public const string OpContains  = "contains";

    public static readonly IReadOnlyList<string> Operators =
        [OpAny, OpEquals, OpNotEquals, OpContains];

    // Actions — deliberately narrow and side-effect-safe. None of them re-enters the task write
    // pipeline, so a rule can never trigger another round of automation (see AutomationService).
    public const string ActionNotifyOwner    = "notify_owner";
    public const string ActionNotifyWatchers = "notify_watchers";
    public const string ActionAddComment     = "add_comment";
    public const string ActionSetPriority    = "set_priority";
    public const string ActionAddLabel       = "add_label";

    public static readonly IReadOnlyList<string> Actions =
        [ActionNotifyOwner, ActionNotifyWatchers, ActionAddComment, ActionSetPriority, ActionAddLabel];

    /// <summary>Hard cap on how many rules a single event may run — a safety valve against a
    /// misconfigured team slowing every task write to a crawl.</summary>
    public const int MaxRulesPerEvent = 25;

    public static bool IsValidTrigger(string? value)  => value is not null && Triggers.Contains(value);
    public static bool IsScheduledTrigger(string? value) => value is not null && ScheduledTriggers.Contains(value);
    public static bool IsValidField(string? value)    => value is not null && Fields.Contains(value);
    public static bool IsValidOperator(string? value) => value is not null && Operators.Contains(value);
    public static bool IsValidAction(string? value)   => value is not null && Actions.Contains(value);

    /// <summary>Human-friendly label for a trigger/field/operator/action code, for display.</summary>
    public static string Label(string code) => code switch
    {
        TaskCreated       => "Task created",
        TaskStatusChanged => "Status changed",
        TaskAssigned      => "Owner assigned",
        TaskCommentAdded  => "Comment added",
        TaskDueSoon       => "Due soon",
        FieldAny          => "(any task)",
        OpAny             => "always",
        OpEquals          => "equals",
        OpNotEquals       => "is not",
        OpContains        => "contains",
        ActionNotifyOwner    => "Notify owner",
        ActionNotifyWatchers => "Notify watchers",
        ActionAddComment     => "Add comment",
        ActionSetPriority    => "Set priority",
        ActionAddLabel       => "Add label",
        _                 => code,
    };
}
