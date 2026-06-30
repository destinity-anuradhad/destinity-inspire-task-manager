using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    // Baseline migration — DB was created manually before EF migrations were introduced.
    // Up/Down are intentionally empty; the AppDbContextModelSnapshot is the source of truth.
    // Future migrations diff against the snapshot and produce real schema changes.
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) { }
        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
