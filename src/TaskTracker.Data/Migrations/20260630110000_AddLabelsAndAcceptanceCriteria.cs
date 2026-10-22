using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    public partial class AddLabelsAndAcceptanceCriteria : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Tasks' AND COLUMN_NAME='Reporter')
                    ALTER TABLE [Tasks] ADD [Reporter] NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Tasks' AND COLUMN_NAME='Labels')
                    ALTER TABLE [Tasks] ADD [Labels] NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Tasks' AND COLUMN_NAME='AcceptanceCriteria')
                    ALTER TABLE [Tasks] ADD [AcceptanceCriteria] NVARCHAR(MAX) NULL;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Tasks' AND COLUMN_NAME='Labels')
                    ALTER TABLE [Tasks] DROP COLUMN [Labels];
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Tasks' AND COLUMN_NAME='AcceptanceCriteria')
                    ALTER TABLE [Tasks] DROP COLUMN [AcceptanceCriteria];
            ");
        }
    }
}
