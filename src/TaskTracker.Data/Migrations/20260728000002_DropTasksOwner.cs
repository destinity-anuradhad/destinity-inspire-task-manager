using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    public partial class DropTasksOwner : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1
                    FROM [Tasks]
                    WHERE NULLIF(LTRIM(RTRIM([Owner])), '') IS NOT NULL
                )
                    THROW 51001,
                        'Migration stopped: Tasks.Owner contains assignment data. Reconcile and backfill TaskOwners before dropping it.',
                        1;
            ");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_Owner",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Owner",
                table: "Tasks");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Owner",
                table: "Tasks",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Owner",
                table: "Tasks",
                column: "Owner");
        }
    }
}
