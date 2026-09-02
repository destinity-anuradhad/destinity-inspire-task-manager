using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddImmutableSprintHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Sprints",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletedBy",
                table: "Sprints",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SprintCompletionSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SprintId = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CommittedCount = table.Column<int>(type: "int", nullable: false),
                    CompletedCount = table.Column<int>(type: "int", nullable: false),
                    CarriedForwardCount = table.Column<int>(type: "int", nullable: false),
                    CommittedEstimate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CompletedEstimate = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintCompletionSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SprintCompletionSnapshots_Sprints_SprintId",
                        column: x => x.SprintId,
                        principalTable: "Sprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SprintWorkItemEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    FromSprintId = table.Column<int>(type: "int", nullable: true),
                    ToSprintId = table.Column<int>(type: "int", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Estimate = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OccurredBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintWorkItemEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SprintWorkItemEvents_Sprints_FromSprintId",
                        column: x => x.FromSprintId,
                        principalTable: "Sprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SprintWorkItemEvents_Sprints_ToSprintId",
                        column: x => x.ToSprintId,
                        principalTable: "Sprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SprintWorkItemEvents_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sprints_CompletedAt",
                table: "Sprints",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SprintCompletionSnapshots_SprintId",
                table: "SprintCompletionSnapshots",
                column: "SprintId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SprintWorkItemEvents_FromSprintId_OccurredAt",
                table: "SprintWorkItemEvents",
                columns: new[] { "FromSprintId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SprintWorkItemEvents_TaskId_OccurredAt",
                table: "SprintWorkItemEvents",
                columns: new[] { "TaskId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SprintWorkItemEvents_ToSprintId_OccurredAt",
                table: "SprintWorkItemEvents",
                columns: new[] { "ToSprintId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SprintCompletionSnapshots");

            migrationBuilder.DropTable(
                name: "SprintWorkItemEvents");

            migrationBuilder.DropIndex(
                name: "IX_Sprints_CompletedAt",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "CompletedBy",
                table: "Sprints");
        }
    }
}
