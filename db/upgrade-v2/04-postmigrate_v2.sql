/* ============================================================================
   03-postmigrate_v2.sql   (OPTIONAL data cleanup)
   Run on:   DestiniyInspire_TaskManager_v2
   When:     AFTER 02-deploy-schema.ps1   (the EstimateUnit column is added by the migration)
   Why:      The migration adds Tasks.EstimateUnit defaulting to 'Points' for existing
             rows. This project estimates in HOURS, so flip legacy tasks to 'Hours'.
   Safe:     Idempotent. Skip this file if you prefer to leave estimates as 'Points'.
   ============================================================================ */
SET NOCOUNT ON;

IF COL_LENGTH('Tasks', 'EstimateUnit') IS NULL
BEGIN
    PRINT 'Tasks.EstimateUnit not found — run 02-upgrade_v2.sql first.';
    RETURN;
END;

UPDATE Tasks
SET EstimateUnit = 'Hours'
WHERE ISNULL(EstimateUnit, '') IN ('', 'Points');

PRINT 'Set EstimateUnit = Hours on ' + CAST(@@ROWCOUNT AS varchar(10)) + ' task(s).';
