using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaskTracker.Data;

#nullable disable

namespace TaskTracker.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260831000100_BackfillVerifiedLegacyTaskOwners")]
public partial class BackfillVerifiedLegacyTaskOwners : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'[Tasks]', N'U') IS NULL
               OR OBJECT_ID(N'[TaskOwners]', N'U') IS NULL
               OR OBJECT_ID(N'[AppUsers]', N'U') IS NULL
                THROW 51006,
                    'Migration stopped: Tasks, TaskOwners, and AppUsers must exist before assignment backfill.',
                    1;

            IF EXISTS (
                SELECT token.[OwnerName]
                FROM (
                    SELECT LTRIM(RTRIM(value)) AS [OwnerName]
                    FROM [Tasks] t
                    CROSS APPLY STRING_SPLIT(t.[Owner], ',')
                    WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL
                ) token
                LEFT JOIN [AppUsers] u ON u.[DisplayName] = token.[OwnerName]
                GROUP BY token.[OwnerName]
                HAVING COUNT(DISTINCT u.[Id]) <> 1
            )
                THROW 51007,
                    'Migration stopped: every legacy owner must match exactly one AppUser.',
                    1;

            INSERT INTO [TaskOwners] ([TaskId], [UserId])
            SELECT DISTINCT t.[Id], u.[Id]
            FROM [Tasks] t
            CROSS APPLY STRING_SPLIT(t.[Owner], ',') token
            INNER JOIN [AppUsers] u
                ON u.[DisplayName] = LTRIM(RTRIM(token.value))
            WHERE NULLIF(LTRIM(RTRIM(token.value)), '') IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1
                  FROM [TaskOwners] existing
                  WHERE existing.[TaskId] = t.[Id]
                    AND existing.[UserId] = u.[Id]
              );

            IF EXISTS (
                SELECT 1
                FROM [Tasks] t
                CROSS APPLY STRING_SPLIT(t.[Owner], ',') token
                INNER JOIN [AppUsers] u
                    ON u.[DisplayName] = LTRIM(RTRIM(token.value))
                WHERE NULLIF(LTRIM(RTRIM(token.value)), '') IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [TaskOwners] assignment
                      WHERE assignment.[TaskId] = t.[Id]
                        AND assignment.[UserId] = u.[Id]
                  )
            )
                THROW 51008,
                    'Migration stopped: assignment backfill verification failed.',
                    1;
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately preserve recovered assignments. They cannot be distinguished
        // safely from assignments created after this migration was applied.
    }
}
