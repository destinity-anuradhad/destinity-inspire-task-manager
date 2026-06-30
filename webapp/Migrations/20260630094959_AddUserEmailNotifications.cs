using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    // Adds Email address and per-user notification preference columns to AppUsers.
    // These columns were previously added via raw SQL at startup — this migration
    // makes that schema change tracked and reproducible on fresh deployments.
    public partial class AddUserEmailNotifications : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='Email')
                    ALTER TABLE [AppUsers] ADD [Email] NVARCHAR(200) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='NotifyOnComment')
                    ALTER TABLE [AppUsers] ADD [NotifyOnComment] BIT NOT NULL DEFAULT 1;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='NotifyOnBlocked')
                    ALTER TABLE [AppUsers] ADD [NotifyOnBlocked] BIT NOT NULL DEFAULT 1;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(table: "AppUsers", name: "Email");
            migrationBuilder.DropColumn(table: "AppUsers", name: "NotifyOnComment");
            migrationBuilder.DropColumn(table: "AppUsers", name: "NotifyOnBlocked");
        }
    }
}
