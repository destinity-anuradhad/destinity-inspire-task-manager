using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations;

public partial class AddActivityLogUserAgent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID('ActivityLog') AND name = 'UserAgent'
            )
            BEGIN
                ALTER TABLE ActivityLog ADD UserAgent NVARCHAR(512) NULL;
            END
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID('ActivityLog') AND name = 'UserAgent'
            )
            BEGIN
                ALTER TABLE ActivityLog DROP COLUMN UserAgent;
            END
        ");
    }
}
