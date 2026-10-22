using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTaskOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add Owner column if it doesn't already exist (it may pre-date EF management)
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'Tasks' AND COLUMN_NAME = 'Owner'
                )
                    ALTER TABLE [Tasks] ADD [Owner] nvarchar(max) NULL;
            ");

            // Drop TaskOwners join table if it exists
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'[TaskOwners]', N'U') IS NOT NULL
                   AND EXISTS (SELECT 1 FROM [TaskOwners])
                    THROW 51002,
                        'Migration stopped: TaskOwners contains assignment data and cannot be dropped safely.',
                        1;

                IF OBJECT_ID(N'[TaskOwners]', N'U') IS NOT NULL
                    DROP TABLE [TaskOwners];
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Owner",
                table: "Tasks");

            migrationBuilder.CreateTable(
                name: "TaskOwners",
                columns: table => new
                {
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskOwners", x => new { x.TaskId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TaskOwners_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaskOwners_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskOwners_UserId",
                table: "TaskOwners",
                column: "UserId");
        }
    }
}
