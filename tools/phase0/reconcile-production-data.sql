/*
    Phase 0 read-only reconciliation report.

    Run against a restored production backup first. This script performs no writes.
    Save every result set with the database name, backup identifier, and UTC run time.
*/

SET NOCOUNT ON;

SELECT
    DB_NAME() AS DatabaseName,
    SYSUTCDATETIME() AS CheckedAtUtc,
    @@SERVERNAME AS ServerName;

SELECT [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId];

SELECT
    (SELECT COUNT(*) FROM [Tasks]) AS TaskCount,
    (SELECT COUNT(*) FROM [Tasks] WHERE NULLIF(LTRIM(RTRIM([Owner])), '') IS NOT NULL) AS TasksWithLegacyOwner,
    (SELECT COUNT(*) FROM [TaskOwners]) AS NormalizedAssignmentCount,
    (SELECT COUNT(DISTINCT [TaskId]) FROM [TaskOwners]) AS TasksWithNormalizedOwner,
    (SELECT COUNT(*) FROM [Sprints]) AS SprintCount,
    (SELECT COUNT(*) FROM [Sprints] WHERE [IsActive] = 1) AS ActiveSprintCount;

-- Display names are not safe identity keys when duplicated.
SELECT [DisplayName], COUNT(*) AS UserCount
FROM [AppUsers]
GROUP BY [DisplayName]
HAVING COUNT(*) > 1;

-- Every non-empty legacy owner token must resolve to exactly one user before backfill.
WITH OwnerTokens AS
(
    SELECT
        t.[Id] AS TaskId,
        LTRIM(RTRIM(value)) AS OwnerName
    FROM [Tasks] t
    CROSS APPLY STRING_SPLIT(t.[Owner], ',')
    WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL
)
SELECT
    ot.[TaskId],
    ot.[OwnerName],
    COUNT(u.[Id]) AS MatchingUserCount
FROM OwnerTokens ot
LEFT JOIN [AppUsers] u ON u.[DisplayName] = ot.[OwnerName]
GROUP BY ot.[TaskId], ot.[OwnerName]
HAVING COUNT(u.[Id]) <> 1
ORDER BY ot.[TaskId], ot.[OwnerName];

-- Differences between the legacy display cache and normalized assignment rows.
WITH LegacyAssignments AS
(
    SELECT DISTINCT t.[Id] AS TaskId, u.[Id] AS UserId
    FROM [Tasks] t
    CROSS APPLY STRING_SPLIT(t.[Owner], ',') token
    INNER JOIN [AppUsers] u ON u.[DisplayName] = LTRIM(RTRIM(token.value))
    WHERE NULLIF(LTRIM(RTRIM(token.value)), '') IS NOT NULL
)
SELECT
    COALESCE(legacy.[TaskId], normalized.[TaskId]) AS TaskId,
    COALESCE(legacy.[UserId], normalized.[UserId]) AS UserId,
    CASE
        WHEN legacy.[TaskId] IS NULL THEN 'Normalized only'
        WHEN normalized.[TaskId] IS NULL THEN 'Legacy only'
    END AS Difference
FROM LegacyAssignments legacy
FULL OUTER JOIN [TaskOwners] normalized
    ON normalized.[TaskId] = legacy.[TaskId]
   AND normalized.[UserId] = legacy.[UserId]
WHERE legacy.[TaskId] IS NULL OR normalized.[TaskId] IS NULL
ORDER BY TaskId, UserId;

-- Capacity rows must map to one user and must not contain zero/null identities.
SELECT
    sc.[Id], sc.[SprintId], sc.[OwnerName], sc.[UserId], COUNT(u.[Id]) AS MatchingUserCount
FROM [SprintCapacities] sc
LEFT JOIN [AppUsers] u ON u.[DisplayName] = sc.[OwnerName]
GROUP BY sc.[Id], sc.[SprintId], sc.[OwnerName], sc.[UserId]
HAVING sc.[UserId] IS NULL OR sc.[UserId] = 0 OR COUNT(u.[Id]) <> 1;

-- Historical sprint risk inventory. Treat inactive sprint membership as evidence;
-- do not move or delete these rows during Phase 0.
SELECT
    s.[Id], s.[Name], s.[IsActive], s.[StartDate], s.[EndDate],
    COUNT(t.[Id]) AS TaskCount,
    SUM(CASE WHEN t.[Id] IS NOT NULL AND t.[ActualEndDate] IS NULL THEN 1 ELSE 0 END) AS IncompleteTaskCount
FROM [Sprints] s
LEFT JOIN [Tasks] t ON t.[SprintId] = s.[Id]
GROUP BY s.[Id], s.[Name], s.[IsActive], s.[StartDate], s.[EndDate]
ORDER BY s.[StartDate], s.[Id];

-- Candidate recovery evidence. These rows require human approval and backup
-- comparison; the reconciliation script never converts them into assignments.
SELECT [TaskId], [ChangedAt], [ChangedBy], [Action], [Details]
FROM [ActivityLog]
WHERE [Action] LIKE '%Owner%' OR [Action] LIKE '%Assign%'
ORDER BY [ChangedAt], [Id];

SELECT
    cc.[name] AS ConstraintName,
    OBJECT_NAME(cc.[parent_object_id]) AS TableName,
    cc.[is_disabled],
    cc.[is_not_trusted],
    cc.[definition]
FROM sys.check_constraints cc
WHERE OBJECT_NAME(cc.[parent_object_id]) IN ('AppUsers', 'TeamMembers');
