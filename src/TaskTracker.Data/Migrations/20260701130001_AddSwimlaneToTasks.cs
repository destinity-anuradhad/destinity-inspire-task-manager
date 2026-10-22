using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    public partial class AddSwimlaneToTasks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Tasks' AND COLUMN_NAME='Swimlane')
                    ALTER TABLE [Tasks] ADD [Swimlane] NVARCHAR(50) NULL;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Tasks' AND COLUMN_NAME='Swimlane')
                    ALTER TABLE [Tasks] DROP COLUMN [Swimlane];
            ");
        }
    }
}
