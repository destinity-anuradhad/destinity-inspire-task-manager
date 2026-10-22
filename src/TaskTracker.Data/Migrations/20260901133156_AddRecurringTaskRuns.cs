using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringTaskRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecurringTaskRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecurringTaskDefinitionId = table.Column<int>(type: "int", nullable: false),
                    ScheduledFor = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TaskId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringTaskRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringTaskRuns_RecurringTaskDefinitions_RecurringTaskDefinitionId",
                        column: x => x.RecurringTaskDefinitionId,
                        principalTable: "RecurringTaskDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecurringTaskRuns_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskRuns_RecurringTaskDefinitionId_ScheduledFor",
                table: "RecurringTaskRuns",
                columns: new[] { "RecurringTaskDefinitionId", "ScheduledFor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskRuns_TaskId",
                table: "RecurringTaskRuns",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringTaskRuns");
        }
    }
}
