/*
    Read-only gate for the existing-database upgrade path.

    Run this only against an isolated restore. It performs no DDL or DML.
    A BLOCK result means do not run `dotnet ef database update` or the
    generated idempotent script against that database.
*/

SET NOCOUNT ON;

DECLARE @hasTasks bit = IIF(OBJECT_ID(N'dbo.Tasks', N'U') IS NULL, 0, 1);
DECLARE @hasTaskOwners bit = IIF(OBJECT_ID(N'dbo.TaskOwners', N'U') IS NULL, 0, 1);
DECLARE @hasLegacyOwner bit = IIF(
    COL_LENGTH(N'dbo.Tasks', N'Owner') IS NULL, 0, 1);
DECLARE @dropOwnerRecorded bit = IIF(EXISTS
(
    SELECT 1
    FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20260728000002_DropTasksOwner'
), 1, 0);

SELECT
    DB_NAME() AS DatabaseName,
    SYSUTCDATETIME() AS CheckedAtUtc,
    @hasTasks AS HasTasksTable,
    @hasTaskOwners AS HasTaskOwnersTable,
    @hasLegacyOwner AS HasLegacyOwnerColumn,
    @dropOwnerRecorded AS DropOwnerMigrationRecorded;

SELECT
    CASE
        WHEN @hasTasks = 0 THEN 'BLOCK'
        WHEN @hasLegacyOwner = 1 AND @hasTaskOwners = 0 THEN 'BLOCK'
        WHEN @dropOwnerRecorded = 1 AND @hasLegacyOwner = 1 THEN 'BLOCK'
        WHEN @dropOwnerRecorded = 1 AND @hasLegacyOwner = 0 THEN 'BLOCK'
        ELSE 'BLOCK'
    END AS Decision,
    'The current EF migration chain contains a destructive owner/TaskOwners path. '
        + 'Preserve this database; use only an explicitly reviewed additive upgrade script.' AS Reason;

SELECT MigrationId, ProductVersion
FROM dbo.__EFMigrationsHistory
WHERE MigrationId IN
(
    N'20260728000002_DropTasksOwner',
    N'20260817032043_NormalizationPhase1_TaskOwners',
    N'20260831000100_BackfillVerifiedLegacyTaskOwners'
)
ORDER BY MigrationId;
