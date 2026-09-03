IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260630094913_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260630094913_InitialCreate', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260630094959_AddUserEmailNotifications'
)
BEGIN

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='Email')
                        ALTER TABLE [AppUsers] ADD [Email] NVARCHAR(200) NULL;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='NotifyOnComment')
                        ALTER TABLE [AppUsers] ADD [NotifyOnComment] BIT NOT NULL DEFAULT 1;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='NotifyOnBlocked')
                        ALTER TABLE [AppUsers] ADD [NotifyOnBlocked] BIT NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260630094959_AddUserEmailNotifications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260630094959_AddUserEmailNotifications', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260630110000_AddLabelsAndAcceptanceCriteria'
)
BEGIN

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Tasks' AND COLUMN_NAME='Reporter')
                        ALTER TABLE [Tasks] ADD [Reporter] NVARCHAR(MAX) NULL;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Tasks' AND COLUMN_NAME='Labels')
                        ALTER TABLE [Tasks] ADD [Labels] NVARCHAR(MAX) NULL;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Tasks' AND COLUMN_NAME='AcceptanceCriteria')
                        ALTER TABLE [Tasks] ADD [AcceptanceCriteria] NVARCHAR(MAX) NULL;
                
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260630110000_AddLabelsAndAcceptanceCriteria'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260630110000_AddLabelsAndAcceptanceCriteria', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701120000_AddTaskAttachments'
)
BEGIN

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='TaskAttachments')
                    BEGIN
                        CREATE TABLE [TaskAttachments] (
                            [Id]          INT IDENTITY(1,1) NOT NULL,
                            [TaskId]      INT NOT NULL,
                            [FileName]    NVARCHAR(MAX) NOT NULL DEFAULT '',
                            [StoredName]  NVARCHAR(MAX) NOT NULL DEFAULT '',
                            [FileSize]    BIGINT NOT NULL DEFAULT 0,
                            [ContentType] NVARCHAR(MAX) NULL,
                            [UploadedBy]  NVARCHAR(MAX) NULL,
                            [UploadedAt]  DATETIME2 NOT NULL DEFAULT GETDATE(),
                            CONSTRAINT [PK_TaskAttachments] PRIMARY KEY ([Id])
                        );
                        CREATE INDEX [IX_TaskAttachments_TaskId] ON [TaskAttachments] ([TaskId]);
                    END
                
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701120000_AddTaskAttachments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260701120000_AddTaskAttachments', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701130000_AddWipLimitToWorkStates'
)
BEGIN

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WorkStates' AND COLUMN_NAME='WipLimit')
                        ALTER TABLE [WorkStates] ADD [WipLimit] INT NULL;
                
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701130000_AddWipLimitToWorkStates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260701130000_AddWipLimitToWorkStates', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701130001_AddSwimlaneToTasks'
)
BEGIN

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Tasks' AND COLUMN_NAME='Swimlane')
                        ALTER TABLE [Tasks] ADD [Swimlane] NVARCHAR(50) NULL;
                
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701130001_AddSwimlaneToTasks'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260701130001_AddSwimlaneToTasks', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072319_AddItemTypesToWorkStates'
)
BEGIN
    ALTER TABLE [WorkStates] ADD [ItemTypes] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072319_AddItemTypesToWorkStates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260810072319_AddItemTypesToWorkStates', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810110637_AddActualHoursToTasks'
)
BEGIN
    ALTER TABLE [Tasks] ADD [ActualHours] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810110637_AddActualHoursToTasks'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260810110637_AddActualHoursToTasks', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811000001_AddSprintHoursLog'
)
BEGIN
    CREATE TABLE [SprintHoursLogs] (
        [Id] int NOT NULL IDENTITY,
        [SprintId] int NOT NULL,
        [TaskId] int NOT NULL,
        [Hours] decimal(18,2) NOT NULL,
        [LoggedBy] nvarchar(max) NOT NULL,
        [LoggedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SprintHoursLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811000001_AddSprintHoursLog'
)
BEGIN
    CREATE INDEX [IX_SprintHoursLogs_SprintId] ON [SprintHoursLogs] ([SprintId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811000001_AddSprintHoursLog'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SprintHoursLogs_SprintId_TaskId] ON [SprintHoursLogs] ([SprintId], [TaskId]) WHERE [SprintId] IS NOT NULL AND [TaskId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811000001_AddSprintHoursLog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260811000001_AddSprintHoursLog', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812115920_RemoveTaskOwners'
)
BEGIN

                    IF NOT EXISTS (
                        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_NAME = 'Tasks' AND COLUMN_NAME = 'Owner'
                    )
                        ALTER TABLE [Tasks] ADD [Owner] nvarchar(max) NULL;
                
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812115920_RemoveTaskOwners'
)
BEGIN

                    IF OBJECT_ID(N'[TaskOwners]', N'U') IS NOT NULL
                       AND EXISTS (SELECT 1 FROM [TaskOwners])
                        THROW 51002,
                            'Migration stopped: TaskOwners contains assignment data and cannot be dropped safely.',
                            1;

                    IF OBJECT_ID(N'[TaskOwners]', N'U') IS NOT NULL
                        DROP TABLE [TaskOwners];
                
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260812115920_RemoveTaskOwners'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260812115920_RemoveTaskOwners', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    CREATE TABLE [Modules] (
        [Id] int NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        CONSTRAINT [PK_Modules] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    CREATE TABLE [Clients] (
        [Id] int NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        CONSTRAINT [PK_Clients] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN

                    INSERT INTO Modules (Id, Name)
                    SELECT DISTINCT ModuleId, ''
                    FROM Tasks WHERE ModuleId IS NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN

                    INSERT INTO Clients (Id, Name)
                    SELECT DISTINCT ClientId, ''
                    FROM Tasks WHERE ClientId IS NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN

                    UPDATE sc
                    SET sc.UserId = u.Id
                    FROM SprintCapacities sc
                    JOIN AppUsers u ON u.DisplayName = sc.OwnerName;

                    IF EXISTS (
                        SELECT 1
                        FROM SprintCapacities
                        WHERE UserId IS NULL OR UserId = 0
                    )
                        THROW 51003,
                            'Migration stopped: one or more SprintCapacities could not be matched to an AppUser.',
                            1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SprintCapacities]') AND [c].[name] = N'UserId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [SprintCapacities] DROP CONSTRAINT ' + @var + ';');
    EXEC(N'UPDATE [SprintCapacities] SET [UserId] = 0 WHERE [UserId] IS NULL');
    ALTER TABLE [SprintCapacities] ALTER COLUMN [UserId] int NOT NULL;
    ALTER TABLE [SprintCapacities] ADD DEFAULT 0 FOR [UserId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    CREATE TABLE [TaskOwners] (
        [TaskId] int NOT NULL,
        [UserId] int NOT NULL,
        CONSTRAINT [PK_TaskOwners] PRIMARY KEY ([TaskId], [UserId]),
        CONSTRAINT [FK_TaskOwners_AppUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TaskOwners_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN

                    INSERT INTO TaskOwners (TaskId, UserId)
                    SELECT DISTINCT t.Id, u.Id
                    FROM Tasks t
                    CROSS APPLY STRING_SPLIT(t.Owner, ',') sv
                    JOIN AppUsers u ON u.DisplayName = LTRIM(RTRIM(sv.value))
                    WHERE t.Owner IS NOT NULL
                      AND LTRIM(RTRIM(sv.value)) <> '';

                    IF EXISTS (
                        SELECT 1
                        FROM Tasks t
                        CROSS APPLY STRING_SPLIT(t.Owner, ',') sv
                        WHERE t.Owner IS NOT NULL
                          AND LTRIM(RTRIM(sv.value)) <> ''
                          AND NOT EXISTS (
                              SELECT 1
                              FROM AppUsers u
                              WHERE u.DisplayName = LTRIM(RTRIM(sv.value))
                          )
                    )
                        THROW 51004,
                            'Migration stopped: one or more Tasks.Owner values could not be matched to an AppUser.',
                            1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SprintHoursLogs')
                    BEGIN
                        CREATE TABLE SprintHoursLogs (
                            Id       INT           NOT NULL IDENTITY(1,1),
                            SprintId INT           NOT NULL,
                            TaskId   INT           NOT NULL,
                            Hours    DECIMAL(18,2) NOT NULL,
                            LoggedBy NVARCHAR(MAX) NOT NULL DEFAULT '',
                            LoggedAt DATETIME2     NOT NULL,
                            CONSTRAINT PK_SprintHoursLogs PRIMARY KEY (Id)
                        );
                        CREATE INDEX IX_SprintHoursLogs_SprintId
                            ON SprintHoursLogs(SprintId);
                        CREATE UNIQUE INDEX IX_SprintHoursLogs_SprintId_TaskId
                            ON SprintHoursLogs(SprintId, TaskId);
                    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    CREATE INDEX [IX_Tasks_ClientId] ON [Tasks] ([ClientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    CREATE INDEX [IX_Tasks_ModuleId] ON [Tasks] ([ModuleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SprintCapacities_SprintId_UserId] ON [SprintCapacities] ([SprintId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    CREATE INDEX [IX_SprintCapacities_UserId] ON [SprintCapacities] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    CREATE INDEX [IX_SavedQueries_TeamId] ON [SavedQueries] ([TeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    CREATE INDEX [IX_AppUsers_RequestedTeamId] ON [AppUsers] ([RequestedTeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    CREATE INDEX [IX_TaskOwners_UserId] ON [TaskOwners] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    ALTER TABLE [ActivityLog] ADD CONSTRAINT [FK_ActivityLog_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    ALTER TABLE [AppUsers] ADD CONSTRAINT [FK_AppUsers_Teams_RequestedTeamId] FOREIGN KEY ([RequestedTeamId]) REFERENCES [Teams] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    ALTER TABLE [SavedQueries] ADD CONSTRAINT [FK_SavedQueries_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    ALTER TABLE [SprintCapacities] ADD CONSTRAINT [FK_SprintCapacities_AppUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    ALTER TABLE [Sprints] ADD CONSTRAINT [FK_Sprints_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    ALTER TABLE [Tasks] ADD CONSTRAINT [FK_Tasks_Clients_ClientId] FOREIGN KEY ([ClientId]) REFERENCES [Clients] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    ALTER TABLE [Tasks] ADD CONSTRAINT [FK_Tasks_Modules_ModuleId] FOREIGN KEY ([ModuleId]) REFERENCES [Modules] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    ALTER TABLE [Tasks] ADD CONSTRAINT [FK_Tasks_Sprints_SprintId] FOREIGN KEY ([SprintId]) REFERENCES [Sprints] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    ALTER TABLE [Tasks] ADD CONSTRAINT [FK_Tasks_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    ALTER TABLE AppUsers WITH NOCHECK ADD CONSTRAINT CK_AppUsers_Role CHECK (Role IN ('Admin', 'Member'));
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    ALTER TABLE TeamMembers WITH NOCHECK ADD CONSTRAINT CK_TeamMembers_TeamRole CHECK (TeamRole IN ('TeamAdmin', 'TeamLead', 'Member'));
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817032043_NormalizationPhase1_TaskOwners'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817032043_NormalizationPhase1_TaskOwners', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831000000_RepairTeamLeadConstraint'
)
BEGIN

                IF OBJECT_ID(N'[TeamMembers]', N'U') IS NOT NULL
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM sys.check_constraints
                        WHERE [parent_object_id] = OBJECT_ID(N'[TeamMembers]')
                          AND [name] = N'CK_TeamMembers_TeamRole'
                    )
                        ALTER TABLE [TeamMembers] DROP CONSTRAINT [CK_TeamMembers_TeamRole];

                    UPDATE [TeamMembers]
                    SET [TeamRole] = N'TeamLead'
                    WHERE [TeamRole] = N'Lead';

                    IF EXISTS (
                        SELECT 1
                        FROM [TeamMembers]
                        WHERE [TeamRole] NOT IN (N'TeamAdmin', N'TeamLead', N'Member')
                    )
                        THROW 51005,
                            'Migration stopped: TeamMembers contains an unsupported TeamRole.',
                            1;

                    ALTER TABLE [TeamMembers] WITH CHECK
                        ADD CONSTRAINT [CK_TeamMembers_TeamRole]
                        CHECK ([TeamRole] IN (N'TeamAdmin', N'TeamLead', N'Member'));
                    ALTER TABLE [TeamMembers]
                        CHECK CONSTRAINT [CK_TeamMembers_TeamRole];
                END
            
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831000000_RepairTeamLeadConstraint'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831000000_RepairTeamLeadConstraint', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831000100_BackfillVerifiedLegacyTaskOwners'
)
BEGIN

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
            
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831000100_BackfillVerifiedLegacyTaskOwners'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831000100_BackfillVerifiedLegacyTaskOwners', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901000000_AddUserSessionVersion'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [SessionVersion] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901000000_AddUserSessionVersion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901000000_AddUserSessionVersion', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042508_AddVersionedWorkflowModel'
)
BEGIN
    ALTER TABLE [Tasks] ADD [WorkflowDefinitionId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042508_AddVersionedWorkflowModel'
)
BEGIN
    CREATE TABLE [WorkflowDefinitions] (
        [Id] int NOT NULL IDENTITY,
        [TeamId] int NULL,
        [ItemType] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Version] int NOT NULL,
        [IsPublished] bit NOT NULL,
        [IsDefault] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [PublishedAt] datetime2 NULL,
        CONSTRAINT [PK_WorkflowDefinitions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkflowDefinitions_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042508_AddVersionedWorkflowModel'
)
BEGIN
    CREATE TABLE [WorkflowStates] (
        [Id] int NOT NULL IDENTITY,
        [WorkflowDefinitionId] int NOT NULL,
        [Key] nvarchar(100) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Category] nvarchar(50) NOT NULL,
        [SortOrder] int NOT NULL,
        [IsInitial] bit NOT NULL,
        [IsTerminal] bit NOT NULL,
        [RequiresOwner] bit NOT NULL,
        [RequiresActualHours] bit NOT NULL,
        CONSTRAINT [PK_WorkflowStates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkflowStates_WorkflowDefinitions_WorkflowDefinitionId] FOREIGN KEY ([WorkflowDefinitionId]) REFERENCES [WorkflowDefinitions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042508_AddVersionedWorkflowModel'
)
BEGIN
    CREATE TABLE [WorkflowTransitions] (
        [Id] int NOT NULL IDENTITY,
        [WorkflowDefinitionId] int NOT NULL,
        [FromStateId] int NOT NULL,
        [ToStateId] int NOT NULL,
        [MemberCanTransition] bit NOT NULL,
        CONSTRAINT [PK_WorkflowTransitions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkflowTransitions_WorkflowDefinitions_WorkflowDefinitionId] FOREIGN KEY ([WorkflowDefinitionId]) REFERENCES [WorkflowDefinitions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042508_AddVersionedWorkflowModel'
)
BEGIN
    CREATE INDEX [IX_Tasks_WorkflowDefinitionId] ON [Tasks] ([WorkflowDefinitionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042508_AddVersionedWorkflowModel'
)
BEGIN
    CREATE INDEX [IX_WorkflowDefinitions_TeamId_ItemType_IsPublished] ON [WorkflowDefinitions] ([TeamId], [ItemType], [IsPublished]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042508_AddVersionedWorkflowModel'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_WorkflowDefinitions_TeamId_ItemType_Version] ON [WorkflowDefinitions] ([TeamId], [ItemType], [Version]) WHERE [TeamId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042508_AddVersionedWorkflowModel'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkflowStates_WorkflowDefinitionId_Key] ON [WorkflowStates] ([WorkflowDefinitionId], [Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042508_AddVersionedWorkflowModel'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkflowStates_WorkflowDefinitionId_Name] ON [WorkflowStates] ([WorkflowDefinitionId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042508_AddVersionedWorkflowModel'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkflowTransitions_WorkflowDefinitionId_FromStateId_ToStateId] ON [WorkflowTransitions] ([WorkflowDefinitionId], [FromStateId], [ToStateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042508_AddVersionedWorkflowModel'
)
BEGIN
    ALTER TABLE [Tasks] ADD CONSTRAINT [FK_Tasks_WorkflowDefinitions_WorkflowDefinitionId] FOREIGN KEY ([WorkflowDefinitionId]) REFERENCES [WorkflowDefinitions] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042508_AddVersionedWorkflowModel'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901042508_AddVersionedWorkflowModel', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901082341_AddImmutableSprintHistory'
)
BEGIN
    ALTER TABLE [Sprints] ADD [CompletedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901082341_AddImmutableSprintHistory'
)
BEGIN
    ALTER TABLE [Sprints] ADD [CompletedBy] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901082341_AddImmutableSprintHistory'
)
BEGIN
    CREATE TABLE [SprintCompletionSnapshots] (
        [Id] int NOT NULL IDENTITY,
        [SprintId] int NOT NULL,
        [CompletedAt] datetime2 NOT NULL,
        [CompletedBy] nvarchar(max) NULL,
        [CommittedCount] int NOT NULL,
        [CompletedCount] int NOT NULL,
        [CarriedForwardCount] int NOT NULL,
        [CommittedEstimate] decimal(18,2) NOT NULL,
        [CompletedEstimate] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_SprintCompletionSnapshots] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SprintCompletionSnapshots_Sprints_SprintId] FOREIGN KEY ([SprintId]) REFERENCES [Sprints] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901082341_AddImmutableSprintHistory'
)
BEGIN
    CREATE TABLE [SprintWorkItemEvents] (
        [Id] bigint NOT NULL IDENTITY,
        [TaskId] int NOT NULL,
        [FromSprintId] int NULL,
        [ToSprintId] int NULL,
        [EventType] nvarchar(50) NOT NULL,
        [Status] nvarchar(max) NULL,
        [Estimate] decimal(18,2) NULL,
        [OccurredAt] datetime2 NOT NULL,
        [OccurredBy] nvarchar(max) NULL,
        CONSTRAINT [PK_SprintWorkItemEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SprintWorkItemEvents_Sprints_FromSprintId] FOREIGN KEY ([FromSprintId]) REFERENCES [Sprints] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SprintWorkItemEvents_Sprints_ToSprintId] FOREIGN KEY ([ToSprintId]) REFERENCES [Sprints] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SprintWorkItemEvents_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901082341_AddImmutableSprintHistory'
)
BEGIN
    CREATE INDEX [IX_Sprints_CompletedAt] ON [Sprints] ([CompletedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901082341_AddImmutableSprintHistory'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SprintCompletionSnapshots_SprintId] ON [SprintCompletionSnapshots] ([SprintId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901082341_AddImmutableSprintHistory'
)
BEGIN
    CREATE INDEX [IX_SprintWorkItemEvents_FromSprintId_OccurredAt] ON [SprintWorkItemEvents] ([FromSprintId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901082341_AddImmutableSprintHistory'
)
BEGIN
    CREATE INDEX [IX_SprintWorkItemEvents_TaskId_OccurredAt] ON [SprintWorkItemEvents] ([TaskId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901082341_AddImmutableSprintHistory'
)
BEGIN
    CREATE INDEX [IX_SprintWorkItemEvents_ToSprintId_OccurredAt] ON [SprintWorkItemEvents] ([ToSprintId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901082341_AddImmutableSprintHistory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901082341_AddImmutableSprintHistory', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901083457_AddAppendOnlyTimeEntries'
)
BEGIN
    ALTER TABLE [Tasks] ADD [RemainingWorkHours] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901083457_AddAppendOnlyTimeEntries'
)
BEGIN
    CREATE TABLE [TimeEntries] (
        [Id] bigint NOT NULL IDENTITY,
        [TaskId] int NOT NULL,
        [SprintId] int NULL,
        [UserId] int NULL,
        [Hours] decimal(18,2) NOT NULL,
        [WorkDate] datetime2 NOT NULL,
        [EntryType] nvarchar(50) NOT NULL,
        [Note] nvarchar(2000) NULL,
        [ReversesEntryId] bigint NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_TimeEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TimeEntries_AppUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AppUsers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_TimeEntries_Sprints_SprintId] FOREIGN KEY ([SprintId]) REFERENCES [Sprints] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_TimeEntries_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901083457_AddAppendOnlyTimeEntries'
)
BEGIN
    CREATE INDEX [IX_TimeEntries_SprintId_WorkDate] ON [TimeEntries] ([SprintId], [WorkDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901083457_AddAppendOnlyTimeEntries'
)
BEGIN
    CREATE INDEX [IX_TimeEntries_TaskId_WorkDate] ON [TimeEntries] ([TaskId], [WorkDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901083457_AddAppendOnlyTimeEntries'
)
BEGIN
    CREATE INDEX [IX_TimeEntries_UserId_WorkDate] ON [TimeEntries] ([UserId], [WorkDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901083457_AddAppendOnlyTimeEntries'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901083457_AddAppendOnlyTimeEntries', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901095933_AddEstimateUnits'
)
BEGIN
    ALTER TABLE [Tasks] ADD [EstimateUnit] nvarchar(20) NOT NULL DEFAULT N'Points';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901095933_AddEstimateUnits'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901095933_AddEstimateUnits', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901103000_AddUserTimeOff'
)
BEGIN
    CREATE TABLE [UserTimeOff] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NOT NULL,
        [Reason] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_UserTimeOff] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserTimeOff_AppUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901103000_AddUserTimeOff'
)
BEGIN
    CREATE INDEX [IX_UserTimeOff_UserId_StartDate_EndDate] ON [UserTimeOff] ([UserId], [StartDate], [EndDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901103000_AddUserTimeOff'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901103000_AddUserTimeOff', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901111927_AddDurableEmailOutbox'
)
BEGIN
    CREATE TABLE [EmailOutboxMessages] (
        [Id] bigint NOT NULL IDENTITY,
        [ToEmail] nvarchar(320) NOT NULL,
        [ToName] nvarchar(200) NOT NULL,
        [Subject] nvarchar(500) NOT NULL,
        [HtmlBody] nvarchar(max) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [AttemptCount] int NOT NULL,
        [AvailableAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ProcessingAt] datetime2 NULL,
        [SentAt] datetime2 NULL,
        [LastError] nvarchar(4000) NULL,
        [IdempotencyKey] nvarchar(200) NULL,
        CONSTRAINT [PK_EmailOutboxMessages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901111927_AddDurableEmailOutbox'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_EmailOutboxMessages_IdempotencyKey] ON [EmailOutboxMessages] ([IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901111927_AddDurableEmailOutbox'
)
BEGIN
    CREATE INDEX [IX_EmailOutboxMessages_Status_AvailableAt] ON [EmailOutboxMessages] ([Status], [AvailableAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901111927_AddDurableEmailOutbox'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901111927_AddDurableEmailOutbox', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901113614_MakeProjectsFirstClass'
)
BEGIN
    ALTER TABLE [Projects] ADD [CreatedAt] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901113614_MakeProjectsFirstClass'
)
BEGIN
    ALTER TABLE [Projects] ADD [CreatedBy] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901113614_MakeProjectsFirstClass'
)
BEGIN
    ALTER TABLE [Projects] ADD [Description] nvarchar(2000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901113614_MakeProjectsFirstClass'
)
BEGIN
    ALTER TABLE [Projects] ADD [LeadUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901113614_MakeProjectsFirstClass'
)
BEGIN
    ALTER TABLE [Projects] ADD [StartDate] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901113614_MakeProjectsFirstClass'
)
BEGIN
    ALTER TABLE [Projects] ADD [Status] nvarchar(30) NOT NULL DEFAULT N'Active';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901113614_MakeProjectsFirstClass'
)
BEGIN
    ALTER TABLE [Projects] ADD [TargetDate] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901113614_MakeProjectsFirstClass'
)
BEGIN
    ALTER TABLE [Projects] ADD [TeamId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901113614_MakeProjectsFirstClass'
)
BEGIN
    CREATE INDEX [IX_Projects_LeadUserId] ON [Projects] ([LeadUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901113614_MakeProjectsFirstClass'
)
BEGIN
    CREATE INDEX [IX_Projects_TeamId_Status_Name] ON [Projects] ([TeamId], [Status], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901113614_MakeProjectsFirstClass'
)
BEGIN
    ALTER TABLE [Projects] ADD CONSTRAINT [FK_Projects_AppUsers_LeadUserId] FOREIGN KEY ([LeadUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901113614_MakeProjectsFirstClass'
)
BEGIN
    ALTER TABLE [Projects] ADD CONSTRAINT [FK_Projects_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901113614_MakeProjectsFirstClass'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901113614_MakeProjectsFirstClass', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901121801_LinkTasksToGovernedProjects'
)
BEGIN
    ALTER TABLE [Tasks] ADD [ProjectId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901121801_LinkTasksToGovernedProjects'
)
BEGIN

                    UPDATE t SET ProjectId = p.Id
                    FROM Tasks t INNER JOIN Projects p ON p.TeamId = t.TeamId
                        AND p.Status = 'Active' AND p.Name = t.Project
                    WHERE t.TeamId IS NOT NULL
                        AND NULLIF(LTRIM(RTRIM(t.Project)), '') IS NOT NULL
                        AND (SELECT COUNT(*) FROM Projects p2
                             WHERE p2.TeamId = t.TeamId AND p2.Status = 'Active'
                               AND p2.Name = t.Project) = 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901121801_LinkTasksToGovernedProjects'
)
BEGIN
    CREATE INDEX [IX_Tasks_ProjectId] ON [Tasks] ([ProjectId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901121801_LinkTasksToGovernedProjects'
)
BEGIN
    ALTER TABLE [Tasks] ADD CONSTRAINT [FK_Tasks_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901121801_LinkTasksToGovernedProjects'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901121801_LinkTasksToGovernedProjects', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901123003_AddProjectReleases'
)
BEGIN
    CREATE TABLE [ProjectReleases] (
        [Id] int NOT NULL IDENTITY,
        [ProjectId] int NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Status] nvarchar(30) NOT NULL DEFAULT N'Planned',
        [TargetDate] datetime2 NULL,
        [ReleasedAt] datetime2 NULL,
        [Notes] nvarchar(2000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_ProjectReleases] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProjectReleases_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901123003_AddProjectReleases'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProjectReleases_ProjectId_Name] ON [ProjectReleases] ([ProjectId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901123003_AddProjectReleases'
)
BEGIN
    CREATE INDEX [IX_ProjectReleases_ProjectId_Status_TargetDate] ON [ProjectReleases] ([ProjectId], [Status], [TargetDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901123003_AddProjectReleases'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901123003_AddProjectReleases', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901123814_AddTaskChecklists'
)
BEGIN
    CREATE TABLE [TaskChecklistItems] (
        [Id] int NOT NULL IDENTITY,
        [TaskId] int NOT NULL,
        [Text] nvarchar(500) NOT NULL,
        [IsCompleted] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [CompletedAt] datetime2 NULL,
        [CompletedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_TaskChecklistItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TaskChecklistItems_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901123814_AddTaskChecklists'
)
BEGIN
    CREATE INDEX [IX_TaskChecklistItems_TaskId_SortOrder] ON [TaskChecklistItems] ([TaskId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901123814_AddTaskChecklists'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901123814_AddTaskChecklists', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901124918_AddTaskTemplates'
)
BEGIN
    CREATE TABLE [TaskTemplates] (
        [Id] int NOT NULL IDENTITY,
        [TeamId] int NULL,
        [Name] nvarchar(200) NOT NULL,
        [ItemType] nvarchar(50) NOT NULL,
        [Priority] nvarchar(max) NULL,
        [Estimate] decimal(18,2) NULL,
        [EstimateUnit] nvarchar(20) NOT NULL DEFAULT N'Points',
        [Notes] nvarchar(4000) NULL,
        [Labels] nvarchar(max) NULL,
        [AcceptanceCriteria] nvarchar(4000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_TaskTemplates] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901124918_AddTaskTemplates'
)
BEGIN
    CREATE TABLE [TaskTemplateChecklistItems] (
        [Id] int NOT NULL IDENTITY,
        [TaskTemplateId] int NOT NULL,
        [Text] nvarchar(500) NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_TaskTemplateChecklistItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TaskTemplateChecklistItems_TaskTemplates_TaskTemplateId] FOREIGN KEY ([TaskTemplateId]) REFERENCES [TaskTemplates] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901124918_AddTaskTemplates'
)
BEGIN
    CREATE INDEX [IX_TaskTemplateChecklistItems_TaskTemplateId_SortOrder] ON [TaskTemplateChecklistItems] ([TaskTemplateId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901124918_AddTaskTemplates'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TaskTemplates_TeamId_Name] ON [TaskTemplates] ([TeamId], [Name]) WHERE [TeamId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901124918_AddTaskTemplates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901124918_AddTaskTemplates', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901132434_AddRecurringTaskDefinitions'
)
BEGIN
    CREATE TABLE [RecurringTaskDefinitions] (
        [Id] int NOT NULL IDENTITY,
        [TeamId] int NOT NULL,
        [TaskTemplateId] int NOT NULL,
        [Goal] nvarchar(500) NOT NULL,
        [OwnerUserId] int NULL,
        [Frequency] nvarchar(20) NOT NULL,
        [NextRunAt] datetime2 NOT NULL,
        [EndsAt] datetime2 NULL,
        [IsActive] bit NOT NULL,
        [LastRunAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_RecurringTaskDefinitions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RecurringTaskDefinitions_AppUsers_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_RecurringTaskDefinitions_TaskTemplates_TaskTemplateId] FOREIGN KEY ([TaskTemplateId]) REFERENCES [TaskTemplates] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RecurringTaskDefinitions_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901132434_AddRecurringTaskDefinitions'
)
BEGIN
    CREATE INDEX [IX_RecurringTaskDefinitions_IsActive_NextRunAt] ON [RecurringTaskDefinitions] ([IsActive], [NextRunAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901132434_AddRecurringTaskDefinitions'
)
BEGIN
    CREATE INDEX [IX_RecurringTaskDefinitions_OwnerUserId] ON [RecurringTaskDefinitions] ([OwnerUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901132434_AddRecurringTaskDefinitions'
)
BEGIN
    CREATE INDEX [IX_RecurringTaskDefinitions_TaskTemplateId] ON [RecurringTaskDefinitions] ([TaskTemplateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901132434_AddRecurringTaskDefinitions'
)
BEGIN
    CREATE INDEX [IX_RecurringTaskDefinitions_TeamId_TaskTemplateId] ON [RecurringTaskDefinitions] ([TeamId], [TaskTemplateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901132434_AddRecurringTaskDefinitions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901132434_AddRecurringTaskDefinitions', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133156_AddRecurringTaskRuns'
)
BEGIN
    CREATE TABLE [RecurringTaskRuns] (
        [Id] bigint NOT NULL IDENTITY,
        [RecurringTaskDefinitionId] int NOT NULL,
        [ScheduledFor] datetime2 NOT NULL,
        [TaskId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_RecurringTaskRuns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RecurringTaskRuns_RecurringTaskDefinitions_RecurringTaskDefinitionId] FOREIGN KEY ([RecurringTaskDefinitionId]) REFERENCES [RecurringTaskDefinitions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RecurringTaskRuns_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133156_AddRecurringTaskRuns'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RecurringTaskRuns_RecurringTaskDefinitionId_ScheduledFor] ON [RecurringTaskRuns] ([RecurringTaskDefinitionId], [ScheduledFor]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133156_AddRecurringTaskRuns'
)
BEGIN
    CREATE INDEX [IX_RecurringTaskRuns_TaskId] ON [RecurringTaskRuns] ([TaskId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133156_AddRecurringTaskRuns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901133156_AddRecurringTaskRuns', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133439_AddGovernedCustomFields'
)
BEGIN
    CREATE TABLE [CustomFieldDefinitions] (
        [Id] int NOT NULL IDENTITY,
        [TeamId] int NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [FieldType] nvarchar(20) NOT NULL,
        [IsRequired] bit NOT NULL,
        [OptionsJson] nvarchar(max) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_CustomFieldDefinitions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133439_AddGovernedCustomFields'
)
BEGIN
    CREATE TABLE [TaskCustomFieldValues] (
        [Id] int NOT NULL IDENTITY,
        [TaskId] int NOT NULL,
        [CustomFieldDefinitionId] int NOT NULL,
        [Value] nvarchar(1000) NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_TaskCustomFieldValues] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TaskCustomFieldValues_CustomFieldDefinitions_CustomFieldDefinitionId] FOREIGN KEY ([CustomFieldDefinitionId]) REFERENCES [CustomFieldDefinitions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TaskCustomFieldValues_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133439_AddGovernedCustomFields'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomFieldDefinitions_TeamId_Name] ON [CustomFieldDefinitions] ([TeamId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133439_AddGovernedCustomFields'
)
BEGIN
    CREATE INDEX [IX_TaskCustomFieldValues_CustomFieldDefinitionId] ON [TaskCustomFieldValues] ([CustomFieldDefinitionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133439_AddGovernedCustomFields'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TaskCustomFieldValues_TaskId_CustomFieldDefinitionId] ON [TaskCustomFieldValues] ([TaskId], [CustomFieldDefinitionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133439_AddGovernedCustomFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901133439_AddGovernedCustomFields', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902085914_AddApiClients'
)
BEGIN
    CREATE TABLE [ApiClients] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [KeyPrefix] nvarchar(12) NOT NULL,
        [KeyHash] nvarchar(128) NOT NULL,
        [Scopes] nvarchar(2000) NOT NULL,
        [OwnerUserId] int NOT NULL,
        [TeamId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NULL,
        [RevokedAt] datetime2 NULL,
        [LastUsedAt] datetime2 NULL,
        [LastUsedIp] nvarchar(45) NULL,
        CONSTRAINT [PK_ApiClients] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902085914_AddApiClients'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ApiClients_KeyHash] ON [ApiClients] ([KeyHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902085914_AddApiClients'
)
BEGIN
    CREATE INDEX [IX_ApiClients_OwnerUserId_RevokedAt] ON [ApiClients] ([OwnerUserId], [RevokedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902085914_AddApiClients'
)
BEGIN
    CREATE INDEX [IX_ApiClients_TeamId_RevokedAt] ON [ApiClients] ([TeamId], [RevokedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902085914_AddApiClients'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260902085914_AddApiClients', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902092902_AddWebhooks'
)
BEGIN
    CREATE TABLE [WebhookDeliveries] (
        [Id] bigint NOT NULL IDENTITY,
        [SubscriptionId] int NOT NULL,
        [EventId] nvarchar(64) NOT NULL,
        [EventType] nvarchar(100) NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [AttemptCount] int NOT NULL,
        [AvailableAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ProcessingAt] datetime2 NULL,
        [DeliveredAt] datetime2 NULL,
        [ResponseStatusCode] int NULL,
        [LastError] nvarchar(2000) NULL,
        CONSTRAINT [PK_WebhookDeliveries] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902092902_AddWebhooks'
)
BEGIN
    CREATE TABLE [WebhookSubscriptions] (
        [Id] int NOT NULL IDENTITY,
        [TeamId] int NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Url] nvarchar(2048) NOT NULL,
        [Secret] nvarchar(128) NOT NULL,
        [EventTypes] nvarchar(1000) NOT NULL,
        [IsActive] bit NOT NULL,
        [OwnerUserId] int NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [LastStatus] nvarchar(30) NULL,
        [LastDeliveryAt] datetime2 NULL,
        CONSTRAINT [PK_WebhookSubscriptions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902092902_AddWebhooks'
)
BEGIN
    CREATE INDEX [IX_WebhookDeliveries_Status_AvailableAt] ON [WebhookDeliveries] ([Status], [AvailableAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902092902_AddWebhooks'
)
BEGIN
    CREATE INDEX [IX_WebhookDeliveries_SubscriptionId] ON [WebhookDeliveries] ([SubscriptionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902092902_AddWebhooks'
)
BEGIN
    CREATE INDEX [IX_WebhookSubscriptions_TeamId_IsActive] ON [WebhookSubscriptions] ([TeamId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902092902_AddWebhooks'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260902092902_AddWebhooks', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902094313_AddImportJobs'
)
BEGIN
    CREATE TABLE [ImportJobs] (
        [Id] int NOT NULL IDENTITY,
        [TeamId] int NOT NULL,
        [FileName] nvarchar(260) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [TotalRows] int NOT NULL,
        [ValidRows] int NOT NULL,
        [ErrorRows] int NOT NULL,
        [CreatedRows] int NOT NULL,
        [SkippedRows] int NOT NULL,
        [FailedRows] int NOT NULL,
        [CreatedByUserId] int NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [AppliedAt] datetime2 NULL,
        CONSTRAINT [PK_ImportJobs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902094313_AddImportJobs'
)
BEGIN
    CREATE TABLE [ImportRows] (
        [Id] bigint NOT NULL IDENTITY,
        [ImportJobId] int NOT NULL,
        [RowNumber] int NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [ExternalKey] nvarchar(200) NULL,
        [Status] nvarchar(20) NOT NULL,
        [Message] nvarchar(500) NULL,
        [CreatedTaskId] int NULL,
        CONSTRAINT [PK_ImportRows] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902094313_AddImportJobs'
)
BEGIN
    CREATE INDEX [IX_ImportJobs_TeamId_Status] ON [ImportJobs] ([TeamId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902094313_AddImportJobs'
)
BEGIN
    CREATE INDEX [IX_ImportRows_ExternalKey] ON [ImportRows] ([ExternalKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902094313_AddImportJobs'
)
BEGIN
    CREATE INDEX [IX_ImportRows_ImportJobId] ON [ImportRows] ([ImportJobId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902094313_AddImportJobs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260902094313_AddImportJobs', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902105307_AddApiIdempotency'
)
BEGIN
    CREATE TABLE [ApiIdempotencyRecords] (
        [Id] bigint NOT NULL IDENTITY,
        [ApiClientId] int NOT NULL,
        [IdempotencyKey] nvarchar(200) NOT NULL,
        [Method] nvarchar(10) NOT NULL,
        [Path] nvarchar(500) NOT NULL,
        [StatusCode] int NOT NULL,
        [ResponseBody] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ApiIdempotencyRecords] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902105307_AddApiIdempotency'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ApiIdempotencyRecords_ApiClientId_IdempotencyKey] ON [ApiIdempotencyRecords] ([ApiClientId], [IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902105307_AddApiIdempotency'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260902105307_AddApiIdempotency', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903051402_AddAutomationRules'
)
BEGIN
    CREATE TABLE [AutomationRuleRuns] (
        [Id] bigint NOT NULL IDENTITY,
        [RuleId] int NOT NULL,
        [TaskId] int NOT NULL,
        [EventType] nvarchar(50) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Detail] nvarchar(1000) NULL,
        [OccurredAt] datetime2 NOT NULL,
        [OccurredBy] nvarchar(200) NULL,
        CONSTRAINT [PK_AutomationRuleRuns] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903051402_AddAutomationRules'
)
BEGIN
    CREATE TABLE [AutomationRules] (
        [Id] int NOT NULL IDENTITY,
        [TeamId] int NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsEnabled] bit NOT NULL,
        [TriggerType] nvarchar(50) NOT NULL,
        [ConditionField] nvarchar(50) NULL,
        [ConditionOperator] nvarchar(20) NULL,
        [ConditionValue] nvarchar(200) NULL,
        [ActionType] nvarchar(50) NOT NULL,
        [ActionConfig] nvarchar(500) NULL,
        [SortOrder] int NOT NULL,
        [RunCount] int NOT NULL,
        [LastRunAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        CONSTRAINT [PK_AutomationRules] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903051402_AddAutomationRules'
)
BEGIN
    CREATE INDEX [IX_AutomationRuleRuns_RuleId] ON [AutomationRuleRuns] ([RuleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903051402_AddAutomationRules'
)
BEGIN
    CREATE INDEX [IX_AutomationRuleRuns_TaskId] ON [AutomationRuleRuns] ([TaskId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903051402_AddAutomationRules'
)
BEGIN
    CREATE INDEX [IX_AutomationRules_TeamId_TriggerType_IsEnabled] ON [AutomationRules] ([TeamId], [TriggerType], [IsEnabled]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903051402_AddAutomationRules'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903051402_AddAutomationRules', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903060055_AddSlaPolicies'
)
BEGIN
    CREATE TABLE [SlaPolicies] (
        [Id] int NOT NULL IDENTITY,
        [TeamId] int NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsEnabled] bit NOT NULL,
        [AppliesToItemType] nvarchar(50) NULL,
        [AppliesToPriority] nvarchar(50) NULL,
        [TargetHours] int NOT NULL,
        [SortOrder] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        CONSTRAINT [PK_SlaPolicies] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903060055_AddSlaPolicies'
)
BEGIN
    CREATE INDEX [IX_SlaPolicies_TeamId_IsEnabled] ON [SlaPolicies] ([TeamId], [IsEnabled]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903060055_AddSlaPolicies'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903060055_AddSlaPolicies', N'10.0.9');
END;

COMMIT;
GO

