using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    public partial class NormalizeDb : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SprintCapacity → link to AppUser by Id
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "SprintCapacities",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE sc
                SET sc.UserId = u.Id
                FROM SprintCapacities sc
                INNER JOIN AppUsers u ON u.DisplayName = sc.OwnerName
            ");

            // Covering indexes — eliminate full-scan hot paths
            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TeamId_IsDeleted_Status",
                table: "Tasks",
                columns: new[] { "TeamId", "IsDeleted", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_TeamId_IsDeleted_Status",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "SprintCapacities");
        }
    }
}
