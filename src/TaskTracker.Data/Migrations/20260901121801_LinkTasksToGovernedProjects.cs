using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    /// <inheritdoc />
    public partial class LinkTasksToGovernedProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "Tasks",
                type: "int",
                nullable: true);

            // Reconcile legacy labels only if exactly one active project with the
            // same name exists in the task's team. Ambiguous labels stay null.
            migrationBuilder.Sql(@"
                UPDATE t SET ProjectId = p.Id
                FROM Tasks t INNER JOIN Projects p ON p.TeamId = t.TeamId
                    AND p.Status = 'Active' AND p.Name = t.Project
                WHERE t.TeamId IS NOT NULL
                    AND NULLIF(LTRIM(RTRIM(t.Project)), '') IS NOT NULL
                    AND (SELECT COUNT(*) FROM Projects p2
                         WHERE p2.TeamId = t.TeamId AND p2.Status = 'Active'
                           AND p2.Name = t.Project) = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId",
                table: "Tasks",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Projects_ProjectId",
                table: "Tasks",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Projects_ProjectId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_ProjectId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Tasks");
        }
    }
}
