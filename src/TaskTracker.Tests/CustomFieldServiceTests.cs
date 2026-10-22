using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class CustomFieldServiceTests
{
    [Fact]
    public async Task CreateAndSetValues_StoresTypedCanonicalValuesForTeamTask()
    {
        using var db = DbContextFactory.Create(nameof(CreateAndSetValues_StoresTypedCanonicalValuesForTeamTask));
        var service = new CustomFieldService(db);
        var ctx = new UserCtx(1, "Priya", "priya", false, "TeamAdmin", 1);
        var created = await service.CreateDefinitionAsync(new CustomFieldDefinition { Name = "Risk", FieldType = "Select", IsRequired = true }, ["Low", "High"], ctx);
        Assert.True(created.Success, created.Error);
        var definition = Assert.Single(db.CustomFieldDefinitions);
        var task = new TaskItem { Goal = "Release", TeamId = 1, Status = "New", RowVersion = [] };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var saved = await service.SetValuesAsync(task.Id, new Dictionary<int, string?> { [definition.Id] = "High" }, ctx);

        Assert.True(saved.Success, saved.Error);
        Assert.Equal("High", Assert.Single(db.TaskCustomFieldValues).Value);
        var invalid = await service.SetValuesAsync(task.Id, new Dictionary<int, string?> { [definition.Id] = "Medium" }, ctx);
        Assert.False(invalid.Success);
    }
}
