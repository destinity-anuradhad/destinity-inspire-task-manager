/* READ-ONLY check: finds empty (NULL) values in date columns the app expects to always be filled.
   Run in SSMS against the database the app uses. It changes nothing.
   Any row with NullRows > 0 causes "SqlNullValueException: Data is Null" when that table is read. */
SET NOCOUNT ON;
DECLARE @cols TABLE (TableName sysname, ColumnName sysname);
INSERT @cols VALUES
    (N'ActivityLog', N'ChangedAt'),
    (N'ApiClients', N'CreatedAt'),
    (N'ApiIdempotencyRecords', N'CreatedAt'),
    (N'AppUsers', N'CreatedAt'),
    (N'AutomationRules', N'CreatedAt'),
    (N'AutomationRuleRuns', N'OccurredAt'),
    (N'CustomFieldDefinitions', N'CreatedAt'),
    (N'EmailLogs', N'SentAt'),
    (N'EmailOutboxMessages', N'AvailableAt'),
    (N'EmailOutboxMessages', N'CreatedAt'),
    (N'ImportJobs', N'CreatedAt'),
    (N'Notifications', N'CreatedAt'),
    (N'Projects', N'CreatedAt'),
    (N'ProjectReleases', N'CreatedAt'),
    (N'RecurringTaskDefinitions', N'CreatedAt'),
    (N'RecurringTaskDefinitions', N'NextRunAt'),
    (N'RecurringTaskRuns', N'CreatedAt'),
    (N'RecurringTaskRuns', N'ScheduledFor'),
    (N'SavedQueries', N'CreatedAt'),
    (N'SlaPolicies', N'CreatedAt'),
    (N'Sprints', N'CreatedAt'),
    (N'SprintCompletionSnapshots', N'CompletedAt'),
    (N'SprintHoursLogs', N'LoggedAt'),
    (N'SprintWorkItemEvents', N'OccurredAt'),
    (N'TaskAttachments', N'UploadedAt'),
    (N'TaskChecklistItems', N'CreatedAt'),
    (N'TaskCustomFieldValues', N'UpdatedAt'),
    (N'Tasks', N'CreatedAt'),
    (N'Tasks', N'UpdatedAt'),
    (N'TaskLinks', N'CreatedAt'),
    (N'TaskTemplates', N'CreatedAt'),
    (N'TaskWatchers', N'CreatedAt'),
    (N'Teams', N'CreatedAt'),
    (N'TeamMembers', N'JoinedAt'),
    (N'TimeEntries', N'CreatedAt'),
    (N'TimeEntries', N'WorkDate'),
    (N'UserTimeOff', N'CreatedAt'),
    (N'UserTimeOff', N'EndDate'),
    (N'UserTimeOff', N'StartDate'),
    (N'WebhookDeliveries', N'AvailableAt'),
    (N'WebhookDeliveries', N'CreatedAt'),
    (N'WebhookSubscriptions', N'CreatedAt'),
    (N'WorkflowDefinitions', N'CreatedAt');

DECLARE @sql nvarchar(max) = N'';
SELECT @sql += CASE WHEN @sql = N'' THEN N'' ELSE N' UNION ALL ' END
    + N'SELECT N''' + c.TableName + N''' AS TableName, N''' + c.ColumnName + N''' AS ColumnName, '
    + N'COUNT(*) AS NullRows FROM dbo.' + QUOTENAME(c.TableName) + N' WHERE ' + QUOTENAME(c.ColumnName) + N' IS NULL'
FROM @cols c
WHERE COL_LENGTH(N'dbo.' + c.TableName, c.ColumnName) IS NOT NULL;

SET @sql = N'SELECT * FROM (' + @sql + N') r WHERE NullRows > 0 ORDER BY TableName, ColumnName;';
EXEC (@sql);

-- Expected columns that do not exist at all (schema older than the code):
SELECT c.TableName, c.ColumnName AS MissingColumn
FROM @cols c
WHERE OBJECT_ID(N'dbo.' + c.TableName) IS NOT NULL AND COL_LENGTH(N'dbo.' + c.TableName, c.ColumnName) IS NULL;
