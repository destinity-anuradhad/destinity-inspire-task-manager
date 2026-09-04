using System.Text;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class AuditExportServiceTests
{
    private static void Seed(TaskTracker.Data.AppDbContext db, params ActivityLog[] entries)
    {
        db.ActivityLog.AddRange(entries);
        db.SaveChanges();
    }

    private static ActivityLog Entry(DateTime at, string by = "Admin", string category = "Task", string action = "Created", int? teamId = 1)
        => new() { ChangedAt = at, ChangedBy = by, Category = category, Action = action, Details = "d", TeamId = teamId, TaskGoal = "", Project = "P" };

    // ── Export ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_FiltersByDateCategoryUser_NewestFirst()
    {
        using var db = DbContextFactory.Create(nameof(Export_FiltersByDateCategoryUser_NewestFirst));
        var jan1 = new DateTime(2026, 1, 1);
        Seed(db,
            Entry(jan1.AddDays(1), by: "Alice", category: "Task"),
            Entry(jan1.AddDays(2), by: "Bob",   category: "Auth"),
            Entry(jan1.AddDays(3), by: "Alice", category: "Task"));
        var svc = new AuditExportService(db);

        var byUser = await svc.ExportAsync(new AuditExportFilter(User: "Alice"));
        Assert.Equal(2, byUser.TotalMatched);
        Assert.All(byUser.Rows, r => Assert.Equal("Alice", r.ChangedBy));
        Assert.True(byUser.Rows[0].ChangedAt >= byUser.Rows[1].ChangedAt);   // newest first

        var byCat = await svc.ExportAsync(new AuditExportFilter(Category: "Auth"));
        Assert.Single(byCat.Rows);

        var byDate = await svc.ExportAsync(new AuditExportFilter(FromUtc: jan1.AddDays(2), ToUtc: jan1.AddDays(4)));
        Assert.Equal(2, byDate.TotalMatched);   // days 2 and 3
    }

    [Fact]
    public void ToCsv_EscapesAndHeaders()
    {
        var rows = new[]
        {
            new AuditExportRow(1, new DateTime(2026, 1, 1, 9, 30, 0), "Al, \"Boss\"", "Task", "Updated",
                "changed \"status\"", 5, 1, "Proj", "127.0.0.1"),
        };
        var csv = Encoding.UTF8.GetString(AuditExportService.ToCsv(rows));

        Assert.Contains("Id,ChangedAtUtc,ChangedBy", csv);          // header
        Assert.Contains("\"Al, \"\"Boss\"\"\"", csv);               // comma + quotes escaped
        Assert.Contains("\"changed \"\"status\"\"\"", csv);
    }

    // ── Retention ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CountOlderThan_IsReadOnly()
    {
        using var db = DbContextFactory.Create(nameof(CountOlderThan_IsReadOnly));
        var now = DateTime.Now;
        Seed(db, Entry(now.AddDays(-100)), Entry(now.AddDays(-100)), Entry(now.AddDays(-1)));
        var svc = new AuditExportService(db);

        var count = await svc.CountOlderThanAsync(now.AddDays(-30));

        Assert.Equal(2, count);
        Assert.Equal(3, db.ActivityLog.Count());   // nothing deleted
    }

    [Fact]
    public async Task Purge_DeletesOldEntries_AndWritesAuditRecord()
    {
        using var db = DbContextFactory.Create(nameof(Purge_DeletesOldEntries_AndWritesAuditRecord));
        var now = DateTime.Now;
        Seed(db,
            Entry(now.AddDays(-100)), Entry(now.AddDays(-90)),
            Entry(now.AddDays(-2)));                              // recent — kept
        var svc = new AuditExportService(db);

        var deleted = await svc.PurgeOlderThanAsync(now.AddDays(-30), performedBy: "Root");

        Assert.Equal(2, deleted);
        // The two old entries are gone; the recent one plus a new purge record remain.
        Assert.Equal(2, db.ActivityLog.Count());
        Assert.Contains(db.ActivityLog, a => a.Action == "Audit Retention Purge"
            && a.ChangedBy == "Root" && a.Details.Contains("Deleted 2"));
    }
}
