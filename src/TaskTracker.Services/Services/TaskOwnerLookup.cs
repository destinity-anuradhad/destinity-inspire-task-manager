using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;

namespace TaskTracker.Services;

/// <summary>
/// Shared read helper for #7 (owner-string removal): resolves a task's owner **display names** from
/// the authoritative <c>TaskOwners</c> junction rather than the denormalized <c>Owner</c> string.
/// Soft-deleted tasks' owners are included (junction rows aren't removed on soft delete), matching the
/// old behaviour of reading the string off history rows.
/// </summary>
public static class TaskOwnerLookup
{
    public static async Task<Dictionary<int, List<string>>> OwnerNamesByTaskAsync(
        this AppDbContext db, IEnumerable<int> taskIds, CancellationToken ct = default)
    {
        var ids = taskIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return (await db.TaskOwners
                .Where(o => ids.Contains(o.TaskId))
                .Join(db.AppUsers, o => o.UserId, u => u.Id, (o, u) => new { o.TaskId, u.DisplayName })
                .ToListAsync(ct))
            .GroupBy(x => x.TaskId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }
}
