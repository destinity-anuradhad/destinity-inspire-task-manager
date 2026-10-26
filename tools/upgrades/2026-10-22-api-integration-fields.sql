/* v1 API integration fields — additive, idempotent upgrade for an existing database.
   Equivalent of EF migration 20261022100000_AddApiIntegrationFields
   (see docs/non-destructive-existing-database-upgrade.md and docs/api-v1.md).

   Adds, only when absent:
     - Tasks.UpdatedAt        datetime2 NOT NULL DEFAULT SYSUTCDATETIME()  (UTC; incremental API sync, ETags)
     - IX_Tasks_TeamId_UpdatedAt
     - ApiClients.OwnTasksOnly bit NOT NULL DEFAULT 0                      (personal keys limited to own tasks)
   Backfills Tasks.UpdatedAt from each task's latest ActivityLog entry (or CreatedAt), converting
   server-local time to UTC with the database server's current offset: every row when the column is
   new, otherwise only the empty ones.

   Databases upgraded from v1 already have a legacy, nullable Tasks.UpdatedAt that is mostly empty. The
   script keeps that column and its existing values, fills only its NULLs and gives it a default for new
   rows (the app reads it as always set).

   Issues no DROP, DELETE, TRUNCATE or ALTER COLUMN. Existing rows keep every value they have; existing
   API keys get OwnTasksOnly = 0, i.e. today's behaviour. Safe to run more than once. */
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

-- The backfill value; dynamic SQL because the column may not exist when this batch is compiled.
DECLARE @backfill nvarchar(max) = N'
    UPDATE t
    SET t.UpdatedAt = DATEADD(MINUTE, DATEDIFF(MINUTE, GETDATE(), GETUTCDATE()),
            COALESCE((SELECT MAX(a.ChangedAt) FROM dbo.ActivityLog a WHERE a.TaskId = t.Id), t.CreatedAt))
    FROM dbo.Tasks t';

IF COL_LENGTH(N'dbo.Tasks', N'UpdatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Tasks ADD UpdatedAt datetime2 NOT NULL
        CONSTRAINT DF_Tasks_UpdatedAt DEFAULT (SYSUTCDATETIME());
    EXEC (@backfill);   -- new column: every row
END
ELSE
BEGIN
    -- Legacy column: fill only the empty values, and default new rows if nothing else does.
    EXEC (@backfill + N' WHERE t.UpdatedAt IS NULL');
    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints
                   WHERE parent_object_id = OBJECT_ID(N'dbo.Tasks')
                     AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Tasks'), N'UpdatedAt', 'ColumnId'))
        EXEC (N'ALTER TABLE dbo.Tasks ADD CONSTRAINT DF_Tasks_UpdatedAt DEFAULT (SYSUTCDATETIME()) FOR UpdatedAt;');
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Tasks') AND name = N'IX_Tasks_TeamId_UpdatedAt')
    EXEC (N'CREATE INDEX IX_Tasks_TeamId_UpdatedAt ON dbo.Tasks (TeamId, UpdatedAt);');

IF COL_LENGTH(N'dbo.ApiClients', N'OwnTasksOnly') IS NULL
    ALTER TABLE dbo.ApiClients ADD OwnTasksOnly bit NOT NULL
        CONSTRAINT DF_ApiClients_OwnTasksOnly DEFAULT (0);

-- Keep the EF migration chain in step, so `dotnet ef` treats this migration as applied.
-- (Dynamic SQL: SQL Server does not short-circuit, so a static reference fails when the table is absent.)
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
    EXEC (N'
        IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N''20261022100000_AddApiIntegrationFields'')
            INSERT dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
            VALUES (N''20261022100000_AddApiIntegrationFields'', N''10.0.9'');');

COMMIT TRANSACTION;
GO

-- Verification (read-only; a separate batch so it compiles after the columns exist)
SELECT
    (SELECT COUNT(*) FROM dbo.Tasks)                                   AS Tasks,
    (SELECT COUNT(*) FROM dbo.Tasks WHERE UpdatedAt IS NULL)           AS TasksWithoutUpdatedAt,
    (SELECT COUNT(*) FROM dbo.ApiClients)                              AS ApiClients,
    (SELECT COUNT(*) FROM dbo.ApiClients WHERE OwnTasksOnly = 1)       AS OwnTasksOnlyKeys;
