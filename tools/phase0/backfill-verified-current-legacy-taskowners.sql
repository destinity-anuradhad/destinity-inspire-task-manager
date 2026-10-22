/*
    Additive assignment recovery for DestiniyInspire_TaskManager.

    This script inserts only assignments represented by a current non-empty
    Tasks.Owner value that resolves to exactly one AppUsers.DisplayName. It
    does not update or delete Tasks, TaskOwners, or any other data.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Tasks', N'U') IS NULL
   OR OBJECT_ID(N'dbo.TaskOwners', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AppUsers', N'U') IS NULL
    THROW 51020, 'Tasks, TaskOwners, and AppUsers must exist before backfill.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Tasks t
    CROSS APPLY STRING_SPLIT(t.Owner, ',') token
    LEFT JOIN dbo.AppUsers u ON u.DisplayName = LTRIM(RTRIM(token.value))
    WHERE NULLIF(LTRIM(RTRIM(token.value)), '') IS NOT NULL
    GROUP BY t.Id, LTRIM(RTRIM(token.value))
    HAVING COUNT(DISTINCT u.Id) <> 1
)
    THROW 51021, 'Every current legacy owner must resolve to exactly one user.', 1;

INSERT INTO dbo.TaskOwners (TaskId, UserId)
SELECT DISTINCT t.Id, u.Id
FROM dbo.Tasks t
CROSS APPLY STRING_SPLIT(t.Owner, ',') token
INNER JOIN dbo.AppUsers u ON u.DisplayName = LTRIM(RTRIM(token.value))
WHERE NULLIF(LTRIM(RTRIM(token.value)), '') IS NOT NULL
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.TaskOwners existing
      WHERE existing.TaskId = t.Id
        AND existing.UserId = u.Id
  );

IF EXISTS
(
    SELECT 1
    FROM dbo.Tasks t
    CROSS APPLY STRING_SPLIT(t.Owner, ',') token
    INNER JOIN dbo.AppUsers u ON u.DisplayName = LTRIM(RTRIM(token.value))
    WHERE NULLIF(LTRIM(RTRIM(token.value)), '') IS NOT NULL
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.TaskOwners recovered
          WHERE recovered.TaskId = t.Id
            AND recovered.UserId = u.Id
      )
)
    THROW 51022, 'Task-owner backfill verification failed.', 1;

SELECT TaskId, UserId
FROM dbo.TaskOwners
WHERE TaskId IN
(
    SELECT Id
    FROM dbo.Tasks
    WHERE NULLIF(LTRIM(RTRIM(Owner)), '') IS NOT NULL
)
ORDER BY TaskId, UserId;

COMMIT TRANSACTION;
