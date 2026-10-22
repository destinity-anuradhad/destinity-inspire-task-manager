using System.Text.Json;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class ChartServiceTests
{
    [Fact]
    public async Task GetChartDataAsync_UsesCompletionSnapshotForHistoricVelocity()
    {
        using var db = DbContextFactory.Create(nameof(GetChartDataAsync_UsesCompletionSnapshotForHistoricVelocity));
        var sprint = new Sprint { Name = "Historic", TeamId = 1, CompletedAt = DateTime.UtcNow, IsActive = false };
        db.Sprints.Add(sprint);
        await db.SaveChangesAsync();
        db.SprintCompletionSnapshots.Add(new SprintCompletionSnapshot {
            SprintId = sprint.Id, CommittedEstimate = 13, CompletedEstimate = 8,
            CommittedCount = 3, CompletedCount = 2, CarriedForwardCount = 1
        });
        // This task represents a later reassignment and must not rewrite velocity.
        db.Tasks.Add(new TaskItem { Goal = "Moved later", TeamId = 1, SprintId = null, Weight = 99, EstimateUnit = "Points", RowVersion = [] });
        await db.SaveChangesAsync();

        var data = await new ChartService(db).GetChartDataAsync(1);
        using var velocity = JsonDocument.Parse(data.VelocityJson);
        var item = velocity.RootElement[0];

        Assert.Equal(13, item.GetProperty("committed").GetDecimal());
        Assert.Equal(8, item.GetProperty("completed").GetDecimal());
    }
}
