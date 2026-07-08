using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskTracker.Migrations;

[Migration("20260707100000_AddSavedQueryTeamId")]
public partial class AddSavedQueryTeamId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'SavedQueries') AND name = N'TeamId')
                ALTER TABLE [SavedQueries] ADD [TeamId] INT NULL;
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'SavedQueries') AND name = N'TeamId')
                ALTER TABLE [SavedQueries] DROP COLUMN [TeamId];
        ");
    }
}
