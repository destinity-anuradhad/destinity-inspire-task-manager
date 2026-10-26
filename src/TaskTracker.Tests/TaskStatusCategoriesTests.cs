using TaskTracker.Services;
using Xunit;

namespace TaskTracker.Tests;

public class TaskStatusCategoriesTests
{
    [Theory]
    [InlineData("Task", "To Do", null, TaskStatusCategories.ToDo)]
    [InlineData("Task", "In Progress", null, TaskStatusCategories.InProgress)]
    [InlineData("Task", "Development Done", null, TaskStatusCategories.InProgress)]   // mid-stage, not done
    [InlineData("Task", "Done", null, TaskStatusCategories.Done)]
    [InlineData("Bug", "New", null, TaskStatusCategories.ToDo)]
    [InlineData("Bug", "Resolved", null, TaskStatusCategories.Done)]
    [InlineData("Epic", "Resolved", null, TaskStatusCategories.InProgress)]
    [InlineData("Task", "Blocked", null, TaskStatusCategories.Blocked)]
    [InlineData("Task", "On hold", null, TaskStatusCategories.Blocked)]
    [InlineData("Bug", "Rejected", null, TaskStatusCategories.Cancelled)]
    [InlineData("Task", "Shipped", "Done", TaskStatusCategories.Done)]               // a team workflow's own state
    [InlineData("Task", "Review", "InProgress", TaskStatusCategories.InProgress)]
    [InlineData("Task", "Blocked", "InProgress", TaskStatusCategories.Blocked)]       // side-states win
    [InlineData(null, "", null, TaskStatusCategories.ToDo)]
    public void For_MapsStatusesToStableCategories(string? itemType, string? status, string? workflowCategory, string expected)
        => Assert.Equal(expected, TaskStatusCategories.For(itemType, status, workflowCategory));

    [Fact]
    public void IsOpen_IsFalseOnlyForDoneAndCancelled()
    {
        Assert.Equal(
            new[] { TaskStatusCategories.ToDo, TaskStatusCategories.InProgress, TaskStatusCategories.Blocked },
            TaskStatusCategories.All.Where(TaskStatusCategories.IsOpen).ToArray());
    }
}
