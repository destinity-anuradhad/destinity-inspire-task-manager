using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    [Migration("20260702170000_AddTaskWatchers")]
    public partial class AddTaskWatchers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='TaskWatchers')
                BEGIN
                    CREATE TABLE [TaskWatchers] (
                        [Id]        INT IDENTITY(1,1) NOT NULL,
                        [TaskId]    INT NOT NULL,
                        [UserId]    INT NOT NULL,
                        [CreatedAt] DATETIME2 NOT NULL,
                        CONSTRAINT [PK_TaskWatchers] PRIMARY KEY ([Id])
                    );
                    CREATE INDEX [IX_TaskWatchers_TaskId] ON [TaskWatchers] ([TaskId]);
                    CREATE UNIQUE INDEX [IX_TaskWatchers_TaskId_UserId] ON [TaskWatchers] ([TaskId], [UserId]);
                END
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='TaskWatchers')
                    DROP TABLE [TaskWatchers];
            ");
        }
    }
}
