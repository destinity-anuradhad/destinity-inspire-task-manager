using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations;

public partial class AddTeamIdToTasksAndSprints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'Tasks') AND name = N'TeamId'
            )
            BEGIN
                ALTER TABLE [Tasks] ADD [TeamId] INT NULL;
                UPDATE [Tasks] SET [TeamId] = (SELECT TOP 1 [Id] FROM [Teams]);
            END

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'Tasks') AND name = N'IX_Tasks_TeamId'
            )
                CREATE INDEX [IX_Tasks_TeamId] ON [Tasks] ([TeamId]);

            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'Sprints') AND name = N'TeamId'
            )
            BEGIN
                ALTER TABLE [Sprints] ADD [TeamId] INT NULL;
                UPDATE [Sprints] SET [TeamId] = (SELECT TOP 1 [Id] FROM [Teams]);
            END

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'Sprints') AND name = N'IX_Sprints_TeamId'
            )
                CREATE INDEX [IX_Sprints_TeamId] ON [Sprints] ([TeamId]);
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'Tasks')   AND name = N'IX_Tasks_TeamId')
                DROP INDEX [IX_Tasks_TeamId]   ON [Tasks];
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'Sprints') AND name = N'IX_Sprints_TeamId')
                DROP INDEX [IX_Sprints_TeamId] ON [Sprints];
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Tasks')   AND name = N'TeamId')
                ALTER TABLE [Tasks]   DROP COLUMN [TeamId];
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Sprints') AND name = N'TeamId')
                ALTER TABLE [Sprints] DROP COLUMN [TeamId];
        ");
    }
}
