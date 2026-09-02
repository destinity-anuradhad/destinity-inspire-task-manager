using System.Text;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class ImportServiceTests
{
    private static Team Seed(TaskTracker.Data.AppDbContext db)
    {
        var team = new Team { Name = "Core", IsActive = true };
        db.Add(team);
        db.WorkStates.Add(new WorkState { Name = "New", SortOrder = 0 });
        db.SaveChanges();
        return team;
    }

    private static ImportService NewService(TaskTracker.Data.AppDbContext db)
        => new(db, ServiceFactory.CreateTaskService(db));

    private static Stream Csv(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static UserCtx Ctx(int teamId) => new(1, "Tester", "tester", true, "TeamAdmin", teamId);

    [Fact]
    public async Task Preview_FlagsInvalidRowsWithoutWriting()
    {
        using var db = DbContextFactory.Create(nameof(Preview_FlagsInvalidRowsWithoutWriting));
        var team = Seed(db);
        var import = NewService(db);

        var csv = "Goal,ItemType,Status\n" +
                  "Good task,Task,New\n" +      // valid
                  ",Task,New\n" +               // missing goal
                  "Bad type,Sasquatch,New\n";   // unknown item type
        var preview = await import.PreviewAsync(team.Id, 1, "tester", "t.csv", Csv(csv));

        Assert.Equal(3, preview.Job.TotalRows);
        Assert.Equal(1, preview.Job.ValidRows);
        Assert.Equal(2, preview.Job.ErrorRows);
        Assert.Equal("Preview", preview.Job.Status);
        Assert.Empty(db.Tasks);   // preview writes no tasks
        Assert.Equal("Valid", preview.Rows[0].RowStatus);
        Assert.Equal("Invalid", preview.Rows[1].RowStatus);
        Assert.Equal("Invalid", preview.Rows[2].RowStatus);
    }

    [Fact]
    public async Task Apply_CreatesTasksForValidRows()
    {
        using var db = DbContextFactory.Create(nameof(Apply_CreatesTasksForValidRows));
        var team = Seed(db);
        var import = NewService(db);

        var preview = await import.PreviewAsync(team.Id, 1, "tester", "t.csv",
            Csv("Goal,Status\nAlpha,New\nBeta,New\n"));
        var result = await import.ApplyAsync(preview.Job.Id, Ctx(team.Id), isSysAdmin: true);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Created);
        Assert.Equal(2, db.Tasks.Count(t => t.TeamId == team.Id));
        Assert.Equal("Applied", result.Job.Status);
    }

    [Fact]
    public async Task Apply_IsIdempotentOnExternalKey()
    {
        using var db = DbContextFactory.Create(nameof(Apply_IsIdempotentOnExternalKey));
        var team = Seed(db);
        var import = NewService(db);
        const string csv = "Goal,Status,ExternalKey\nGamma,New,IMP-1\n";

        var first = await import.ApplyAsync((await import.PreviewAsync(team.Id, 1, "t", "a.csv", Csv(csv))).Job.Id, Ctx(team.Id), true);
        Assert.Equal(1, first!.Created);

        // Re-importing the same external key creates nothing.
        var second = await import.ApplyAsync((await import.PreviewAsync(team.Id, 1, "t", "b.csv", Csv(csv))).Job.Id, Ctx(team.Id), true);
        Assert.Equal(0, second!.Created);
        Assert.Equal(1, second.Skipped);
        Assert.Equal(1, db.Tasks.Count(t => t.TeamId == team.Id));
    }
}
