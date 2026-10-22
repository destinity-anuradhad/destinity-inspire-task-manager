using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskTracker.Migrations;

[Migration("20260701160000_AddSoftDelete")]
public partial class AddSoftDelete : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Tasks') AND name = N'IsDeleted')
                ALTER TABLE [Tasks] ADD [IsDeleted] BIT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Tasks') AND name = N'DeletedAt')
                ALTER TABLE [Tasks] ADD [DeletedAt] DATETIME2 NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Tasks') AND name = N'DeletedBy')
                ALTER TABLE [Tasks] ADD [DeletedBy] NVARCHAR(MAX) NULL;
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
