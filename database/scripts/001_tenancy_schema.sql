IF OBJECT_ID(N'[tenancy].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'tenancy') IS NULL EXEC(N'CREATE SCHEMA [tenancy];');
    CREATE TABLE [tenancy].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [tenancy].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090540_InitialTenancy'
)
BEGIN
    IF SCHEMA_ID(N'tenancy') IS NULL EXEC(N'CREATE SCHEMA [tenancy];');
END;

IF NOT EXISTS (
    SELECT * FROM [tenancy].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090540_InitialTenancy'
)
BEGIN
    CREATE TABLE [tenancy].[SuperAdminUsers] (
        [UserId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SuperAdminUsers] PRIMARY KEY ([UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [tenancy].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090540_InitialTenancy'
)
BEGIN
    CREATE TABLE [tenancy].[Tenants] (
        [Id] uniqueidentifier NOT NULL,
        [TenantCode] nvarchar(30) NOT NULL,
        [TenantName] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [ContactEmail] nvarchar(256) NULL,
        [ContactPhone] nvarchar(30) NULL,
        [Address] nvarchar(500) NULL,
        [Country] nvarchar(100) NULL,
        [TimeZone] nvarchar(100) NULL,
        [CreatedBy] int NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedBy] int NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [tenancy].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090540_InitialTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_TenantCode] ON [tenancy].[Tenants] ([TenantCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [tenancy].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260919090540_InitialTenancy'
)
BEGIN
    INSERT INTO [tenancy].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260919090540_InitialTenancy', N'9.0.2');
END;

COMMIT;
GO

