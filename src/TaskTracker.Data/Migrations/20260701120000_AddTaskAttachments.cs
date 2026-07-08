using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    public partial class AddTaskAttachments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='TaskAttachments')
                BEGIN
                    CREATE TABLE [TaskAttachments] (
                        [Id]          INT IDENTITY(1,1) NOT NULL,
                        [TaskId]      INT NOT NULL,
                        [FileName]    NVARCHAR(MAX) NOT NULL DEFAULT '',
                        [StoredName]  NVARCHAR(MAX) NOT NULL DEFAULT '',
                        [FileSize]    BIGINT NOT NULL DEFAULT 0,
                        [ContentType] NVARCHAR(MAX) NULL,
                        [UploadedBy]  NVARCHAR(MAX) NULL,
                        [UploadedAt]  DATETIME2 NOT NULL DEFAULT GETDATE(),
                        CONSTRAINT [PK_TaskAttachments] PRIMARY KEY ([Id])
                    );
                    CREATE INDEX [IX_TaskAttachments_TaskId] ON [TaskAttachments] ([TaskId]);
                END
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='TaskAttachments')
                    DROP TABLE [TaskAttachments];
            ");
        }
    }
}
