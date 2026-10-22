using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;

namespace TaskTracker.Tests.Helpers;

internal static class DbContextFactory
{
    public static AppDbContext Create(string dbName)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .EnableSensitiveDataLogging()
            .Options;
        var db = new AppDbContext(opts);
        db.Database.EnsureCreated();
        return db;
    }
}
