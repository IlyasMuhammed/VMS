IF OBJECT_ID(N'[notif].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'notif') IS NULL EXEC(N'CREATE SCHEMA [notif];');
    CREATE TABLE [notif].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [notif].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923040739_InitialNotifications'
)
BEGIN
    IF SCHEMA_ID(N'notif') IS NULL EXEC(N'CREATE SCHEMA [notif];');
END;

IF NOT EXISTS (
    SELECT * FROM [notif].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923040739_InitialNotifications'
)
BEGIN
    CREATE TABLE [notif].[NotificationRules] (
        [NotificationRuleId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [EventType] nvarchar(30) NOT NULL,
        [Name] nvarchar(80) NOT NULL,
        [LeadDays] int NOT NULL,
        [RecipientPermission] nvarchar(60) NOT NULL,
        [EscalationAfterDays] int NULL,
        [EscalationPermission] nvarchar(60) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_NotificationRules] PRIMARY KEY ([NotificationRuleId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [notif].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923040739_InitialNotifications'
)
BEGIN
    CREATE TABLE [notif].[Notifications] (
        [NotificationId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] int NOT NULL,
        [EventType] nvarchar(30) NOT NULL,
        [Title] nvarchar(150) NOT NULL,
        [Message] nvarchar(500) NOT NULL,
        [SourceEntity] nvarchar(40) NOT NULL,
        [SourceId] nvarchar(40) NOT NULL,
        [IsEscalation] bit NOT NULL,
        [OwnerType] nvarchar(20) NOT NULL,
        [OwnerId] int NOT NULL,
        [OccurredOn] datetime2 NOT NULL,
        [IsRead] bit NOT NULL,
        [ReadOn] datetime2 NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([NotificationId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [notif].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923040739_InitialNotifications'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NotificationRules_TenantId_EventType] ON [notif].[NotificationRules] ([TenantId], [EventType]);
END;

IF NOT EXISTS (
    SELECT * FROM [notif].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923040739_InitialNotifications'
)
BEGIN
    CREATE INDEX [IX_Notifications_TenantId_UserId_IsRead_OccurredOn] ON [notif].[Notifications] ([TenantId], [UserId], [IsRead], [OccurredOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [notif].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923040739_InitialNotifications'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Notifications_Dedup] ON [notif].[Notifications] ([TenantId], [UserId], [SourceEntity], [SourceId], [EventType], [IsEscalation]);
END;

IF NOT EXISTS (
    SELECT * FROM [notif].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923040739_InitialNotifications'
)
BEGIN
    INSERT INTO [notif].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260923040739_InitialNotifications', N'9.0.2');
END;

COMMIT;
GO

