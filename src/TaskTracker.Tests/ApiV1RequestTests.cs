using TaskTracker.Controllers;
using Xunit;

namespace TaskTracker.Tests;

/// <summary>Parsing of GET /api/v1/tasks query parameters into an <see cref="TaskTracker.Services.ApiTaskQuery"/>.</summary>
public class ApiV1RequestTests
{
    [Fact]
    public void ToQuery_ParsesListsMeAndUpdatedSince()
    {
        var request = new ApiV1Controller.TaskListRequest
        {
            OwnerId = "me, 7",
            Status = "New,To Do, In Progress",
            ItemType = "Task,Bug",
            Ids = "3,4",
            UpdatedSince = "2026-10-21T09:20:00+05:30",
            Open = true,
            PageSize = 500,
        };

        var (query, error) = request.ToQuery(currentUserId: 42);

        Assert.Null(error);
        Assert.Equal(new[] { 42, 7 }, query.OwnerIds);
        Assert.Equal(new[] { "New", "To Do", "In Progress" }, query.Statuses);
        Assert.Equal(new[] { "Task", "Bug" }, query.ItemTypes);
        Assert.Equal(new[] { 3, 4 }, query.Ids);
        Assert.Equal(new DateTime(2026, 10, 21, 3, 50, 0, DateTimeKind.Utc), query.UpdatedSinceUtc);
        Assert.True(query.Open);
        Assert.Equal(100, query.PageSize);   // clamped
    }

    [Fact]
    public void ToQuery_KeepsTheLegacyParametersWorking()
    {
        var (query, error) = new ApiV1Controller.TaskListRequest
        {
            Owner = "Dev",
            Sprint = "__none__",
            DateFrom = new DateTime(2026, 10, 1),
            SortDir = "asc",
        }.ToQuery(currentUserId: 1);

        Assert.Null(error);
        Assert.Equal("Dev", query.OwnerName);
        Assert.Equal("backlog", query.Sprint);
        Assert.Equal(new DateTime(2026, 10, 1), query.DueFrom);
        Assert.False(query.Descending);
    }

    [Theory]
    [InlineData("abc", null, null, "ownerId")]
    [InlineData(null, "1,x", null, "ids")]
    [InlineData(null, null, "yesterday", "updatedSince")]
    public void ToQuery_RejectsBadValuesWithAClearMessage(string? ownerId, string? ids, string? updatedSince, string mentions)
    {
        var (_, error) = new ApiV1Controller.TaskListRequest { OwnerId = ownerId, Ids = ids, UpdatedSince = updatedSince }.ToQuery(1);
        Assert.NotNull(error);
        Assert.Contains(mentions, error);
    }

    [Fact]
    public void ToQuery_SprintIdWinsOverSprint()
    {
        var (query, _) = new ApiV1Controller.TaskListRequest { SprintId = 9, Sprint = "active" }.ToQuery(1);
        Assert.Equal("9", query.Sprint);
    }
}
