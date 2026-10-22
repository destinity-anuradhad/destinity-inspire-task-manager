using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskTracker.Migrations;

[Migration("20260707180000_AddEmailLog")]
public partial class AddEmailLog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'EmailLogs')
            BEGIN
                CREATE TABLE [EmailLogs] (
                    [Id]           INT           NOT NULL IDENTITY,
                    [ToEmail]      NVARCHAR(256) NOT NULL,
                    [ToName]       NVARCHAR(200) NOT NULL,
                    [Subject]      NVARCHAR(500) NOT NULL,
                    [HtmlBody]     NVARCHAR(MAX) NOT NULL,
                    [SentAt]       DATETIME2     NOT NULL,
                    [IsSuccess]    BIT           NOT NULL DEFAULT 0,
                    [ErrorMessage] NVARCHAR(MAX) NULL,
                    CONSTRAINT [PK_EmailLogs] PRIMARY KEY ([Id])
                );
                CREATE INDEX [IX_EmailLogs_SentAt]    ON [EmailLogs] ([SentAt]);
                CREATE INDEX [IX_EmailLogs_ToEmail]   ON [EmailLogs] ([ToEmail]);
                CREATE INDEX [IX_EmailLogs_IsSuccess] ON [EmailLogs] ([IsSuccess]);
            END
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS [EmailLogs];");
    }
}
