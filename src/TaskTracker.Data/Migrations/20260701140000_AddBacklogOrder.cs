using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations;

[Migration("20260701140000_AddBacklogOrder")]
public partial class AddBacklogOrder : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Tasks' AND COLUMN_NAME = 'BacklogOrder'
            )
            BEGIN
                ALTER TABLE [Tasks] ADD [BacklogOrder] INT NULL;
            END
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Tasks' AND COLUMN_NAME = 'BacklogOrder'
            )
            BEGIN
                ALTER TABLE [Tasks] DROP COLUMN [BacklogOrder];
            END
        ");
    }
}
