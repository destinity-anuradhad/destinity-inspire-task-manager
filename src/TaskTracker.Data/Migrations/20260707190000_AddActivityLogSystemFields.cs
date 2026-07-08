using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskTracker.Migrations;

[Migration("20260707190000_AddActivityLogSystemFields")]
public partial class AddActivityLogSystemFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            -- Make TaskId nullable to support auth/system events with no task
            ALTER TABLE [ActivityLog] ALTER COLUMN [TaskId] INT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ActivityLog') AND name = 'TeamId')
                ALTER TABLE [ActivityLog] ADD [TeamId] INT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ActivityLog') AND name = 'Category')
                ALTER TABLE [ActivityLog] ADD [Category] NVARCHAR(50) NOT NULL DEFAULT 'Task';

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ActivityLog') AND name = 'IpAddress')
                ALTER TABLE [ActivityLog] ADD [IpAddress] NVARCHAR(45) NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('ActivityLog') AND name = 'IX_ActivityLog_TeamId')
                CREATE INDEX [IX_ActivityLog_TeamId] ON [ActivityLog] ([TeamId]);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('ActivityLog') AND name = 'IX_ActivityLog_Category')
                CREATE INDEX [IX_ActivityLog_Category] ON [ActivityLog] ([Category]);
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
