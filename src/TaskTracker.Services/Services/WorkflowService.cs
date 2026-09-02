using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

/// <summary>Publishes validated, append-only workflow revisions.</summary>
public sealed class WorkflowService(AppDbContext db) : IWorkflowService
{
    public async Task<WorkflowPublishResult> PublishAsync(WorkflowDraft draft, UserCtx ctx)
    {
        if (!ctx.IsTeamAdmin || (draft.TeamId.HasValue && draft.TeamId != ctx.TeamId && !ctx.IsAdmin))
            return new WorkflowPublishResult { Error = "You do not have permission to publish this workflow." };

        var itemType = draft.ItemType.Trim();
        if (itemType is not ("Epic" or "Feature" or "Story" or "Task" or "Bug"))
            return new WorkflowPublishResult { Error = "Workflow item type is invalid." };
        if (string.IsNullOrWhiteSpace(draft.Name) || draft.States.Count == 0)
            return new WorkflowPublishResult { Error = "A workflow name and at least one state are required." };

        var keys = draft.States.Select(s => s.Key.Trim()).ToList();
        var names = draft.States.Select(s => s.Name.Trim()).ToList();
        if (keys.Any(string.IsNullOrWhiteSpace) || names.Any(string.IsNullOrWhiteSpace)
            || keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != keys.Count
            || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            return new WorkflowPublishResult { Error = "Workflow state keys and names must be present and unique." };
        if (draft.States.Count(s => s.IsInitial) != 1)
            return new WorkflowPublishResult { Error = "A workflow must have exactly one initial state." };
        if (!draft.States.Any(s => s.IsTerminal))
            return new WorkflowPublishResult { Error = "A workflow must have at least one terminal state." };

        var knownKeys = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (draft.Transitions.Any(t => !knownKeys.Contains(t.FromStateKey.Trim()) || !knownKeys.Contains(t.ToStateKey.Trim())))
            return new WorkflowPublishResult { Error = "Every transition must reference a state in this workflow." };
        if (draft.Transitions.GroupBy(t => (t.FromStateKey.Trim(), t.ToStateKey.Trim()), StringTupleComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            return new WorkflowPublishResult { Error = "Workflow transitions must be unique." };

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync()
            : null;
        var version = (await db.WorkflowDefinitions
            .Where(w => w.TeamId == draft.TeamId && w.ItemType == itemType)
            .MaxAsync(w => (int?)w.Version) ?? 0) + 1;
        var definition = new WorkflowDefinition {
            TeamId = draft.TeamId, ItemType = itemType, Name = draft.Name.Trim(), Version = version,
            IsPublished = true, IsDefault = true, CreatedAt = DateTime.UtcNow, PublishedAt = DateTime.UtcNow
        };
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();

        var states = draft.States.Select(s => new WorkflowState {
            WorkflowDefinitionId = definition.Id, Key = s.Key.Trim(), Name = s.Name.Trim(),
            Category = s.Category.Trim(), SortOrder = s.SortOrder, IsInitial = s.IsInitial,
            IsTerminal = s.IsTerminal, RequiresOwner = s.RequiresOwner,
            RequiresActualHours = s.RequiresActualHours
        }).ToList();
        db.WorkflowStates.AddRange(states);
        await db.SaveChangesAsync();

        var stateIds = states.ToDictionary(s => s.Key, s => s.Id, StringComparer.OrdinalIgnoreCase);
        db.WorkflowTransitions.AddRange(draft.Transitions.Select(t => new WorkflowTransition {
            WorkflowDefinitionId = definition.Id,
            FromStateId = stateIds[t.FromStateKey.Trim()], ToStateId = stateIds[t.ToStateKey.Trim()],
            MemberCanTransition = t.MemberCanTransition
        }));
        await db.SaveChangesAsync();
        if (transaction != null)
            await transaction.CommitAsync();
        return new WorkflowPublishResult { Success = true, WorkflowDefinitionId = definition.Id, Version = version };
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> GetPublishedAsync(int? teamId, string itemType) =>
        await db.WorkflowDefinitions.AsNoTracking()
            .Where(w => w.IsPublished && w.ItemType == itemType && (w.TeamId == teamId || w.TeamId == null))
            .OrderByDescending(w => w.TeamId == teamId).ThenByDescending(w => w.Version)
            .ToListAsync();

    private sealed class StringTupleComparer : IEqualityComparer<(string From, string To)>
    {
        public static readonly StringTupleComparer OrdinalIgnoreCase = new();
        public bool Equals((string From, string To) x, (string From, string To) y) =>
            string.Equals(x.From, y.From, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.To, y.To, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string From, string To) value) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.From),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.To));
    }
}
