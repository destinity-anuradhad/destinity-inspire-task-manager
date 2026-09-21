/* ============================================================================
   01-prepare_v2.sql
   Run on:   DestiniyInspire_TaskManager_v2   (the freshly RESTORED copy of v1)
   When:     BEFORE 02-deploy-schema.ps1
   Why:      The new app reads ownership from the Tasks.Owner string, not the legacy
             TaskOwners join table. ~24 assignments in v1 exist ONLY in TaskOwners and
             would appear "unowned" in the new app. This back-fills those into Tasks.Owner.
   Note:     Non-destructive — TaskOwners itself is left intact (it's kept in the new
             schema too). Idempotent; multi-owner tasks resolve over repeat runs.
   ============================================================================ */
SET NOCOUNT ON;

IF OBJECT_ID(N'[TaskOwners]', N'U') IS NULL
BEGIN
    PRINT 'TaskOwners does not exist — nothing to back-fill.';
    RETURN;
END;

UPDATE t
SET t.Owner = CASE WHEN ISNULL(t.Owner, '') = '' THEN u.DisplayName
                   ELSE t.Owner + ', ' + u.DisplayName END
FROM Tasks t
JOIN TaskOwners tox ON tox.TaskId = t.Id
JOIN AppUsers   u   ON u.Id       = tox.UserId
WHERE ISNULL(t.Owner, '') NOT LIKE '%' + u.DisplayName + '%';

PRINT 'Back-filled owners into Tasks.Owner: ' + CAST(@@ROWCOUNT AS varchar(10)) + ' task(s) this pass.';

DECLARE @remaining int = (
    SELECT COUNT(*)
    FROM TaskOwners tox
    JOIN Tasks    t ON t.Id = tox.TaskId
    JOIN AppUsers u ON u.Id = tox.UserId
    WHERE ISNULL(t.Owner, '') NOT LIKE '%' + u.DisplayName + '%'
);
IF @remaining > 0
    PRINT 'NOTE: ' + CAST(@remaining AS varchar(10)) + ' assignment(s) still not reflected — re-run once more.';
ELSE
    PRINT 'All TaskOwners assignments are reflected in Tasks.Owner.';
