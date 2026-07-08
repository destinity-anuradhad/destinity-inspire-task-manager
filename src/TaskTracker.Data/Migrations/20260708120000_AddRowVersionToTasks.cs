using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations;

public partial class AddRowVersionToTasks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'Tasks') AND name = N'RowVersion'
            )
                ALTER TABLE [Tasks] ADD [RowVersion] ROWVERSION NOT NULL;
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'Tasks') AND name = N'RowVersion'
            )
                ALTER TABLE [Tasks] DROP COLUMN [RowVersion];
        ");
    }
}
