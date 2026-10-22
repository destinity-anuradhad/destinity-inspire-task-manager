using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    public partial class AddWipLimitToWorkStates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WorkStates' AND COLUMN_NAME='WipLimit')
                    ALTER TABLE [WorkStates] ADD [WipLimit] INT NULL;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WorkStates' AND COLUMN_NAME='WipLimit')
                    ALTER TABLE [WorkStates] DROP COLUMN [WipLimit];
            ");
        }
    }
}
