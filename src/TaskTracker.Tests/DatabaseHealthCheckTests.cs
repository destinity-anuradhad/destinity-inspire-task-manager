using Microsoft.Extensions.Diagnostics.HealthChecks;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class DatabaseHealthCheckTests
{
    [Fact]
    public async Task ReportsHealthy_WhenDatabaseIsReachable()
    {
        using var db = DbContextFactory.Create(nameof(ReportsHealthy_WhenDatabaseIsReachable));
        var check = new DatabaseHealthCheck(db);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
