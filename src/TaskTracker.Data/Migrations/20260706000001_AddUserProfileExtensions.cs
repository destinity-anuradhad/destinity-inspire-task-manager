using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    [Migration("20260706000001_AddUserProfileExtensions")]
    public partial class AddUserProfileExtensions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='JobTitle')
                    ALTER TABLE [AppUsers] ADD [JobTitle] nvarchar(200) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='Department')
                    ALTER TABLE [AppUsers] ADD [Department] nvarchar(200) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='DailyCapacityHours')
                    ALTER TABLE [AppUsers] ADD [DailyCapacityHours] decimal(5,2) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='WorkingDays')
                    ALTER TABLE [AppUsers] ADD [WorkingDays] nvarchar(50) NULL;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='WorkingDays')
                    ALTER TABLE [AppUsers] DROP COLUMN [WorkingDays];
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='DailyCapacityHours')
                    ALTER TABLE [AppUsers] DROP COLUMN [DailyCapacityHours];
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='Department')
                    ALTER TABLE [AppUsers] DROP COLUMN [Department];
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='JobTitle')
                    ALTER TABLE [AppUsers] DROP COLUMN [JobTitle];
            ");
        }
    }
}
