/*
  Load-test data seeding — backlog #29 (large-team query performance).

  Creates one large team with many users and many tasks so the "materialize entire team"
  read paths (task list, dashboard, charts, export) can be measured under realistic volume.

  SAFETY
  ------
  * Intended for a NON-PRODUCTION perf/staging database only.
  * Everything it creates is tagged so it can be removed cleanly (see the CLEANUP block at the end):
      - Team name     = 'LOADTEST Team'
      - Usernames     start with 'lt_'
      - Tasks.CreatedBy = 'loadtest-seed'
  * The guard below refuses to run unless you set @Confirm = 'YES'.

  USAGE (app stopped; point at the perf DB via the connection string)
  -------------------------------------------------------------------
    sqlcmd -S <server> -d <perfdb> -U <user> -i tools/loadtest/seed-loadtest-data.sql
  Adjust @UserCount / @TaskCount below for the volume you want to test.
*/

SET NOCOUNT ON;

DECLARE @Confirm   varchar(10) = 'NO';    -- <<< set to 'YES' to run
DECLARE @UserCount int         = 200;     -- team members
DECLARE @TaskCount int         = 50000;   -- tasks in the team

IF @Confirm <> 'YES'
BEGIN
    RAISERROR('Refusing to seed: set @Confirm = ''YES'' (and confirm this is a non-production DB).', 16, 1);
    RETURN;
END

-- ── 1. Team ──────────────────────────────────────────────────────────────────
DECLARE @TeamId int;
SELECT @TeamId = Id FROM Teams WHERE Name = 'LOADTEST Team';
IF @TeamId IS NULL
BEGIN
    INSERT INTO Teams (Name, IsActive) VALUES ('LOADTEST Team', 1);
    SET @TeamId = SCOPE_IDENTITY();
END

-- ── 2. Users (Active, unique display names) ─────────────────────────────────
;WITH nums AS (
    SELECT TOP (@UserCount) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT INTO AppUsers (Username, PasswordHash, Role, DisplayName, Status, CreatedAt, SessionVersion, NotifyOnAssigned, NotifyOnComment, NotifyOnBlocked, MustChangePassword)
SELECT
    'lt_user_'  + RIGHT('00000' + CAST(n AS varchar(6)), 5),
    'LOADTEST-NOT-A-REAL-HASH',            -- not for interactive login
    'Member',
    'LT User '  + RIGHT('00000' + CAST(n AS varchar(6)), 5),
    'Active', SYSUTCDATETIME(), 1, 1, 1, 1, 1
FROM nums
WHERE NOT EXISTS (SELECT 1 FROM AppUsers u WHERE u.Username = 'lt_user_' + RIGHT('00000' + CAST(n AS varchar(6)), 5));

-- ── 3. Team membership ──────────────────────────────────────────────────────
INSERT INTO TeamMembers (TeamId, UserId, TeamRole, JoinedAt)
SELECT @TeamId, u.Id, 'Member', SYSUTCDATETIME()
FROM AppUsers u
WHERE u.Username LIKE 'lt\_user\_%' ESCAPE '\'
  AND NOT EXISTS (SELECT 1 FROM TeamMembers m WHERE m.TeamId = @TeamId AND m.UserId = u.Id);

-- Snapshot the seeded users with a dense 1..N index for round-robin owner assignment.
IF OBJECT_ID('tempdb..#U') IS NOT NULL DROP TABLE #U;
SELECT Id, DisplayName, ROW_NUMBER() OVER (ORDER BY Id) AS rn
INTO #U
FROM AppUsers WHERE Username LIKE 'lt\_user\_%' ESCAPE '\';
DECLARE @Users int = (SELECT COUNT(*) FROM #U);

-- ── 4. Tasks (varied status/priority/type; one owner each, round-robin) ─────
;WITH nums AS (
    SELECT TOP (@TaskCount) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c
)
INSERT INTO Tasks (Goal, TeamId, Status, ItemType, Priority, EstimateUnit, Weight, Owner, Reporter, CreatedBy, CreatedAt, IsDeleted, TargetDate)
SELECT
    'LOADTEST task ' + CAST(n AS varchar(10)),
    @TeamId,
    CHOOSE(1 + (n % 5), 'New', 'Active', 'In Progress', 'Resolved', 'Closed'),
    CHOOSE(1 + (n % 5), 'Task', 'Bug', 'Story', 'Feature', 'Epic'),
    CHOOSE(1 + (n % 4), 'Critical', 'High', 'Medium', 'Low'),
    CASE WHEN n % 2 = 0 THEN 'Hours' ELSE 'Points' END,
    (n % 13),
    u.DisplayName,
    u.DisplayName,
    'loadtest-seed',
    DATEADD(DAY, -(n % 120), SYSUTCDATETIME()),
    0,
    DATEADD(DAY, (n % 30) - 10, SYSUTCDATETIME())
FROM nums
JOIN #U u ON u.rn = (n % @Users) + 1;

-- ── 5. Owner junction (authoritative — mirrors the Owner string) ────────────
INSERT INTO TaskOwners (TaskId, UserId)
SELECT t.Id, u.Id
FROM Tasks t
JOIN #U u ON u.DisplayName = t.Owner
WHERE t.CreatedBy = 'loadtest-seed'
  AND NOT EXISTS (SELECT 1 FROM TaskOwners o WHERE o.TaskId = t.Id AND o.UserId = u.Id);

PRINT 'Seed complete.';
SELECT
    (SELECT COUNT(*) FROM AppUsers WHERE Username LIKE 'lt\_user\_%' ESCAPE '\')  AS SeededUsers,
    (SELECT COUNT(*) FROM Tasks WHERE CreatedBy = 'loadtest-seed')                 AS SeededTasks,
    (SELECT COUNT(*) FROM TaskOwners o JOIN Tasks t ON t.Id = o.TaskId WHERE t.CreatedBy = 'loadtest-seed') AS SeededTaskOwners,
    @TeamId AS LoadTestTeamId;

/*
  ── CLEANUP (run to remove everything this script created) ──────────────────
  DELETE o FROM TaskOwners o JOIN Tasks t ON t.Id = o.TaskId WHERE t.CreatedBy = 'loadtest-seed';
  DELETE FROM Tasks WHERE CreatedBy = 'loadtest-seed';
  DELETE m FROM TeamMembers m JOIN AppUsers u ON u.Id = m.UserId WHERE u.Username LIKE 'lt\_user\_%' ESCAPE '\';
  DELETE FROM AppUsers WHERE Username LIKE 'lt\_user\_%' ESCAPE '\';
  DELETE FROM Teams WHERE Name = 'LOADTEST Team';
*/
