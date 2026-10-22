using Microsoft.EntityFrameworkCore;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class TaskServiceTests
{
    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ReturnsTask_WhenExists()
    {
        using var db   = DbContextFactory.Create(nameof(GetByIdAsync_ReturnsTask_WhenExists));
        var task = new TaskItem { Goal = "Build invoice export", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateTaskService(db);
        var result = await svc.GetByIdAsync(task.Id, teamId: null);

        Assert.NotNull(result);
        Assert.Equal("Build invoice export", result.Goal);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        using var db  = DbContextFactory.Create(nameof(GetByIdAsync_ReturnsNull_WhenNotFound));
        var svc    = ServiceFactory.CreateTaskService(db);
        var result = await svc.GetByIdAsync(9999, teamId: null);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenTeamMismatch()
    {
        using var db   = DbContextFactory.Create(nameof(GetByIdAsync_ReturnsNull_WhenTeamMismatch));
        var task = new TaskItem { Goal = "Team-scoped task", TeamId = 1, CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateTaskService(db);
        var result = await svc.GetByIdAsync(task.Id, teamId: 2);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenSoftDeleted()
    {
        using var db   = DbContextFactory.Create(nameof(GetByIdAsync_ReturnsNull_WhenSoftDeleted));
        // Must bypass the global query filter to insert a soft-deleted row
        var task = new TaskItem { Goal = "Deleted task", IsDeleted = true, DeletedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateTaskService(db);
        var result = await svc.GetByIdAsync(task.Id, teamId: null);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetBurndownAsync_UsesImmutableSnapshotForCompletedSprint()
    {
        using var db = DbContextFactory.Create(nameof(GetBurndownAsync_UsesImmutableSnapshotForCompletedSprint));
        var sprint = new Sprint {
            Name = "Historic", TeamId = 1, StartDate = DateTime.UtcNow.AddDays(-5),
            CompletedAt = DateTime.UtcNow, IsActive = false
        };
        db.Sprints.Add(sprint);
        await db.SaveChangesAsync();
        db.SprintCompletionSnapshots.Add(new SprintCompletionSnapshot {
            SprintId = sprint.Id, CommittedCount = 5, CompletedCount = 3, CarriedForwardCount = 2,
            CommittedEstimate = 13, CompletedEstimate = 8
        });
        await db.SaveChangesAsync();

        var result = await ServiceFactory.CreateTaskService(db).GetBurndownAsync(sprint.Id, 1);

        Assert.NotNull(result);
        Assert.True(result.IsHistoricalSnapshot);
        Assert.Equal(5, result.Total);
        Assert.Equal(13, result.TotalWeight);
        Assert.Equal(2, result.CarriedForwardCount);
    }

    // ── GetListAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetListAsync_FiltersByStatus()
    {
        using var db  = DbContextFactory.Create(nameof(GetListAsync_FiltersByStatus));
        db.Tasks.AddRange(
            new TaskItem { Goal = "Open",   Status = "New",         CreatedAt = DateTime.UtcNow, RowVersion = [] },
            new TaskItem { Goal = "Closed", Status = "Done",        CreatedAt = DateTime.UtcNow, RowVersion = [] },
            new TaskItem { Goal = "Active", Status = "In Progress",  CreatedAt = DateTime.UtcNow, RowVersion = [] }
        );
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateTaskService(db);
        var result = await svc.GetListAsync(new TaskListFilters { Status = "Done" }, teamId: null);

        Assert.Equal(1, result.ViewModel.TotalCount);
        Assert.All(result.ViewModel.Tasks, t => Assert.Equal("Done", t.Status));
    }

    [Fact]
    public async Task GetListAsync_SearchesGoalField()
    {
        using var db  = DbContextFactory.Create(nameof(GetListAsync_SearchesGoalField));
        db.Tasks.AddRange(
            new TaskItem { Goal = "invoice export module", CreatedAt = DateTime.UtcNow, RowVersion = [] },
            new TaskItem { Goal = "user login page",       CreatedAt = DateTime.UtcNow, RowVersion = [] },
            new TaskItem { Goal = "fix invoice rounding",  CreatedAt = DateTime.UtcNow, RowVersion = [] }
        );
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateTaskService(db);
        var result = await svc.GetListAsync(new TaskListFilters { Search = "invoice" }, teamId: null);

        Assert.Equal(2, result.ViewModel.TotalCount);
        Assert.All(result.ViewModel.Tasks, t => Assert.Contains("invoice", t.Goal!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetListAsync_ExcludesSoftDeletedTasks()
    {
        using var db  = DbContextFactory.Create(nameof(GetListAsync_ExcludesSoftDeletedTasks));
        db.Tasks.AddRange(
            new TaskItem { Goal = "Active task",  CreatedAt = DateTime.UtcNow, RowVersion = [] },
            new TaskItem { Goal = "Deleted task", IsDeleted = true, DeletedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, RowVersion = [] }
        );
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateTaskService(db);
        var result = await svc.GetListAsync(new TaskListFilters(), teamId: null);

        Assert.Equal(1, result.ViewModel.TotalCount);
        Assert.Equal("Active task", result.ViewModel.Tasks[0].Goal);
    }

    [Fact]
    public async Task GetListAsync_ScopesToTeam()
    {
        using var db  = DbContextFactory.Create(nameof(GetListAsync_ScopesToTeam));
        db.Tasks.AddRange(
            new TaskItem { Goal = "Team A task",  TeamId = 1,    CreatedAt = DateTime.UtcNow, RowVersion = [] },
            new TaskItem { Goal = "Team B task",  TeamId = 2,    CreatedAt = DateTime.UtcNow, RowVersion = [] },
            new TaskItem { Goal = "No-team task", TeamId = null, CreatedAt = DateTime.UtcNow, RowVersion = [] }
        );
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateTaskService(db);
        var result = await svc.GetListAsync(new TaskListFilters(), teamId: 1);

        Assert.Equal(1, result.ViewModel.TotalCount);
        Assert.Equal("Team A task", result.ViewModel.Tasks[0].Goal);
    }

    [Fact]
    public async Task GetListAsync_PaginatesResults()
    {
        using var db  = DbContextFactory.Create(nameof(GetListAsync_PaginatesResults));
        for (int i = 1; i <= 25; i++)
            db.Tasks.Add(new TaskItem { Goal = $"Task {i:D2}", CreatedAt = DateTime.UtcNow.AddMinutes(-i), RowVersion = [] });
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var p1  = await svc.GetListAsync(new TaskListFilters { Page = 1, PageSize = 10 }, teamId: null);
        var p2  = await svc.GetListAsync(new TaskListFilters { Page = 2, PageSize = 10 }, teamId: null);
        var p3  = await svc.GetListAsync(new TaskListFilters { Page = 3, PageSize = 10 }, teamId: null);

        Assert.Equal(25, p1.ViewModel.TotalCount);
        Assert.Equal(10, p1.ViewModel.Tasks.Count);
        Assert.Equal(10, p2.ViewModel.Tasks.Count);
        Assert.Equal(5,  p3.ViewModel.Tasks.Count);
    }

    // ── SaveAsync (create) ────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_CreateTask_AssignsMemberAsOwnerAndSyncsJoinTable()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_CreateTask_AssignsMemberAsOwnerAndSyncsJoinTable));
        var user = new AppUser { Username = "priya", DisplayName = "Priya" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Priya", "priya", IsAdmin: false, TeamRole: "", TeamId: 1);
        var result = await svc.SaveAsync(new TaskItem { Goal = "New task", Status = "New", RowVersion = [] }, ctx, forceClose: false);

        Assert.True(result.Success, result.Error);
        var owners = await db.TaskOwners.ToListAsync();
        Assert.Contains(owners, o => o.UserId == user.Id);
    }

    [Fact]
    public async Task SaveAsync_CreateTask_RejectsActiveStatusWithNoOwner()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_CreateTask_RejectsActiveStatusWithNoOwner));
        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);
        var result = await svc.SaveAsync(
            new TaskItem { Goal = "Task without owner", Status = "Active" }, ctx, forceClose: false);

        Assert.False(result.Success);
        Assert.Contains("owner", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_CreateTask_RejectsUnknownOwnerWithoutPersistingTask()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_CreateTask_RejectsUnknownOwnerWithoutPersistingTask));
        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);

        var result = await svc.SaveAsync(
            new TaskItem { Goal = "Bad assignment", Owner = "Missing User", Status = "New" },
            ctx,
            forceClose: false);

        Assert.False(result.Success);
        Assert.Contains("exactly one", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.Tasks.ToListAsync());
        Assert.Empty(await db.TaskOwners.ToListAsync());
    }

    [Fact]
    public async Task SaveAsync_CreateTask_RejectsUnknownEstimateUnit()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_CreateTask_RejectsUnknownEstimateUnit));
        var svc = ServiceFactory.CreateTaskService(db);
        var result = await svc.SaveAsync(new TaskItem {
            Goal = "Invalid unit", Status = "New", EstimateUnit = "Days"
        }, new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1), forceClose: false);

        Assert.False(result.Success);
        Assert.Contains("Estimate unit", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_CreateTask_UsesGovernedProjectNameAndIdentity()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_CreateTask_UsesGovernedProjectNameAndIdentity));
        var project = new ProjectItem { Name = "Payments", TeamId = 1, Status = "Active" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var result = await ServiceFactory.CreateTaskService(db).SaveAsync(new TaskItem {
            Goal = "Build checkout", Status = "New", ProjectId = project.Id, Project = "Untrusted label", RowVersion = []
        }, new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1), forceClose: false);

        Assert.True(result.Success, result.Error);
        var task = Assert.Single(db.Tasks);
        Assert.Equal(project.Id, task.ProjectId);
        Assert.Equal("Payments", task.Project);
    }

    [Fact]
    public async Task SaveAsync_CreateTask_RejectsGovernedProjectFromAnotherTeam()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_CreateTask_RejectsGovernedProjectFromAnotherTeam));
        var project = new ProjectItem { Name = "Other", TeamId = 2, Status = "Active" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var result = await ServiceFactory.CreateTaskService(db).SaveAsync(new TaskItem {
            Goal = "Cross-team", Status = "New", ProjectId = project.Id
        }, new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1), forceClose: false);

        Assert.False(result.Success);
        Assert.Contains("current team", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChecklistLifecycle_TracksCompletionAndRejectsUnauthorizedChanges()
    {
        using var db = DbContextFactory.Create(nameof(ChecklistLifecycle_TracksCompletionAndRejectsUnauthorizedChanges));
        var owner = new AppUser { Username = "priya", DisplayName = "Priya" };
        db.AppUsers.Add(owner);
        var task = new TaskItem { Goal = "Deploy", Owner = "Priya", TeamId = 1, Status = "New", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        db.TaskOwners.Add(new TaskOwner { TaskId = task.Id, UserId = owner.Id });
        await db.SaveChangesAsync();
        var service = ServiceFactory.CreateTaskService(db);
        var ownerCtx = new UserCtx(owner.Id, "Priya", "priya", false, "", 1);

        Assert.True((await service.AddChecklistItemAsync(task.Id, "Run smoke tests", ownerCtx)).Success);
        var item = Assert.Single(db.TaskChecklistItems);
        Assert.True((await service.ToggleChecklistItemAsync(item.Id, true, ownerCtx)).Success);
        Assert.True(item.IsCompleted);
        Assert.Equal("Priya", item.CompletedBy);
        Assert.NotNull(item.CompletedAt);

        var denied = await service.DeleteChecklistItemAsync(item.Id, new UserCtx(0, "Kasun", "kasun", false, "", 1));
        Assert.False(denied.Success);
        Assert.Single(db.TaskChecklistItems);
    }

    [Fact]
    public async Task SaveAsync_UpdateTask_PersistsRemainingWorkHours()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_UpdateTask_PersistsRemainingWorkHours));
        var task = new TaskItem {
            Goal = "Track remaining work", Status = "New", TeamId = 1,
            RemainingWorkHours = 8, CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var result = await svc.SaveAsync(new TaskItem {
            Id = task.Id, Goal = task.Goal, Status = "New", TeamId = 1,
            RemainingWorkHours = 3.5m
        }, new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1), forceClose: false);

        Assert.True(result.Success, result.Error);
        Assert.Equal(3.5m, (await db.Tasks.FindAsync(task.Id))!.RemainingWorkHours);
    }

    [Fact]
    public async Task SaveAsync_CreateTask_RejectsNegativeRemainingWorkHours()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_CreateTask_RejectsNegativeRemainingWorkHours));
        var result = await ServiceFactory.CreateTaskService(db).SaveAsync(new TaskItem {
            Goal = "Invalid remaining", Status = "New", RemainingWorkHours = -1
        }, new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1), forceClose: false);

        Assert.False(result.Success);
        Assert.Contains("Remaining work", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_UpdateTask_DeniesNonOwnerNonAdmin()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_UpdateTask_DeniesNonOwnerNonAdmin));
        var owner = new AppUser { Username = "kasun", DisplayName = "Kasun" };
        db.AppUsers.Add(owner);
        var task = new TaskItem { Goal = "Kasun task", Owner = "Kasun", CreatedBy = "Kasun", TeamId = 1, CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        db.TaskOwners.Add(new TaskOwner { TaskId = task.Id, UserId = owner.Id });
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Priya", "priya", IsAdmin: false, TeamRole: "", TeamId: 1);
        var update = new TaskItem { Id = task.Id, Goal = "Modified", Owner = "Kasun", Status = "New", TeamId = 1 };
        var result = await svc.SaveAsync(update, ctx, forceClose: false);

        Assert.False(result.Success);
        Assert.Contains("permission", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_UpdateTask_AllowsOwnerViaJoinTable()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_UpdateTask_AllowsOwnerViaJoinTable));
        var user = new AppUser { Username = "kasun", DisplayName = "Kasun" };
        db.AppUsers.Add(user);
        var task = new TaskItem { Goal = "Original goal", Owner = "Kasun", Status = "New", CreatedBy = "Admin", TeamId = 1, CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        db.TaskOwners.Add(new TaskOwner { TaskId = task.Id, UserId = user.Id });
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(user.Id, "Kasun", "kasun", IsAdmin: false, TeamRole: "", TeamId: 1);
        // Keep same Goal to avoid ExecuteUpdateAsync (not supported by InMemory provider)
        var update = new TaskItem { Id = task.Id, Goal = "Original goal", Owner = "Kasun", Status = "New", TeamId = 1 };
        var result = await svc.SaveAsync(update, ctx, forceClose: false);

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task SaveAsync_UpdateTask_DoesNotTreatLegacyOwnerNameAsAuthorization()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_UpdateTask_DoesNotTreatLegacyOwnerNameAsAuthorization));
        var user = new AppUser { Username = "kasun", DisplayName = "Kasun" };
        db.AppUsers.Add(user);
        var task = new TaskItem { Goal = "Legacy-only", Owner = "Kasun", CreatedBy = "Admin", TeamId = 1, CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(user.Id, user.DisplayName, user.Username, IsAdmin: false, TeamRole: "", TeamId: 1);
        var result = await svc.SaveAsync(
            new TaskItem { Id = task.Id, Goal = task.Goal, Owner = task.Owner, Status = "New", TeamId = 1 },
            ctx,
            forceClose: false);

        Assert.False(result.Success);
        Assert.Contains("permission", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_CreateTask_RejectsInvalidHierarchyType()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_CreateTask_RejectsInvalidHierarchyType));
        var feature = new TaskItem {
            Goal = "Feature", ItemType = "Feature", Status = "New", TeamId = 1,
            CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        db.Tasks.Add(feature);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var result = await svc.SaveAsync(new TaskItem {
            Goal = "Task cannot be direct Feature child", ItemType = "Task", Status = "New", ParentId = feature.Id
        }, new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1), forceClose: false);

        Assert.False(result.Success);
        Assert.Contains("cannot contain", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_UpdateTask_CannotBypassCompletionHoursRequirement()
    {
        using var db = DbContextFactory.Create(nameof(SaveAsync_UpdateTask_CannotBypassCompletionHoursRequirement));
        var task = new TaskItem {
            Goal = "Ready for QA", ItemType = "Task", Status = "In QA", Owner = "Priya",
            TeamId = 1, CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);
        var result = await svc.SaveAsync(new TaskItem {
            Id = task.Id, Goal = task.Goal, ItemType = "Task", Status = "Done", Owner = "Priya", TeamId = 1
        }, ctx, forceClose: false);

        Assert.False(result.Success);
        Assert.Contains("actual hours", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("In QA", (await db.Tasks.FindAsync(task.Id))!.Status);
    }

    [Fact]
    public async Task UpdateStatusAsync_CompletionStampsActualEndDate()
    {
        using var db = DbContextFactory.Create(nameof(UpdateStatusAsync_CompletionStampsActualEndDate));
        var task = new TaskItem {
            Goal = "Ready for QA", ItemType = "Task", Status = "In QA", Owner = "Priya",
            TeamId = 1, ActualStartDate = DateTime.UtcNow.AddDays(-1), CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);
        var result = await svc.UpdateStatusAsync(task.Id, "Done", null, ctx, forceClose: false, actualHours: 2);

        Assert.True(result.Success, result.Error);
        var updated = await db.Tasks.FindAsync(task.Id);
        Assert.Equal(2, updated!.ActualHours);
        Assert.NotNull(updated.ActualEndDate);
    }

    [Fact]
    public async Task LogTime_AfterCarryForward_DoesNotDoubleCountPreviousSprintHours()
    {
        using var db = DbContextFactory.Create(nameof(LogTime_AfterCarryForward_DoesNotDoubleCountPreviousSprintHours));
        var s1 = new Sprint { Name = "S1", TeamId = 1, IsActive = true };
        var s2 = new Sprint { Name = "S2", TeamId = 1, IsActive = false };
        db.Sprints.AddRange(s1, s2);
        var task = new TaskItem {
            Goal = "Cross-sprint work", ItemType = "Task", Status = "In Progress", Owner = "Priya",
            TeamId = 1, CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        task.SprintId = s1.Id;
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);

        // 3h logged while the task lives in sprint 1
        Assert.True((await svc.LogTimeAsync(task.Id, 3m, DateTime.UtcNow, null, null, ctx)).Success);
        // carry the task forward to sprint 2 (CompleteAsync does exactly this — moves the SprintId)
        (await db.Tasks.FindAsync(task.Id))!.SprintId = s2.Id;
        await db.SaveChangesAsync();
        // 5h more logged while in sprint 2
        Assert.True((await svc.LogTimeAsync(task.Id, 5m, DateTime.UtcNow, null, null, ctx)).Success);

        var logs = await db.SprintHoursLogs.Where(l => l.TaskId == task.Id)
            .ToDictionaryAsync(l => l.SprintId, l => l.Hours);
        Assert.Equal(3m, logs[s1.Id]);   // previous sprint keeps its 3h
        Assert.Equal(5m, logs[s2.Id]);   // new sprint records only ITS 5h — not the cumulative 8h
        Assert.Equal(8m, (await db.Tasks.FindAsync(task.Id))!.ActualHours);  // task cumulative total intact
    }

    [Fact]
    public async Task UpdateStatusAsync_ReopeningChild_ReopensCompletedParent()
    {
        using var db = DbContextFactory.Create(nameof(UpdateStatusAsync_ReopeningChild_ReopensCompletedParent));
        var parent = new TaskItem {
            Goal = "Story", ItemType = "Story", Status = "Resolved", Owner = "Priya", TeamId = 1,
            ActualEndDate = DateTime.UtcNow.AddHours(-1), CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        db.Tasks.Add(parent);
        await db.SaveChangesAsync();
        var child = new TaskItem {
            Goal = "Done task", ItemType = "Task", Status = "Done", Owner = "Priya", ParentId = parent.Id,
            ActualHours = 2, TeamId = 1, CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        db.Tasks.Add(child);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);
        var result = await svc.UpdateStatusAsync(child.Id, "In Progress", null, ctx, forceClose: false);

        Assert.True(result.Success, result.Error);
        var updatedParent = await db.Tasks.FindAsync(parent.Id);
        Assert.Equal("Active", updatedParent!.Status);
        Assert.Null(updatedParent.ActualEndDate);
    }

    // ── BulkChangeOwnerAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task BulkChangeOwnerAsync_UpdatesJoinTableForEachTask()
    {
        using var db = DbContextFactory.Create(nameof(BulkChangeOwnerAsync_UpdatesJoinTableForEachTask));
        var newOwner = new AppUser { Username = "priya", DisplayName = "Priya" };
        db.AppUsers.Add(newOwner);
        var t1 = new TaskItem { Goal = "T1", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        var t2 = new TaskItem { Goal = "T2", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.AddRange(t1, t2);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: null);
        var result = await svc.BulkChangeOwnerAsync([t1.Id, t2.Id], "Priya", ctx);

        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        var ownerRows = await db.TaskOwners.Where(o => o.UserId == newOwner.Id).ToListAsync();
        Assert.Equal(2, ownerRows.Count);
    }

    [Fact]
    public async Task BulkChangeOwnerAsync_SkipsTasksOwnedByOtherMember()
    {
        using var db = DbContextFactory.Create(nameof(BulkChangeOwnerAsync_SkipsTasksOwnedByOtherMember));
        var kasun = new AppUser { Username = "kasun", DisplayName = "Kasun" };
        var priya = new AppUser { Username = "priya", DisplayName = "Priya" };
        db.AppUsers.AddRange(kasun, priya);
        var myTask    = new TaskItem { Goal = "Mine",    Owner = "Priya", CreatedBy = "Priya", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        var otherTask = new TaskItem { Goal = "Other",   Owner = "Kasun", CreatedBy = "Kasun", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.AddRange(myTask, otherTask);
        await db.SaveChangesAsync();
        db.TaskOwners.Add(new TaskOwner { TaskId = otherTask.Id, UserId = kasun.Id });
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Priya", "priya", IsAdmin: false, TeamRole: "", TeamId: null);
        var result = await svc.BulkChangeOwnerAsync([myTask.Id, otherTask.Id], "Priya", ctx);

        Assert.True(result.Success);
        Assert.Equal(1, result.Count);
        Assert.Equal(1, result.Denied);
    }

    [Fact]
    public async Task QuickSetOwnerAsync_UpdatesNormalizedAssignment()
    {
        using var db = DbContextFactory.Create(nameof(QuickSetOwnerAsync_UpdatesNormalizedAssignment));
        var owner = new AppUser { Username = "priya", DisplayName = "Priya" };
        db.AppUsers.Add(owner);
        var task = new TaskItem { Goal = "Assignable", CreatedBy = "Admin", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: null);
        var result = await svc.QuickSetOwnerAsync(task.Id, owner.DisplayName, ctx);

        Assert.True(result.Success, result.Error);
        Assert.True(await db.TaskOwners.AnyAsync(o => o.TaskId == task.Id && o.UserId == owner.Id));
    }

    [Fact]
    public async Task ClaimTaskAsync_UpdatesNormalizedAssignment()
    {
        using var db = DbContextFactory.Create(nameof(ClaimTaskAsync_UpdatesNormalizedAssignment));
        var owner = new AppUser { Username = "priya", DisplayName = "Priya" };
        db.AppUsers.Add(owner);
        var task = new TaskItem { Goal = "Claimable", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(owner.Id, owner.DisplayName, owner.Username, IsAdmin: false, TeamRole: "Member", TeamId: null);
        var result = await svc.ClaimTaskAsync(task.Id, ctx);

        Assert.True(result.Success, result.Error);
        Assert.True(await db.TaskOwners.AnyAsync(o => o.TaskId == task.Id && o.UserId == owner.Id));
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_SoftDeletesTask()
    {
        using var db = DbContextFactory.Create(nameof(DeleteAsync_SoftDeletesTask));
        var task = new TaskItem { Goal = "To delete", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateTaskService(db);
        var ctx    = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: null);
        var result = await svc.DeleteAsync(task.Id, ctx);

        Assert.True(result.Success);
        var raw = await db.Tasks.IgnoreQueryFilters().FirstAsync(t => t.Id == task.Id);
        Assert.True(raw.IsDeleted);
        Assert.NotNull(raw.DeletedAt);
    }

    [Fact]
    public async Task DeleteAsync_DeniesNonOwner()
    {
        using var db = DbContextFactory.Create(nameof(DeleteAsync_DeniesNonOwner));
        var owner = new AppUser { Username = "kasun", DisplayName = "Kasun" };
        db.AppUsers.Add(owner);
        var task = new TaskItem { Goal = "Kasun task", Owner = "Kasun", CreatedBy = "Kasun", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        db.TaskOwners.Add(new TaskOwner { TaskId = task.Id, UserId = owner.Id });
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateTaskService(db);
        var ctx    = new UserCtx(0, "Priya", "priya", IsAdmin: false, TeamRole: "", TeamId: null);
        var result = await svc.DeleteAsync(task.Id, ctx);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task TeamLead_CanEditAnotherMembersTask()
    {
        using var db = DbContextFactory.Create(nameof(TeamLead_CanEditAnotherMembersTask));
        var task = new TaskItem {
            Goal = "Member task", Owner = "Kasun", CreatedBy = "Kasun", TeamId = 1,
            CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var lead = new UserCtx(99, "Priya", "priya", IsAdmin: false, TeamRole: "TeamLead", TeamId: 1);
        var result = await svc.AddChecklistItemAsync(task.Id, "Review the change", lead);

        Assert.True(result.Success, result.Error);
        Assert.Contains(db.TaskChecklistItems, item => item.TaskId == task.Id && item.Text == "Review the change");
    }

    [Fact]
    public async Task TeamLead_CannotDeleteAnotherMembersTask()
    {
        using var db = DbContextFactory.Create(nameof(TeamLead_CannotDeleteAnotherMembersTask));
        var task = new TaskItem {
            Goal = "Member task", Owner = "Kasun", CreatedBy = "Kasun", TeamId = 1,
            CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var lead = new UserCtx(99, "Priya", "priya", IsAdmin: false, TeamRole: "TeamLead", TeamId: 1);
        var result = await svc.DeleteAsync(task.Id, lead);

        Assert.False(result.Success);
        Assert.False((await db.Tasks.IgnoreQueryFilters().FirstAsync(t => t.Id == task.Id)).IsDeleted);
    }

    // ── BulkChangeStatusAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task BulkChangeStatusAsync_SkipsTasksWithNoOwnerForActiveStatus()
    {
        using var db = DbContextFactory.Create(nameof(BulkChangeStatusAsync_SkipsTasksWithNoOwnerForActiveStatus));
        var t1 = new TaskItem { Goal = "Has owner", Owner = "Priya", CreatedAt = DateTime.UtcNow, RowVersion = [] };
        var t2 = new TaskItem { Goal = "No owner",  Owner = null,    CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.AddRange(t1, t2);
        await db.SaveChangesAsync();

        var svc    = ServiceFactory.CreateTaskService(db);
        var ctx    = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: null);
        var result = await svc.BulkChangeStatusAsync([t1.Id, t2.Id], "Active", ctx);

        Assert.True(result.Success);
        Assert.Equal(1, result.Count);
        Assert.Equal(1, result.NoOwner);
        var updated = await db.Tasks.FindAsync(t1.Id);
        Assert.Equal("Active", updated!.Status);
        var unchanged = await db.Tasks.FindAsync(t2.Id);
        Assert.NotEqual("Active", unchanged!.Status);
    }

    [Fact]
    public async Task BulkChangeStatusAsync_CannotBypassCompletionHoursRequirement()
    {
        using var db = DbContextFactory.Create(nameof(BulkChangeStatusAsync_CannotBypassCompletionHoursRequirement));
        var task = new TaskItem {
            Goal = "Ready for QA", ItemType = "Task", Status = "In QA", Owner = "Priya",
            TeamId = 1, CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);
        var result = await svc.BulkChangeStatusAsync([task.Id], "Done", ctx);

        Assert.True(result.Success, result.Error);
        Assert.Equal(0, result.Count);
        Assert.Equal(1, result.NeedActualHours);
        Assert.Equal("In QA", (await db.Tasks.FindAsync(task.Id))!.Status);
    }

    [Fact]
    public async Task AssignSprintAsync_RecordsAppendOnlyMembershipEvent()
    {
        using var db = DbContextFactory.Create(nameof(AssignSprintAsync_RecordsAppendOnlyMembershipEvent));
        var sprint = new Sprint { Name = "Sprint", TeamId = 1, IsActive = true };
        var task = new TaskItem {
            Goal = "Task", ItemType = "Task", Status = "New", Owner = "Priya", TeamId = 1,
            CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        db.Sprints.Add(sprint);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);
        var result = await svc.AssignSprintAsync(task.Id, sprint.Id, ctx);

        Assert.True(result.Success, result.Error);
        var entry = Assert.Single(db.SprintWorkItemEvents);
        Assert.Equal("Reassigned", entry.EventType);
        Assert.Null(entry.FromSprintId);
        Assert.Equal(sprint.Id, entry.ToSprintId);
    }

    [Fact]
    public async Task LogTimeAsync_PreservesLegacyTotalAndUsesAppendOnlyEntries()
    {
        using var db = DbContextFactory.Create(nameof(LogTimeAsync_PreservesLegacyTotalAndUsesAppendOnlyEntries));
        var task = new TaskItem {
            Goal = "Legacy hours", TeamId = 1, ActualHours = 3, CreatedAt = DateTime.UtcNow.AddDays(-2), RowVersion = []
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);
        var result = await svc.LogTimeAsync(task.Id, 2, DateTime.UtcNow.Date, "Investigation", 4, ctx);

        Assert.True(result.Success, result.Error);
        Assert.Equal(5, result.ActualHours);
        Assert.Equal(4, result.RemainingWorkHours);
        Assert.Equal(2, db.TimeEntries.Count());
        Assert.Contains(db.TimeEntries, e => e.EntryType == "LegacyOpeningBalance" && e.Hours == 3);
        Assert.Contains(db.TimeEntries, e => e.EntryType == "Logged" && e.Hours == 2);
        Assert.Equal(5, (await db.Tasks.FindAsync(task.Id))!.ActualHours);
    }

    [Fact]
    public async Task CorrectTimeEntryAsync_AddsReversingEntryWithoutEditingOriginal()
    {
        using var db = DbContextFactory.Create(nameof(CorrectTimeEntryAsync_AddsReversingEntryWithoutEditingOriginal));
        var task = new TaskItem { Goal = "Time", TeamId = 1, ActualHours = 2, CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        var entry = new TimeEntry {
            TaskId = task.Id, Hours = 2, WorkDate = DateTime.UtcNow.Date, CreatedBy = "Admin", EntryType = "Logged"
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);
        var result = await svc.CorrectTimeEntryAsync(entry.Id, "Wrong task", ctx);

        Assert.True(result.Success, result.Error);
        Assert.Equal(0, result.ActualHours);
        Assert.Equal(2, db.TimeEntries.Count());
        Assert.Equal(2, (await db.TimeEntries.FindAsync(entry.Id))!.Hours);
        var correction = db.TimeEntries.Single(e => e.EntryType == "Correction");
        Assert.Equal(-2, correction.Hours);
        Assert.Equal(entry.Id, correction.ReversesEntryId);
    }

    [Fact]
    public async Task AddLinkAsync_RejectsCrossTeamTask()
    {
        using var db = DbContextFactory.Create(nameof(AddLinkAsync_RejectsCrossTeamTask));
        var from = new TaskItem { Goal = "Team A", TeamId = 1, CreatedAt = DateTime.UtcNow, RowVersion = [] };
        var to = new TaskItem { Goal = "Team B", TeamId = 2, CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.AddRange(from, to);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);
        var result = await svc.AddLinkAsync(from.Id, to.Id, "Blocks", ctx);

        Assert.False(result.Success);
        Assert.Empty(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async Task AddLinkAsync_RejectsBlockingDependencyCycle()
    {
        using var db = DbContextFactory.Create(nameof(AddLinkAsync_RejectsBlockingDependencyCycle));
        var first = new TaskItem { Goal = "First", TeamId = 1, CreatedAt = DateTime.UtcNow, RowVersion = [] };
        var second = new TaskItem { Goal = "Second", TeamId = 1, CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.AddRange(first, second);
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);
        Assert.True((await svc.AddLinkAsync(first.Id, second.Id, "Blocks", ctx)).Success);
        var cycle = await svc.AddLinkAsync(second.Id, first.Id, "Blocks", ctx);

        Assert.False(cycle.Success);
        Assert.Contains("cycle", cycle.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async Task UpdateStatusAsync_CannotCompleteWhileBlockingDependencyIsOpen()
    {
        using var db = DbContextFactory.Create(nameof(UpdateStatusAsync_CannotCompleteWhileBlockingDependencyIsOpen));
        var blocker = new TaskItem {
            Goal = "Blocker", ItemType = "Task", Status = "In Progress", Owner = "Priya", TeamId = 1,
            CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        var blocked = new TaskItem {
            Goal = "Blocked", ItemType = "Task", Status = "In QA", Owner = "Priya", TeamId = 1,
            CreatedAt = DateTime.UtcNow, RowVersion = []
        };
        db.Tasks.AddRange(blocker, blocked);
        await db.SaveChangesAsync();
        db.TaskLinks.Add(new TaskLink {
            FromTaskId = blocker.Id, ToTaskId = blocked.Id, LinkType = "Blocks", CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var ctx = new UserCtx(0, "Admin", "admin", IsAdmin: true, TeamRole: "", TeamId: 1);
        var result = await svc.UpdateStatusAsync(blocked.Id, "Done", null, ctx, forceClose: false, actualHours: 2);

        Assert.False(result.Success);
        Assert.Contains("blocking", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("In QA", (await db.Tasks.FindAsync(blocked.Id))!.Status);
    }

    [Fact]
    public async Task GetLinksAndChildStats_DoNotExposeCrossTeamRecords()
    {
        using var db = DbContextFactory.Create(nameof(GetLinksAndChildStats_DoNotExposeCrossTeamRecords));
        var visible = new TaskItem { Goal = "Visible", TeamId = 1, CreatedAt = DateTime.UtcNow, RowVersion = [] };
        var hidden = new TaskItem { Goal = "Hidden", TeamId = 2, ParentId = visible.Id, CreatedAt = DateTime.UtcNow, RowVersion = [] };
        db.Tasks.Add(visible);
        await db.SaveChangesAsync();
        hidden.ParentId = visible.Id;
        db.Tasks.Add(hidden);
        await db.SaveChangesAsync();
        db.TaskLinks.Add(new TaskLink { FromTaskId = visible.Id, ToTaskId = hidden.Id, LinkType = "Blocks", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var svc = ServiceFactory.CreateTaskService(db);
        var links = await svc.GetLinksAsync(visible.Id, teamId: 1);
        var foreignStats = await svc.GetChildStatsAsync(hidden.Id, teamId: 1);

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<object>>(links));
        Assert.Null(foreignStats);
    }
}
