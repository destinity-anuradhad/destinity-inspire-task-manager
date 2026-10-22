/* ============================================================================
   chat-tables.sql
   Run on:  DestiniyInspire_TaskManager_v2  (the live app DB)
   Purpose: Create the two tables backing the team group-chat feature.
            Team-scoped messages only (no direct messages); attachment blobs live
            outside the web root, only metadata is stored here.
   Idempotent — safe to re-run.
   ============================================================================ */
SET NOCOUNT ON;

IF OBJECT_ID(N'[dbo].[ChatMessages]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ChatMessages] (
        [Id]         INT             IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ChatMessages] PRIMARY KEY,
        [TeamId]     INT             NOT NULL,
        [UserId]     INT             NULL,
        [SenderName] NVARCHAR(256)   NOT NULL CONSTRAINT [DF_ChatMessages_SenderName] DEFAULT (''),
        [Body]       NVARCHAR(MAX)   NULL,
        [CreatedAt]  DATETIME2(7)    NOT NULL CONSTRAINT [DF_ChatMessages_CreatedAt] DEFAULT (SYSDATETIME()),
        [EditedAt]   DATETIME2(7)    NULL,
        [IsDeleted]  BIT             NOT NULL CONSTRAINT [DF_ChatMessages_IsDeleted] DEFAULT (0)
    );
    -- Team feed + keyset pagination ("messages before id X for this team").
    CREATE INDEX [IX_ChatMessages_TeamId_Id] ON [dbo].[ChatMessages] ([TeamId], [Id] DESC);
    PRINT 'Created ChatMessages.';
END
ELSE PRINT 'ChatMessages already exists.';

IF OBJECT_ID(N'[dbo].[ChatAttachments]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ChatAttachments] (
        [Id]          INT           IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ChatAttachments] PRIMARY KEY,
        [MessageId]   INT           NOT NULL,
        [TeamId]      INT           NOT NULL,
        [FileName]    NVARCHAR(500) NOT NULL CONSTRAINT [DF_ChatAttachments_FileName] DEFAULT (''),
        [StoredName]  NVARCHAR(128) NOT NULL CONSTRAINT [DF_ChatAttachments_StoredName] DEFAULT (''),
        [FileSize]    BIGINT        NOT NULL CONSTRAINT [DF_ChatAttachments_FileSize] DEFAULT (0),
        [ContentType] NVARCHAR(256) NULL,
        [UploadedAt]  DATETIME2(7)  NOT NULL CONSTRAINT [DF_ChatAttachments_UploadedAt] DEFAULT (SYSDATETIME())
    );
    CREATE INDEX [IX_ChatAttachments_MessageId] ON [dbo].[ChatAttachments] ([MessageId]);
    PRINT 'Created ChatAttachments.';
END
ELSE PRINT 'ChatAttachments already exists.';

IF OBJECT_ID(N'[dbo].[ChatReactions]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ChatReactions] (
        [Id]        INT           IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ChatReactions] PRIMARY KEY,
        [MessageId] INT           NOT NULL,
        [TeamId]    INT           NOT NULL,
        [UserId]    INT           NOT NULL,
        [Emoji]     NVARCHAR(32)  NOT NULL CONSTRAINT [DF_ChatReactions_Emoji] DEFAULT (''),
        [CreatedAt] DATETIME2(7)  NOT NULL CONSTRAINT [DF_ChatReactions_CreatedAt] DEFAULT (SYSDATETIME())
    );
    CREATE INDEX [IX_ChatReactions_MessageId] ON [dbo].[ChatReactions] ([MessageId]);
    -- One reaction per (message, user, emoji) — the toggle relies on this.
    CREATE UNIQUE INDEX [IX_ChatReactions_Msg_User_Emoji] ON [dbo].[ChatReactions] ([MessageId], [UserId], [Emoji]);
    PRINT 'Created ChatReactions.';
END
ELSE PRINT 'ChatReactions already exists.';

-- Activity auto-posts (task assigned, sprint created/completed, …) reuse ChatMessages with Kind='event'.
IF COL_LENGTH(N'[dbo].[ChatMessages]', N'Kind') IS NULL
BEGIN
    ALTER TABLE [dbo].[ChatMessages] ADD [Kind] NVARCHAR(24) NOT NULL CONSTRAINT [DF_ChatMessages_Kind] DEFAULT ('user');
    PRINT 'Added ChatMessages.Kind.';
END
ELSE PRINT 'ChatMessages.Kind already exists.';

IF COL_LENGTH(N'[dbo].[ChatMessages]', N'MetaJson') IS NULL
BEGIN
    ALTER TABLE [dbo].[ChatMessages] ADD [MetaJson] NVARCHAR(MAX) NULL;
    PRINT 'Added ChatMessages.MetaJson.';
END
ELSE PRINT 'ChatMessages.MetaJson already exists.';
