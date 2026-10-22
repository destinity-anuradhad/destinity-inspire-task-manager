using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaskTracker.Data;

#nullable disable

namespace TaskTracker.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261019150000_UseTenMinuteEstimatePrecision")]
public partial class UseTenMinuteEstimatePrecision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "Weight", table: "Tasks", type: "decimal(18,6)", nullable: true,
            oldClrType: typeof(decimal), oldType: "decimal(18,2)", oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "Estimate", table: "TaskTemplates", type: "decimal(18,6)", nullable: true,
            oldClrType: typeof(decimal), oldType: "decimal(18,2)", oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "Weight", table: "Tasks", type: "decimal(18,2)", nullable: true,
            oldClrType: typeof(decimal), oldType: "decimal(18,6)", oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "Estimate", table: "TaskTemplates", type: "decimal(18,2)", nullable: true,
            oldClrType: typeof(decimal), oldType: "decimal(18,6)", oldNullable: true);
    }
}
