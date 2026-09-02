namespace TaskTracker.Services;

/// <summary>Server-side hierarchy policy for the supported work-item taxonomy.</summary>
public static class TaskHierarchyRules
{
    public static bool CanParentContain(string? parentType, string? childType) =>
        (parentType, childType) switch
        {
            ("Epic", "Feature") => true,
            ("Feature", "Story") => true,
            ("Story", "Task") or ("Story", "Bug") => true,
            _ => false
        };
}
