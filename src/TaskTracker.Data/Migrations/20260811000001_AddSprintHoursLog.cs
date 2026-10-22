using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaskTracker.Data;

#nullable disable

namespace TaskTracker.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260811000001_AddSprintHoursLog")]
    public partial class AddSprintHoursLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SprintHoursLogs",
                columns: table => new
                {
                    Id       = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SprintId = table.Column<int>(type: "int", nullable: false),
                    TaskId   = table.Column<int>(type: "int", nullable: false),
                    Hours    = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LoggedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LoggedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintHoursLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SprintHoursLogs_SprintId",
                table: "SprintHoursLogs",
                column: "SprintId");

            migrationBuilder.CreateIndex(
                name: "IX_SprintHoursLogs_SprintId_TaskId",
                table: "SprintHoursLogs",
                columns: new[] { "SprintId", "TaskId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SprintHoursLogs");
        }
    }
}
