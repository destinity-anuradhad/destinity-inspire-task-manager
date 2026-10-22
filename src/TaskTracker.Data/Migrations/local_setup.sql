-- ============================================================
-- local_setup.sql  — Fresh local DB setup for Task Tracker
-- Creates the full current schema and marks all 22 migrations
-- as applied so dotnet ef database update is a no-op.
--
-- Usage (run from the repo root):
--   sqlcmd -S . -E -d DestiniyInspire_TaskManager -i src\TaskTracker.Data\Migrations\local_setup.sql
--
-- Create the database first if it doesn't already exist:
--   sqlcmd -S . -E -Q "IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name='DestiniyInspire_TaskManager') CREATE DATABASE [DestiniyInspire_TaskManager]"
-- ============================================================

-- Migration history table
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId]    nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32)  NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

-- ─── Teams ────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Teams')
BEGIN
    CREATE TABLE [Teams] (
        [Id]          INT           NOT NULL IDENTITY(1,1),
        [Name]        NVARCHAR(450) NOT NULL DEFAULT '',
        [Description] NVARCHAR(MAX) NULL,
        [Color]       NVARCHAR(MAX) NOT NULL DEFAULT '#094f70',
        [IsActive]    BIT           NOT NULL DEFAULT 1,
        [CreatedAt]   DATETIME2     NOT NULL DEFAULT GETDATE(),
        [CreatedBy]   NVARCHAR(MAX) NULL,
        CONSTRAINT [PK_Teams] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_Teams_Name] ON [Teams] ([Name]);
END;
GO

-- ─── TeamMembers ──────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'TeamMembers')
BEGIN
    CREATE TABLE [TeamMembers] (
        [Id]       INT           NOT NULL IDENTITY(1,1),
        [TeamId]   INT           NOT NULL,
        [UserId]   INT           NOT NULL,
        [TeamRole] NVARCHAR(MAX) NOT NULL DEFAULT 'Member',
        [JoinedAt] DATETIME2     NOT NULL DEFAULT GETDATE(),
        CONSTRAINT [PK_TeamMembers] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_TeamMembers_TeamId] ON [TeamMembers] ([TeamId]);
    CREATE INDEX [IX_TeamMembers_UserId] ON [TeamMembers] ([UserId]);
    CREATE UNIQUE INDEX [IX_TeamMembers_TeamId_UserId] ON [TeamMembers] ([TeamId], [UserId]);
END;
GO

-- ─── AppUsers ─────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AppUsers')
BEGIN
    CREATE TABLE [AppUsers] (
        [Id]                 INT           NOT NULL IDENTITY(1,1),
        [Username]           NVARCHAR(450) NOT NULL,
        [PasswordHash]       NVARCHAR(MAX) NOT NULL DEFAULT '',
        [Role]               NVARCHAR(MAX) NOT NULL DEFAULT 'Member',
        [DisplayName]        NVARCHAR(450) NOT NULL DEFAULT '',
        [ProfileImagePath]   NVARCHAR(MAX) NULL,
        [Email]              NVARCHAR(MAX) NULL,
        [NotifyOnAssigned]   BIT           NOT NULL DEFAULT 1,
        [NotifyOnComment]    BIT           NOT NULL DEFAULT 1,
        [NotifyOnBlocked]    BIT           NOT NULL DEFAULT 1,
        [CreatedAt]          DATETIME2     NOT NULL DEFAULT GETDATE(),
        [Color]              NVARCHAR(MAX) NULL,
        [JobTitle]           NVARCHAR(200) NULL,
        [Department]         NVARCHAR(200) NULL,
        [DailyCapacityHours] DECIMAL(5,2)  NULL,
        [WorkingDays]        NVARCHAR(50)  NULL,
        [Status]             NVARCHAR(50)  NOT NULL DEFAULT 'Active',
        [RequestedTeamId]    INT           NULL,
        [MustChangePassword] BIT           NOT NULL DEFAULT 0,
        CONSTRAINT [PK_AppUsers] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_AppUsers_DisplayName] ON [AppUsers] ([DisplayName]);
    CREATE UNIQUE INDEX [IX_AppUsers_Username] ON [AppUsers] ([Username]);
END;
GO

-- ─── Notifications ────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Notifications')
BEGIN
    CREATE TABLE [Notifications] (
        [Id]        INT           NOT NULL IDENTITY(1,1),
        [UserId]    INT           NOT NULL,
        [TaskId]    INT           NULL,
        [Message]   NVARCHAR(MAX) NOT NULL DEFAULT '',
        [IsRead]    BIT           NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2     NOT NULL DEFAULT GETDATE(),
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_Notifications_UserId]         ON [Notifications] ([UserId]);
    CREATE INDEX [IX_Notifications_UserId_IsRead]  ON [Notifications] ([UserId], [IsRead]);
END;
GO

-- ─── Owners (lookup table for assignable owners) ──────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Owners')
BEGIN
    CREATE TABLE [Owners] (
        [Id]   INT           NOT NULL IDENTITY(1,1),
        [Name] NVARCHAR(MAX) NOT NULL DEFAULT '',
        CONSTRAINT [PK_Owners] PRIMARY KEY ([Id])
    );
END;
GO

-- ─── Projects ────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Projects')
BEGIN
    CREATE TABLE [Projects] (
        [Id]    INT           NOT NULL IDENTITY(1,1),
        [Name]  NVARCHAR(MAX) NOT NULL DEFAULT '',
        [Color] NVARCHAR(MAX) NULL,
        CONSTRAINT [PK_Projects] PRIMARY KEY ([Id])
    );
END;
GO

-- ─── Properties ──────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Properties')
BEGIN
    CREATE TABLE [Properties] (
        [Id]    INT           NOT NULL IDENTITY(1,1),
        [Name]  NVARCHAR(MAX) NOT NULL DEFAULT '',
        [Color] NVARCHAR(MAX) NULL,
        CONSTRAINT [PK_Properties] PRIMARY KEY ([Id])
    );
END;
GO

-- ─── WorkStates ──────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'WorkStates')
BEGIN
    CREATE TABLE [WorkStates] (
        [Id]        INT           NOT NULL IDENTITY(1,1),
        [Name]      NVARCHAR(MAX) NOT NULL DEFAULT '',
        [Color]     NVARCHAR(MAX) NOT NULL DEFAULT '#094f70',
        [SortOrder] INT           NOT NULL DEFAULT 0,
        [WipLimit]  INT           NULL,
        CONSTRAINT [PK_WorkStates] PRIMARY KEY ([Id])
    );
END;
GO

-- ─── Sprints ─────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Sprints')
BEGIN
    CREATE TABLE [Sprints] (
        [Id]        INT           NOT NULL IDENTITY(1,1),
        [Name]      NVARCHAR(MAX) NOT NULL DEFAULT '',
        [Goal]      NVARCHAR(MAX) NULL,
        [StartDate] DATETIME2     NULL,
        [EndDate]   DATETIME2     NULL,
        [IsActive]  BIT           NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2     NOT NULL DEFAULT GETDATE(),
        [CreatedBy] NVARCHAR(MAX) NULL,
        [TeamId]    INT           NULL,
        CONSTRAINT [PK_Sprints] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_Sprints_TeamId] ON [Sprints] ([TeamId]);
END;
GO

-- ─── Tasks ───────────────────────────────────────────────────
-- Note: Owner column intentionally absent — dropped by DropTasksOwner migration.
-- Owner is now stored in TaskOwners junction table.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Tasks')
BEGIN
    CREATE TABLE [Tasks] (
        [Id]                 INT           NOT NULL IDENTITY(1,1),
        [Goal]               NVARCHAR(MAX) NULL,
        [Project]            NVARCHAR(450) NULL,
        [Property]           NVARCHAR(450) NULL,
        [TargetDate]         DATETIME2     NULL,
        [Priority]           NVARCHAR(450) NULL,
        [Status]             NVARCHAR(450) NULL,
        [StartDate]          DATETIME2     NULL,
        [EndDate]            DATETIME2     NULL,
        [ActualStartDate]    DATETIME2     NULL,
        [ActualEndDate]      DATETIME2     NULL,
        [RelatedFiles]       NVARCHAR(MAX) NULL,
        [Notes]              NVARCHAR(MAX) NULL,
        [CreatedAt]          DATETIME2     NOT NULL DEFAULT GETDATE(),
        [CreatedBy]          NVARCHAR(450) NULL,
        [Weight]             DECIMAL(18,2) NULL,
        [ModuleId]           INT           NULL,
        [ClientId]           INT           NULL,
        [ItemType]           NVARCHAR(450) NULL,
        [ParentId]           INT           NULL,
        [SprintId]           INT           NULL,
        [ExternalId]         INT           NULL,
        [Reporter]           NVARCHAR(MAX) NULL,
        [Labels]             NVARCHAR(MAX) NULL,
        [AcceptanceCriteria] NVARCHAR(MAX) NULL,
        [Swimlane]           NVARCHAR(50)  NULL,
        [BacklogOrder]       INT           NULL,
        [IsDeleted]          BIT           NOT NULL DEFAULT 0,
        [DeletedAt]          DATETIME2     NULL,
        [DeletedBy]          NVARCHAR(MAX) NULL,
        [TeamId]             INT           NULL,
        [RowVersion]         ROWVERSION    NOT NULL,
        CONSTRAINT [PK_Tasks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Tasks_Tasks_ParentId]
            FOREIGN KEY ([ParentId]) REFERENCES [Tasks] ([Id]) ON DELETE NO ACTION
    );
    CREATE INDEX [IX_Tasks_CreatedAt]              ON [Tasks] ([CreatedAt]);
    CREATE INDEX [IX_Tasks_CreatedBy]              ON [Tasks] ([CreatedBy]);
    CREATE INDEX [IX_Tasks_ItemType]               ON [Tasks] ([ItemType]);
    CREATE INDEX [IX_Tasks_ParentId]               ON [Tasks] ([ParentId]);
    CREATE INDEX [IX_Tasks_Priority]               ON [Tasks] ([Priority]);
    CREATE INDEX [IX_Tasks_Project]                ON [Tasks] ([Project]);
    CREATE INDEX [IX_Tasks_Property]               ON [Tasks] ([Property]);
    CREATE INDEX [IX_Tasks_SprintId]               ON [Tasks] ([SprintId]);
    CREATE INDEX [IX_Tasks_StartDate]              ON [Tasks] ([StartDate]);
    CREATE INDEX [IX_Tasks_Status]                 ON [Tasks] ([Status]);
    CREATE INDEX [IX_Tasks_TargetDate]             ON [Tasks] ([TargetDate]);
    CREATE INDEX [IX_Tasks_TeamId]                 ON [Tasks] ([TeamId]);
    CREATE INDEX [IX_Tasks_TeamId_IsDeleted_Status] ON [Tasks] ([TeamId], [IsDeleted], [Status]);
END;
GO

-- ─── TaskOwners (junction) ────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'TaskOwners')
BEGIN
    CREATE TABLE [TaskOwners] (
        [TaskId] INT NOT NULL,
        [UserId] INT NOT NULL,
        CONSTRAINT [PK_TaskOwners] PRIMARY KEY ([TaskId], [UserId]),
        CONSTRAINT [FK_TaskOwners_Tasks_TaskId]
            FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TaskOwners_AppUsers_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION
    );
    CREATE INDEX [IX_TaskOwners_UserId] ON [TaskOwners] ([UserId]);
END;
GO

-- ─── TaskLinks ───────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'TaskLinks')
BEGIN
    CREATE TABLE [TaskLinks] (
        [Id]         INT           NOT NULL IDENTITY(1,1),
        [FromTaskId] INT           NOT NULL,
        [ToTaskId]   INT           NOT NULL,
        [LinkType]   NVARCHAR(MAX) NOT NULL DEFAULT '',
        [CreatedAt]  DATETIME2     NOT NULL DEFAULT GETDATE(),
        [CreatedBy]  NVARCHAR(MAX) NULL,
        CONSTRAINT [PK_TaskLinks] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_TaskLinks_FromTaskId] ON [TaskLinks] ([FromTaskId]);
    CREATE INDEX [IX_TaskLinks_ToTaskId]   ON [TaskLinks] ([ToTaskId]);
END;
GO

-- ─── TaskAttachments ─────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'TaskAttachments')
BEGIN
    CREATE TABLE [TaskAttachments] (
        [Id]          INT           NOT NULL IDENTITY(1,1),
        [TaskId]      INT           NOT NULL,
        [FileName]    NVARCHAR(MAX) NOT NULL DEFAULT '',
        [StoredName]  NVARCHAR(MAX) NOT NULL DEFAULT '',
        [FileSize]    BIGINT        NOT NULL DEFAULT 0,
        [ContentType] NVARCHAR(MAX) NULL,
        [UploadedBy]  NVARCHAR(MAX) NULL,
        [UploadedAt]  DATETIME2     NOT NULL DEFAULT GETDATE(),
        CONSTRAINT [PK_TaskAttachments] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_TaskAttachments_TaskId] ON [TaskAttachments] ([TaskId]);
END;
GO

-- ─── TaskWatchers ────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'TaskWatchers')
BEGIN
    CREATE TABLE [TaskWatchers] (
        [Id]        INT       NOT NULL IDENTITY(1,1),
        [TaskId]    INT       NOT NULL,
        [UserId]    INT       NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT [PK_TaskWatchers] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_TaskWatchers_TaskId] ON [TaskWatchers] ([TaskId]);
    CREATE UNIQUE INDEX [IX_TaskWatchers_TaskId_UserId] ON [TaskWatchers] ([TaskId], [UserId]);
END;
GO

-- ─── ActivityLog ─────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'ActivityLog')
BEGIN
    CREATE TABLE [ActivityLog] (
        [Id]        INT           NOT NULL IDENTITY(1,1),
        [Action]    NVARCHAR(MAX) NOT NULL DEFAULT '',
        [Category]  NVARCHAR(50)  NOT NULL DEFAULT 'Task',
        [ChangedAt] DATETIME2     NOT NULL DEFAULT GETDATE(),
        [ChangedBy] NVARCHAR(MAX) NOT NULL DEFAULT '',
        [Details]   NVARCHAR(MAX) NOT NULL DEFAULT '',
        [IpAddress] NVARCHAR(45)  NULL,
        [UserAgent] NVARCHAR(512) NULL,
        [Project]   NVARCHAR(MAX) NOT NULL DEFAULT '',
        [TaskGoal]  NVARCHAR(MAX) NOT NULL DEFAULT '',
        [TaskId]    INT           NULL,
        [TeamId]    INT           NULL,
        CONSTRAINT [PK_ActivityLog] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_ActivityLog_Category]  ON [ActivityLog] ([Category]);
    CREATE INDEX [IX_ActivityLog_ChangedAt] ON [ActivityLog] ([ChangedAt]);
    CREATE INDEX [IX_ActivityLog_TaskId]    ON [ActivityLog] ([TaskId]);
    CREATE INDEX [IX_ActivityLog_TeamId]    ON [ActivityLog] ([TeamId]);
END;
GO

-- ─── EmailLogs ───────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'EmailLogs')
BEGIN
    CREATE TABLE [EmailLogs] (
        [Id]           INT           NOT NULL IDENTITY(1,1),
        [ToEmail]      NVARCHAR(256) NOT NULL DEFAULT '',
        [ToName]       NVARCHAR(200) NOT NULL DEFAULT '',
        [Subject]      NVARCHAR(500) NOT NULL DEFAULT '',
        [HtmlBody]     NVARCHAR(MAX) NOT NULL DEFAULT '',
        [SentAt]       DATETIME2     NOT NULL DEFAULT GETDATE(),
        [IsSuccess]    BIT           NOT NULL DEFAULT 0,
        [ErrorMessage] NVARCHAR(MAX) NULL,
        CONSTRAINT [PK_EmailLogs] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_EmailLogs_SentAt]    ON [EmailLogs] ([SentAt]);
    CREATE INDEX [IX_EmailLogs_ToEmail]   ON [EmailLogs] ([ToEmail]);
    CREATE INDEX [IX_EmailLogs_IsSuccess] ON [EmailLogs] ([IsSuccess]);
END;
GO

-- ─── SavedQueries ────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'SavedQueries')
BEGIN
    CREATE TABLE [SavedQueries] (
        [Id]          INT           NOT NULL IDENTITY(1,1),
        [Name]        NVARCHAR(200) NOT NULL DEFAULT '',
        [Description] NVARCHAR(MAX) NULL,
        [Filters]     NVARCHAR(MAX) NOT NULL DEFAULT '{}',
        [IsShared]    BIT           NOT NULL DEFAULT 0,
        [CreatedBy]   NVARCHAR(200) NULL,
        [CreatedAt]   DATETIME2     NOT NULL DEFAULT GETDATE(),
        [TeamId]      INT           NULL,
        CONSTRAINT [PK_SavedQueries] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_SavedQueries_CreatedBy] ON [SavedQueries] ([CreatedBy]);
END;
GO

-- ─── SprintCapacities ────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'SprintCapacities')
BEGIN
    CREATE TABLE [SprintCapacities] (
        [Id]            INT           NOT NULL IDENTITY(1,1),
        [SprintId]      INT           NOT NULL,
        [OwnerName]     NVARCHAR(200) NOT NULL DEFAULT '',
        [CapacityHours] DECIMAL(18,2) NOT NULL DEFAULT 0,
        [UserId]        INT           NULL,
        CONSTRAINT [PK_SprintCapacities] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_SprintCapacities_SprintId] ON [SprintCapacities] ([SprintId]);
    CREATE UNIQUE INDEX [IX_SprintCapacities_Sprint_Owner] ON [SprintCapacities] ([SprintId], [OwnerName]);
END;
GO

-- ─── Mark all migrations as applied ──────────────────────────
-- This prevents dotnet ef database update from trying to re-run
-- any migrations against this already-initialised database.
INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
SELECT v.[MigrationId], N'10.0.9'
FROM (VALUES
    (N'20260630094913_InitialCreate'),
    (N'20260630094959_AddUserEmailNotifications'),
    (N'20260630110000_AddLabelsAndAcceptanceCriteria'),
    (N'20260701120000_AddTaskAttachments'),
    (N'20260701130000_AddWipLimitToWorkStates'),
    (N'20260701130001_AddSwimlaneToTasks'),
    (N'20260701140000_AddBacklogOrder'),
    (N'20260701150000_AddSavedQueries'),
    (N'20260701160000_AddSoftDelete'),
    (N'20260701170000_AddSprintCapacities'),
    (N'20260702170000_AddTaskWatchers'),
    (N'20260706000001_AddUserProfileExtensions'),
    (N'20260707100000_AddSavedQueryTeamId'),
    (N'20260707180000_AddEmailLog'),
    (N'20260707190000_AddActivityLogSystemFields'),
    (N'20260707200000_AddActivityLogUserAgent'),
    (N'20260708100000_AddTeamIdToTasksAndSprints'),
    (N'20260708110000_ConvertTaskDatesToDateTime'),
    (N'20260708120000_AddRowVersionToTasks'),
    (N'20260710000000_AddTaskOwners'),
    (N'20260728000001_NormalizeDb'),
    (N'20260728000002_DropTasksOwner'),
    (N'20260728000003_MigrateTaskOwnerId'),
    (N'20260728000004_RenameLeadToTeamLead')
) AS v([MigrationId])
WHERE NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = v.[MigrationId]
);
GO
