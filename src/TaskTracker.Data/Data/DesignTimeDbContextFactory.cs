using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskTracker.Data;

/// <summary>
/// Lets the <c>dotnet ef</c> tooling construct <see cref="AppDbContext"/> without booting the web
/// host (or supplying its runtime secrets). Migration scaffolding never opens a connection; the
/// placeholder connection string is only used by commands that actually reach the database, and can
/// be overridden with the <c>ConnectionStrings__DefaultConnection</c> environment variable.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost;Database=TaskTrackerDesignTime;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection)
            .Options;

        return new AppDbContext(options);
    }
}
