using Microsoft.Extensions.Configuration;
using TaskTracker.Data;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

/// <summary>The v1 API read side: exact owners, incremental sync, own-tasks-only keys, categories and time zones.</summary>
public class ApiTaskQueryServiceTests
{
    private const int TeamId = 1;
    private const int OtherTeamId = 2;

    private sealed record Seed(AppUser Dev, AppUser Devika, TaskItem Grn, TaskItem Shared, TaskItem DevikaOnly, TaskItem OtherTeam, Sprint Sprint);

    private static Seed SeedData(AppDbContext db)
    {
        var dev = new AppUser { Username = "dev", DisplayName = "Dev", Email = "dev@corp.lk", Role = "Member", Status = "Active" };
        var devika = new AppUser { Username = "devika", DisplayName = "Devika", Role = "Member", Status = "Active" };
        db.AddRange(dev, devika, new Team { Id = TeamId, Name = "Inspire", IsActive = true }, new Team { Id = OtherTeamId, Name = "Other", IsActive = true });
        var sprint = new Sprint { Name = "Sprint 21", StartDate = new DateTime(2026, 10, 15), EndDate = new DateTime(2026, 10, 28), IsActive = true, TeamId = TeamId };
        db.Add(sprint);
        db.SaveChanges();

        TaskItem Task(string goal, string owner, int team = TeamId) => new()
        {
            Goal = goal,
            Owner = owner,
            Status = "To Do",
            ItemType = "Task",
            TeamId = team,
            CreatedAt = new DateTime(2026, 10, 20, 9, 19, 35),
            RowVersion = [],
        };
        var grn = Task("Fix GRN rounding", "Dev");
        grn.Status = "In Progress";
        grn.Priority = "High";
        grn.EndDate = new DateTime(2026, 10, 23);
        grn.ActualStartDate = new DateTime(2026, 10, 20, 3, 50, 0);   // UTC, as the status path writes it
        grn.SprintId = sprint.Id;
        grn.ModuleId = 10;
        grn.Project = "Destinity Inspire - Inventory";
        grn.ClientId = 7;
        grn.Property = "Browns Hotels & Resorts";
        var shared = Task("Upgrade to Angular 21", "Dev, Devika");
        var devikaOnly = Task("Devika's report", "Devika");
        var otherTeam = Task("Other team task", "Dev", OtherTeamId);
        db.Tasks.AddRange(grn, shared, devikaOnly, otherTeam);
        db.SaveChanges();

        db.TaskOwners.AddRange(
            new TaskOwner { TaskId = grn.Id, UserId = dev.Id },
            new TaskOwner { TaskId = shared.Id, UserId = dev.Id },
            new TaskOwner { TaskId = shared.Id, UserId = devika.Id },
            new TaskOwner { TaskId = devikaOnly.Id, UserId = devika.Id },
            new TaskOwner { TaskId = otherTeam.Id, UserId = dev.Id });
        db.SaveChanges();
        return new Seed(dev, devika, grn, shared, devikaOnly, otherTeam, sprint);
    }

    private static ApiTaskQueryService Service(AppDbContext db) =>
        new(db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppUrl"] = "https://tm.example/" }).Build());

    private static async Task<int[]> Ids(ApiTaskQueryService svc, ApiTaskQuery query, ApiCaller caller) =>
        (await svc.ListAsync(query, caller)).Data.Select(t => t.Id).Order().ToArray();

    [Fact]
    public async Task ListAsync_FiltersByOwnerIdExactly_UnlikeTheLegacyNameFilter()
    {
        using var db = DbContextFactory.Create(nameof(ListAsync_FiltersByOwnerIdExactly_UnlikeTheLegacyNameFilter));
        var s = SeedData(db);
        var svc = Service(db);
        var caller = new ApiCaller(s.Dev.Id, TeamId);

        Assert.Equal(new[] { s.Grn.Id, s.Shared.Id }, await Ids(svc, new ApiTaskQuery { OwnerIds = [s.Dev.Id] }, caller));
        // "Dev" is a substring of "Devika": the old owner= filter over-matches.
        Assert.Equal(new[] { s.Grn.Id, s.Shared.Id, s.DevikaOnly.Id }, await Ids(svc, new ApiTaskQuery { OwnerName = "Dev" }, caller));
    }

    [Fact]
    public async Task ListAsync_StaysInsideTheCallersTeam()
    {
        using var db = DbContextFactory.Create(nameof(ListAsync_StaysInsideTheCallersTeam));
        var s = SeedData(db);
        var svc = Service(db);

        Assert.DoesNotContain(s.OtherTeam.Id, await Ids(svc, new ApiTaskQuery(), new ApiCaller(s.Dev.Id, TeamId)));
        Assert.Null(await svc.GetAsync(s.OtherTeam.Id, new ApiCaller(s.Dev.Id, TeamId)));
    }

    [Fact]
    public async Task OwnTasksOnlyCaller_SeesAndReachesOnlyItsOwnTasks()
    {
        using var db = DbContextFactory.Create(nameof(OwnTasksOnlyCaller_SeesAndReachesOnlyItsOwnTasks));
        var s = SeedData(db);
        var svc = Service(db);
        var caller = new ApiCaller(s.Dev.Id, TeamId, OwnTasksOnly: true);

        Assert.Equal(new[] { s.Grn.Id, s.Shared.Id }, await Ids(svc, new ApiTaskQuery(), caller));
        // Even asking for Devika's tasks explicitly returns nothing that isn't also Dev's.
        Assert.Equal(new[] { s.Shared.Id }, await Ids(svc, new ApiTaskQuery { OwnerIds = [s.Devika.Id] }, caller));
        Assert.Null(await svc.GetAsync(s.DevikaOnly.Id, caller));
        Assert.False(await svc.IsVisibleAsync(s.DevikaOnly.Id, caller));
        Assert.True(await svc.IsVisibleAsync(s.Grn.Id, caller));
    }

    [Fact]
    public async Task ListAsync_UpdatedSince_ReturnsOnlyTasksChangedSinceTheServerTime()
    {
        using var db = DbContextFactory.Create(nameof(ListAsync_UpdatedSince_ReturnsOnlyTasksChangedSinceTheServerTime));
        var s = SeedData(db);
        var svc = Service(db);
        var caller = new ApiCaller(s.Dev.Id, TeamId);

        var first = await svc.ListAsync(new ApiTaskQuery(), caller);
        await Task.Delay(20);
        s.Shared.Priority = "Critical";   // tracked entity: SaveChanges stamps UpdatedAt
        await db.SaveChangesAsync();

        var changed = await svc.ListAsync(new ApiTaskQuery { UpdatedSinceUtc = first.ServerTime.UtcDateTime }, caller);
        Assert.Equal(new[] { s.Shared.Id }, changed.Data.Select(t => t.Id).ToArray());
        Assert.True(changed.Data[0].UpdatedAt > first.ServerTime);
    }

    [Fact]
    public async Task ListAsync_IdsAndIncludeDeleted_LetASyncFindRemovedTasks()
    {
        using var db = DbContextFactory.Create(nameof(ListAsync_IdsAndIncludeDeleted_LetASyncFindRemovedTasks));
        var s = SeedData(db);
        var svc = Service(db);
        var caller = new ApiCaller(s.Dev.Id, TeamId);
        s.Grn.IsDeleted = true;
        s.Grn.DeletedAt = DateTime.Now;
        await db.SaveChangesAsync();

        var ids = new[] { s.Grn.Id, s.Shared.Id };
        Assert.Equal(new[] { s.Shared.Id }, await Ids(svc, new ApiTaskQuery { Ids = ids }, caller));
        var withDeleted = await svc.ListAsync(new ApiTaskQuery { Ids = ids, IncludeDeleted = true }, caller);
        Assert.True(withDeleted.Data.Single(t => t.Id == s.Grn.Id).IsDeleted);
    }

    [Fact]
    public async Task ListAsync_OpenFilter_FollowsItemTypeSideStateAndWorkflowRules()
    {
        using var db = DbContextFactory.Create(nameof(ListAsync_OpenFilter_FollowsItemTypeSideStateAndWorkflowRules));
        var s = SeedData(db);
        db.WorkflowStates.AddRange(
            new WorkflowState { WorkflowDefinitionId = 5, Key = "doing", Name = "Doing", Category = "InProgress" },
            new WorkflowState { WorkflowDefinitionId = 5, Key = "shipped", Name = "Shipped", Category = "Done" });
        TaskItem Make(string status, string itemType = "Task", int? workflow = null) =>
            new() { Goal = status, Status = status, ItemType = itemType, TeamId = TeamId, WorkflowDefinitionId = workflow, RowVersion = [] };
        var devDone = Make("Development Done");
        var done = Make("Done");
        var resolvedBug = Make("Resolved", "Bug");
        var rejected = Make("Rejected");
        var blocked = Make("Blocked");
        var shipped = Make("Shipped", workflow: 5);
        var doing = Make("Doing", workflow: 5);
        db.Tasks.AddRange(devDone, done, resolvedBug, rejected, blocked, shipped, doing);
        await db.SaveChangesAsync();
        var svc = Service(db);
        var caller = new ApiCaller(s.Dev.Id, TeamId);

        var open = await Ids(svc, new ApiTaskQuery { Open = true }, caller);
        var closed = await Ids(svc, new ApiTaskQuery { Open = false }, caller);
        Assert.Equal(new[] { done.Id, resolvedBug.Id, rejected.Id, shipped.Id }.Order(), closed);
        Assert.Contains(devDone.Id, open);
        Assert.Contains(blocked.Id, open);
        Assert.Contains(doing.Id, open);

        var page = await svc.ListAsync(new ApiTaskQuery { Ids = [shipped.Id, doing.Id, rejected.Id, blocked.Id] }, caller);
        var categories = page.Data.ToDictionary(t => t.Id, t => t.StatusCategory);
        Assert.Equal(TaskStatusCategories.Done, categories[shipped.Id]);
        Assert.Equal(TaskStatusCategories.InProgress, categories[doing.Id]);
        Assert.Equal(TaskStatusCategories.Cancelled, categories[rejected.Id]);
        Assert.Equal(TaskStatusCategories.Blocked, categories[blocked.Id]);
    }

    [Fact]
    public async Task Tasks_CarryOwnersDueDateSprintIdsOffsetsAndWebUrl()
    {
        using var db = DbContextFactory.Create(nameof(Tasks_CarryOwnersDueDateSprintIdsOffsetsAndWebUrl));
        var s = SeedData(db);
        var svc = Service(db);

        var task = (await svc.ListAsync(new ApiTaskQuery { Ids = [s.Grn.Id] }, new ApiCaller(s.Dev.Id, TeamId))).Data.Single();

        Assert.Equal(new[] { s.Dev.Id }, task.OwnerIds);
        Assert.Equal("Dev", task.Owners.Single().DisplayName);
        Assert.Equal(TaskStatusCategories.InProgress, task.StatusCategory);
        Assert.Equal(new DateOnly(2026, 10, 23), task.DueDate);   // the form's Due Date (EndDate)
        Assert.Equal(new ApiSprint(s.Sprint.Id, "Sprint 21", new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 28), true), task.Sprint);
        Assert.Equal((10, 7), (task.ModuleId, task.PropertyId));
        Assert.Equal("https://tm.example/Tasks?highlight=" + s.Grn.Id, task.WebUrl);

        // CreatedAt is server-local: same wall-clock time, with the server's offset attached.
        var created = new DateTime(2026, 10, 20, 9, 19, 35);
        Assert.Equal(created, task.CreatedAt.DateTime);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(created), task.CreatedAt.Offset);
        // ActualStartDate is UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 3, 50, 0, TimeSpan.Zero), task.ActualStartDate);
        Assert.Equal(TimeSpan.Zero, task.UpdatedAt.Offset);
    }

    [Fact]
    public async Task Fingerprint_ChangesOnlyWhenAMatchingTaskChanges()
    {
        using var db = DbContextFactory.Create(nameof(Fingerprint_ChangesOnlyWhenAMatchingTaskChanges));
        var s = SeedData(db);
        var svc = Service(db);
        var caller = new ApiCaller(s.Dev.Id, TeamId);
        var mine = new ApiTaskQuery { OwnerIds = [s.Dev.Id] };

        var before = await svc.FingerprintAsync(mine, caller);
        Assert.Equal(before, await svc.FingerprintAsync(mine, caller));

        await Task.Delay(20);
        s.DevikaOnly.Priority = "Low";   // not in the query
        await db.SaveChangesAsync();
        Assert.Equal(before, await svc.FingerprintAsync(mine, caller));

        await Task.Delay(20);
        s.Grn.Priority = "Low";
        await db.SaveChangesAsync();
        Assert.NotEqual(before, await svc.FingerprintAsync(mine, caller));
    }

    [Fact]
    public async Task HistoryAndComments_AreSeparateAndCarryTheServerOffset()
    {
        using var db = DbContextFactory.Create(nameof(HistoryAndComments_AreSeparateAndCarryTheServerOffset));
        var s = SeedData(db);
        var at = new DateTime(2026, 10, 21, 9, 20, 0);
        db.ActivityLog.AddRange(
            new ActivityLog { TaskId = s.Grn.Id, Action = "Status Changed", Details = "To Do → In Progress", ChangedBy = "Dev", ChangedAt = at },
            new ActivityLog { TaskId = s.Grn.Id, Action = "Comment", Details = "Started", ChangedBy = "Dev", ChangedAt = at.AddMinutes(1) });
        await db.SaveChangesAsync();
        var svc = Service(db);

        var history = Assert.Single(await svc.HistoryAsync(s.Grn.Id));
        Assert.Equal("Status Changed", history.Action);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(at), history.ChangedAt.Offset);
        Assert.Equal(history.ChangedAt.ToString("o"), history.ChangedAtIso);
        Assert.Equal("Started", Assert.Single(await svc.CommentsAsync(s.Grn.Id)).Details);
    }

    [Fact]
    public async Task TimeEntriesForUserAsync_ReturnsThatUsersEntriesInTheRange()
    {
        using var db = DbContextFactory.Create(nameof(TimeEntriesForUserAsync_ReturnsThatUsersEntriesInTheRange));
        var s = SeedData(db);
        db.TimeEntries.AddRange(
            new TimeEntry { TaskId = s.Grn.Id, UserId = s.Dev.Id, Hours = 1.5m, WorkDate = new DateTime(2026, 10, 20), CreatedAt = new DateTime(2026, 10, 20, 6, 0, 0) },
            new TimeEntry { TaskId = s.Grn.Id, UserId = s.Dev.Id, Hours = 2m, WorkDate = new DateTime(2026, 10, 25) },
            new TimeEntry { TaskId = s.Shared.Id, UserId = s.Devika.Id, Hours = 3m, WorkDate = new DateTime(2026, 10, 20) });
        await db.SaveChangesAsync();
        var svc = Service(db);

        var entries = await svc.TimeEntriesForUserAsync(s.Dev.Id, new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 21));
        var entry = Assert.Single(entries);
        Assert.Equal((1.5m, new DateOnly(2026, 10, 20), "Dev"), (entry.Hours, entry.WorkDate, entry.UserName));
        Assert.Equal(TimeSpan.Zero, entry.CreatedAt.Offset);
        Assert.Equal(2, (await svc.TimeEntriesForTaskAsync(s.Grn.Id)).Count);
    }

    [Fact]
    public async Task ModulesAndProperties_HaveOneEntryPerIdNamedByTheCommonestName()
    {
        using var db = DbContextFactory.Create(nameof(ModulesAndProperties_HaveOneEntryPerIdNamedByTheCommonestName));
        var s = SeedData(db);
        foreach (var name in new[] { "Browns", "Browns Hotels & Resorts" })
            db.Tasks.Add(new TaskItem { Goal = "x", TeamId = TeamId, ClientId = 7, Property = name, ModuleId = 10, Project = "Destinity Inspire - Inventory", RowVersion = [] });
        await db.SaveChangesAsync();
        var svc = Service(db);
        var caller = new ApiCaller(s.Dev.Id, TeamId);

        var property = Assert.Single(await svc.PropertiesAsync(caller));
        Assert.Equal((7, "Browns Hotels & Resorts", 3), (property.Id, property.Name, property.TaskCount));
        Assert.Equal("Destinity Inspire - Inventory", Assert.Single(await svc.ModulesAsync(caller)).Name);
    }

    [Fact]
    public async Task MeAsync_ReturnsTheUserTeamAndServerTimeZone()
    {
        using var db = DbContextFactory.Create(nameof(MeAsync_ReturnsTheUserTeamAndServerTimeZone));
        var s = SeedData(db);

        var me = await Service(db).MeAsync(new ApiCaller(s.Dev.Id, TeamId));

        Assert.NotNull(me);
        Assert.Equal((s.Dev.Id, "Dev", "dev@corp.lk", "Inspire"), (me!.UserId, me.DisplayName, me.Email, me.TeamName));
        Assert.Equal(TimeZoneInfo.Local.Id, me.ServerTimeZone);
    }

    [Fact]
    public async Task SaveChanges_StampsTaskUpdatedAtOnInsertAndUpdate()
    {
        using var db = DbContextFactory.Create(nameof(SaveChanges_StampsTaskUpdatedAtOnInsertAndUpdate));
        var task = new TaskItem { Goal = "x", UpdatedAt = new DateTime(2000, 1, 1), RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        var inserted = task.UpdatedAt;
        Assert.True(inserted > DateTime.UtcNow.AddMinutes(-1));

        await Task.Delay(20);
        task.Status = "In Progress";
        await db.SaveChangesAsync();
        Assert.True(task.UpdatedAt > inserted);
    }
}
