using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations;

[Migration("20260701150000_AddSavedQueries")]
public partial class AddSavedQueries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = 'SavedQueries'
            )
            BEGIN
                CREATE TABLE [SavedQueries] (
                    [Id]          INT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    [Name]        NVARCHAR(200) NOT NULL,
                    [Description] NVARCHAR(MAX) NULL,
                    [Filters]     NVARCHAR(MAX) NOT NULL DEFAULT '{}',
                    [IsShared]    BIT           NOT NULL DEFAULT 0,
                    [CreatedBy]   NVARCHAR(200) NULL,
                    [CreatedAt]   DATETIME2     NOT NULL DEFAULT GETDATE()
                );
                CREATE INDEX [IX_SavedQueries_CreatedBy] ON [SavedQueries] ([CreatedBy]);
            END
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SavedQueries')
                DROP TABLE [SavedQueries];
        ");
    }
}
