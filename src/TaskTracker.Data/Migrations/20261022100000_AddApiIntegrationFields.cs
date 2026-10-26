using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaskTracker.Data;

#nullable disable

namespace TaskTracker.Migrations;

/// <summary>
/// v1 API integration support: Tasks.UpdatedAt (UTC) for incremental sync / ETags, and
/// ApiClients.OwnTasksOnly for personal keys limited to their owner's tasks.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261022100000_AddApiIntegrationFields")]
public partial class AddApiIntegrationFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAt",
            table: "Tasks",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()");

        // Backfill: the last activity on the task (or its creation), both stored in server-local
        // time, converted to UTC with the database server's current offset.
        migrationBuilder.Sql(@"
UPDATE t
SET t.[UpdatedAt] = DATEADD(MINUTE, DATEDIFF(MINUTE, GETDATE(), GETUTCDATE()),
        COALESCE((SELECT MAX(a.[ChangedAt]) FROM [dbo].[ActivityLog] a WHERE a.[TaskId] = t.[Id]), t.[CreatedAt]))
FROM [dbo].[Tasks] t;");

        migrationBuilder.CreateIndex(
            name: "IX_Tasks_TeamId_UpdatedAt",
            table: "Tasks",
            columns: new[] { "TeamId", "UpdatedAt" });

        migrationBuilder.AddColumn<bool>(
            name: "OwnTasksOnly",
            table: "ApiClients",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "OwnTasksOnly", table: "ApiClients");
        migrationBuilder.DropIndex(name: "IX_Tasks_TeamId_UpdatedAt", table: "Tasks");
        migrationBuilder.DropColumn(name: "UpdatedAt", table: "Tasks");
    }
}
