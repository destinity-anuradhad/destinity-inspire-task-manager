using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaskTracker.Data;

#nullable disable

namespace TaskTracker.Migrations
{
    /// <summary>
    /// Adds the "In QA" board column (a <c>WorkState</c> row) for the Task flow:
    /// To Do → In Progress → Development Done → In QA → Done. Idempotent and data-only —
    /// no schema change, so the model snapshot is unaffected. The attributes below let EF
    /// discover and apply it via <c>database update</c> without a generated Designer file.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260907000000_AddInQaWorkState")]
    public partial class AddInQaWorkState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Insert "In QA" immediately after "Development Done" (making room in SortOrder),
            // but only if it doesn't already exist so re-runs / manual adds are safe.
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [WorkStates] WHERE [Name] = 'In QA')
BEGIN
    DECLARE @dd INT = (SELECT MIN([SortOrder]) FROM [WorkStates] WHERE [Name] = 'Development Done');
    IF @dd IS NULL SET @dd = (SELECT ISNULL(MAX([SortOrder]), 0) FROM [WorkStates]);
    UPDATE [WorkStates] SET [SortOrder] = [SortOrder] + 10 WHERE [SortOrder] > @dd;
    INSERT INTO [WorkStates] ([Name], [Color], [SortOrder], [WipLimit], [ItemTypes])
    VALUES ('In QA', '#a855f7', @dd + 5, NULL, 'Task');
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [WorkStates] WHERE [Name] = 'In QA';");
        }
    }
}
