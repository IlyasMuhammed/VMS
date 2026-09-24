IF OBJECT_ID(N'[auth].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'auth') IS NULL EXEC(N'CREATE SCHEMA [auth];');
    CREATE TABLE [auth].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    IF SCHEMA_ID(N'auth') IS NULL EXEC(N'CREATE SCHEMA [auth];');
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE TABLE [auth].[Permissions] (
        [PermissionID] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Code] nvarchar(100) NOT NULL,
        [Module] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([PermissionID])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE TABLE [auth].[Roles] (
        [RoleID] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [RoleCode] nvarchar(50) NOT NULL,
        [Description] nvarchar(500) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [IsGlobal] bit NOT NULL DEFAULT CAST(1 AS bit),
        [TenantId] uniqueidentifier NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([RoleID])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE TABLE [auth].[RolePermissions] (
        [RolePermissionID] int NOT NULL IDENTITY,
        [RoleID] int NOT NULL,
        [PermissionID] int NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RolePermissionID]),
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionID] FOREIGN KEY ([PermissionID]) REFERENCES [auth].[Permissions] ([PermissionID]) ON DELETE CASCADE,
        CONSTRAINT [FK_RolePermissions_Roles_RoleID] FOREIGN KEY ([RoleID]) REFERENCES [auth].[Roles] ([RoleID]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE TABLE [auth].[UserAccounts] (
        [UserID] int NOT NULL IDENTITY,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NULL,
        [Email] nvarchar(256) NOT NULL,
        [PasswordHash] nvarchar(512) NOT NULL,
        [Phone] nvarchar(30) NULL,
        [Department] nvarchar(100) NULL,
        [RoleID] int NOT NULL,
        [IsActive] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedBy] int NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [UpdatedDate] datetime2 NULL,
        [LastLoginAt] datetime2 NULL,
        [FailedLoginAttempts] int NOT NULL DEFAULT 0,
        [LastFailedAt] datetime2 NULL,
        [LockedUntil] datetime2 NULL,
        [PasswordResetCodeHash] nvarchar(64) NULL,
        [PasswordResetCodeExpiresAt] datetime2 NULL,
        [PasswordResetAttempts] int NOT NULL DEFAULT 0,
        [InviteTokenHash] nvarchar(64) NULL,
        [InviteTokenExpiresAt] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_UserAccounts] PRIMARY KEY ([UserID]),
        CONSTRAINT [FK_UserAccounts_Roles_RoleID] FOREIGN KEY ([RoleID]) REFERENCES [auth].[Roles] ([RoleID]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE TABLE [auth].[UserSessions] (
        [Id] uniqueidentifier NOT NULL,
        [UserID] int NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [RevokedAt] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_UserSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserSessions_UserAccounts_UserID] FOREIGN KEY ([UserID]) REFERENCES [auth].[UserAccounts] ([UserID]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_Code] ON [auth].[Permissions] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionID] ON [auth].[RolePermissions] ([PermissionID]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RolePermissions_RoleID_PermissionID] ON [auth].[RolePermissions] ([RoleID], [PermissionID]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Roles_TenantId_RoleCode] ON [auth].[Roles] ([TenantId], [RoleCode]) WHERE [TenantId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserAccounts_Email] ON [auth].[UserAccounts] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE INDEX [IX_UserAccounts_InviteTokenHash] ON [auth].[UserAccounts] ([InviteTokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE INDEX [IX_UserAccounts_RoleID] ON [auth].[UserAccounts] ([RoleID]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE INDEX [IX_UserAccounts_TenantId_RoleID] ON [auth].[UserAccounts] ([TenantId], [RoleID]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE INDEX [IX_UserSessions_ExpiresAt] ON [auth].[UserSessions] ([ExpiresAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE INDEX [IX_UserSessions_TenantId] ON [auth].[UserSessions] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserSessions_TokenHash] ON [auth].[UserSessions] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    CREATE INDEX [IX_UserSessions_UserID] ON [auth].[UserSessions] ([UserID]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090545_InitialAuth'
)
BEGIN
    INSERT INTO [auth].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260919090545_InitialAuth', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920053351_FoundationSecurity'
)
BEGIN
    ALTER TABLE [auth].[Permissions] ADD [Level] nvarchar(12) NOT NULL DEFAULT N'Operation';
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920053351_FoundationSecurity'
)
BEGIN
    CREATE TABLE [auth].[AccessDenials] (
        [AccessDenialID] bigint NOT NULL IDENTITY,
        [OccurredAt] datetime2 NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] int NOT NULL,
        [UserName] nvarchar(200) NULL,
        [Permission] nvarchar(100) NOT NULL,
        [Method] nvarchar(10) NOT NULL,
        [Path] nvarchar(500) NOT NULL,
        [RouteValues] nvarchar(500) NULL,
        [IpAddress] nvarchar(64) NULL,
        CONSTRAINT [PK_AccessDenials] PRIMARY KEY ([AccessDenialID])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920053351_FoundationSecurity'
)
BEGIN
    CREATE TABLE [auth].[UserRoles] (
        [UserRoleID] int NOT NULL IDENTITY,
        [UserID] int NOT NULL,
        [RoleID] int NOT NULL,
        [ScopeType] nvarchar(20) NOT NULL DEFAULT N'AllBranches',
        [BranchId] uniqueidentifier NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserRoleID]),
        CONSTRAINT [FK_UserRoles_Roles_RoleID] FOREIGN KEY ([RoleID]) REFERENCES [auth].[Roles] ([RoleID]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserRoles_UserAccounts_UserID] FOREIGN KEY ([UserID]) REFERENCES [auth].[UserAccounts] ([UserID]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920053351_FoundationSecurity'
)
BEGIN
    CREATE INDEX [IX_AccessDenials_TenantId_Permission_OccurredAt] ON [auth].[AccessDenials] ([TenantId], [Permission], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920053351_FoundationSecurity'
)
BEGIN
    CREATE INDEX [IX_AccessDenials_TenantId_UserId_OccurredAt] ON [auth].[AccessDenials] ([TenantId], [UserId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920053351_FoundationSecurity'
)
BEGIN
    CREATE INDEX [IX_UserRoles_RoleID] ON [auth].[UserRoles] ([RoleID]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920053351_FoundationSecurity'
)
BEGIN
    CREATE INDEX [IX_UserRoles_TenantId_RoleID] ON [auth].[UserRoles] ([TenantId], [RoleID]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920053351_FoundationSecurity'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserRoles_UserID_RoleID] ON [auth].[UserRoles] ([UserID], [RoleID]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920053351_FoundationSecurity'
)
BEGIN
    INSERT INTO [auth].[UserRoles] ([UserID], [RoleID], [ScopeType], [TenantId])
                      SELECT [UserID], [RoleID], N'AllBranches', [TenantId]
                      FROM [auth].[UserAccounts]
                      WHERE [IsDeleted] = 0;
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920053351_FoundationSecurity'
)
BEGIN
    INSERT INTO [auth].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260920053351_FoundationSecurity', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920055445_AuditTableMapped'
)
BEGIN
    INSERT INTO [auth].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260920055445_AuditTableMapped', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922150027_UserScopeAndDriverLink'
)
BEGIN
    ALTER TABLE [auth].[UserAccounts] ADD [LinkedPartnerId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922150027_UserScopeAndDriverLink'
)
BEGIN
    INSERT INTO [auth].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260922150027_UserScopeAndDriverLink', N'9.0.2');
END;

COMMIT;
GO

