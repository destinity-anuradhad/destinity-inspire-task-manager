using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations;

public partial class MigrateTaskOwnerId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'Tasks') AND name = N'OwnerId'
            )
            BEGIN
                -- Back-populate TaskOwners from Tasks.OwnerId where a row doesn't already exist
                INSERT INTO [TaskOwners] ([TaskId], [UserId])
                SELECT t.[Id], t.[OwnerId]
                FROM   [Tasks] t
                WHERE  t.[OwnerId] IS NOT NULL
                  AND  NOT EXISTS (
                      SELECT 1 FROM [TaskOwners] o
                      WHERE  o.[TaskId] = t.[Id]
                        AND  o.[UserId] = t.[OwnerId]
                  );

                ALTER TABLE [Tasks] DROP COLUMN [OwnerId];
            END
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'Tasks') AND name = N'OwnerId'
            )
            BEGIN
                ALTER TABLE [Tasks] ADD [OwnerId] INT NULL;
                -- Restore single owner per task (first entry in TaskOwners)
                UPDATE t
                SET    t.[OwnerId] = (
                    SELECT TOP 1 o.[UserId]
                    FROM   [TaskOwners] o
                    WHERE  o.[TaskId] = t.[Id]
                )
                FROM   [Tasks] t;
            END
        ");
    }
}
