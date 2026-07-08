using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskTracker.Migrations;

[Migration("20260701170000_AddSprintCapacities")]
public partial class AddSprintCapacities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'SprintCapacities')
            BEGIN
                CREATE TABLE [SprintCapacities] (
                    [Id]            INT           NOT NULL IDENTITY,
                    [SprintId]      INT           NOT NULL,
                    [OwnerName]     NVARCHAR(200) NOT NULL,
                    [CapacityHours] DECIMAL(18,2) NOT NULL DEFAULT 0,
                    CONSTRAINT [PK_SprintCapacities] PRIMARY KEY ([Id])
                );
                CREATE INDEX [IX_SprintCapacities_SprintId] ON [SprintCapacities] ([SprintId]);
                CREATE UNIQUE INDEX [IX_SprintCapacities_Sprint_Owner] ON [SprintCapacities] ([SprintId], [OwnerName]);
            END
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
