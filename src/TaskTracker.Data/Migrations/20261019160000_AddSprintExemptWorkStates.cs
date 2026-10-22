using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaskTracker.Data;

#nullable disable

namespace TaskTracker.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261019160000_AddSprintExemptWorkStates")]
public partial class AddSprintExemptWorkStates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[WorkStates]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [dbo].[WorkStates] WHERE [Name] = 'On hold')
        INSERT INTO [dbo].[WorkStates] ([Name], [Color], [SortOrder], [WipLimit], [ItemTypes])
        VALUES ('On hold', '#f59e0b', (SELECT ISNULL(MAX([SortOrder]), 0) + 10 FROM [dbo].[WorkStates]), NULL, 'Task,Bug,Story,Feature,Epic');

    IF NOT EXISTS (SELECT 1 FROM [dbo].[WorkStates] WHERE [Name] = 'Rejected')
        INSERT INTO [dbo].[WorkStates] ([Name], [Color], [SortOrder], [WipLimit], [ItemTypes])
        VALUES ('Rejected', '#ef4444', (SELECT ISNULL(MAX([SortOrder]), 0) + 10 FROM [dbo].[WorkStates]), NULL, 'Task,Bug,Story,Feature,Epic');
END");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep these shared work-state rows on rollback: tasks may already use them.
    }
}
