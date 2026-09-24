IF OBJECT_ID(N'[core].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'core') IS NULL EXEC(N'CREATE SCHEMA [core];');
    CREATE TABLE [core].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920055440_AuditLog'
)
BEGIN
    IF SCHEMA_ID(N'core') IS NULL EXEC(N'CREATE SCHEMA [core];');
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920055440_AuditLog'
)
BEGIN
    CREATE TABLE [core].[AuditEntries] (
        [AuditEntryID] bigint NOT NULL IDENTITY,
        [OccurredAt] datetime2 NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [GroupId] uniqueidentifier NOT NULL,
        [Entity] nvarchar(100) NOT NULL,
        [RecordId] nvarchar(100) NOT NULL,
        [Action] nvarchar(50) NOT NULL,
        [Field] nvarchar(100) NULL,
        [OldValue] nvarchar(max) NULL,
        [NewValue] nvarchar(max) NULL,
        [Reason] nvarchar(500) NULL,
        [RequiredPermission] nvarchar(100) NULL,
        [UserId] int NULL,
        [UserName] nvarchar(200) NULL,
        [IpAddress] nvarchar(64) NULL,
        CONSTRAINT [PK_AuditEntries] PRIMARY KEY ([AuditEntryID])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920055440_AuditLog'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_GroupId] ON [core].[AuditEntries] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920055440_AuditLog'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_TenantId_Entity_RecordId_OccurredAt] ON [core].[AuditEntries] ([TenantId], [Entity], [RecordId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920055440_AuditLog'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_TenantId_OccurredAt] ON [core].[AuditEntries] ([TenantId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920055440_AuditLog'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_TenantId_UserId_OccurredAt] ON [core].[AuditEntries] ([TenantId], [UserId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920055440_AuditLog'
)
BEGIN

    EXEC(N'
    CREATE TRIGGER [core].[TR_AuditEntries_AppendOnly] ON [core].[AuditEntries]
    INSTEAD OF UPDATE, DELETE
    AS
    BEGIN
        SET NOCOUNT ON;
        THROW 51000, ''Audit entries are append-only: they cannot be changed or deleted.'', 1;
    END')
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920055440_AuditLog'
)
BEGIN
    INSERT INTO [core].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260920055440_AuditLog', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920060629_NumberSeries'
)
BEGIN
    CREATE TABLE [core].[NumberSeries] (
        [NumberSeriesID] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Prefix] nvarchar(10) NOT NULL,
        [Padding] int NOT NULL,
        [ResetPeriod] nvarchar(10) NOT NULL,
        CONSTRAINT [PK_NumberSeries] PRIMARY KEY ([NumberSeriesID])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920060629_NumberSeries'
)
BEGIN
    CREATE TABLE [core].[NumberSeriesCounters] (
        [NumberSeriesID] int NOT NULL,
        [PeriodKey] nvarchar(8) NOT NULL,
        [LastNumber] bigint NOT NULL,
        CONSTRAINT [PK_NumberSeriesCounters] PRIMARY KEY ([NumberSeriesID], [PeriodKey]),
        CONSTRAINT [FK_NumberSeriesCounters_NumberSeries_NumberSeriesID] FOREIGN KEY ([NumberSeriesID]) REFERENCES [core].[NumberSeries] ([NumberSeriesID]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920060629_NumberSeries'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NumberSeries_TenantId_Code] ON [core].[NumberSeries] ([TenantId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920060629_NumberSeries'
)
BEGIN
    INSERT INTO [core].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260920060629_NumberSeries', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920064337_Lookups'
)
BEGIN
    CREATE TABLE [core].[LookupValues] (
        [LookupValueID] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [LookupType] nvarchar(40) NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Description] nvarchar(200) NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [Attributes] nvarchar(max) NULL,
        CONSTRAINT [PK_LookupValues] PRIMARY KEY ([LookupValueID])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920064337_Lookups'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LookupValues_TenantId_LookupType_Code] ON [core].[LookupValues] ([TenantId], [LookupType], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920064337_Lookups'
)
BEGIN
    CREATE INDEX [IX_LookupValues_TenantId_LookupType_SortOrder] ON [core].[LookupValues] ([TenantId], [LookupType], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920064337_Lookups'
)
BEGIN
    INSERT INTO [core].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260920064337_Lookups', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920095938_AuditRoot'
)
BEGIN
    ALTER TABLE [core].[AuditEntries] ADD [RootEntity] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920095938_AuditRoot'
)
BEGIN
    ALTER TABLE [core].[AuditEntries] ADD [RootRecordId] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920095938_AuditRoot'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_TenantId_RootEntity_RootRecordId_OccurredAt] ON [core].[AuditEntries] ([TenantId], [RootEntity], [RootRecordId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920095938_AuditRoot'
)
BEGIN
    INSERT INTO [core].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260920095938_AuditRoot', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922100543_Branches'
)
BEGIN
    CREATE TABLE [core].[Branches] (
        [BranchId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        CONSTRAINT [PK_Branches] PRIMARY KEY ([BranchId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922100543_Branches'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Branches_TenantId_Code] ON [core].[Branches] ([TenantId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922100543_Branches'
)
BEGIN
    INSERT INTO [core].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260922100543_Branches', N'9.0.2');
END;

COMMIT;
GO

