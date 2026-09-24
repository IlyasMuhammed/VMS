IF OBJECT_ID(N'[doc].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'doc') IS NULL EXEC(N'CREATE SCHEMA [doc];');
    CREATE TABLE [doc].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [doc].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922124700_InitialDocuments'
)
BEGIN
    IF SCHEMA_ID(N'doc') IS NULL EXEC(N'CREATE SCHEMA [doc];');
END;

IF NOT EXISTS (
    SELECT * FROM [doc].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922124700_InitialDocuments'
)
BEGIN
    CREATE TABLE [doc].[Documents] (
        [DocumentId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [DocumentCode] nvarchar(20) NOT NULL,
        [OwnerType] nvarchar(20) NOT NULL,
        [OwnerId] int NOT NULL,
        [DocumentTypeId] int NOT NULL,
        [VersionNo] int NOT NULL,
        [IsCurrent] bit NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [DocumentNumber] nvarchar(60) NULL,
        [Provider] nvarchar(120) NULL,
        [IssueDate] date NULL,
        [ExpiryDate] date NULL,
        [StorageKey] nvarchar(300) NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [OriginalFileName] nvarchar(200) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [RejectReason] nvarchar(500) NULL,
        [LinkedTransactionId] int NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        [ModifiedBy] int NULL,
        [ModifiedOn] datetime2 NOT NULL,
        CONSTRAINT [PK_Documents] PRIMARY KEY ([DocumentId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [doc].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922124700_InitialDocuments'
)
BEGIN
    CREATE TABLE [doc].[DocumentTypes] (
        [DocumentTypeId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(80) NOT NULL,
        [AppliesTo] int NOT NULL,
        [PartnerRole] nvarchar(30) NULL,
        [IsExpirable] bit NOT NULL,
        [DefaultValidityValue] int NULL,
        [DefaultValidityUnit] nvarchar(10) NULL,
        [IsPeriodic] bit NOT NULL,
        [RenewalLeadDays] int NOT NULL,
        [MandatoryLevel] nvarchar(10) NOT NULL,
        [RequiresDocumentNumber] bit NOT NULL,
        [HasCost] bit NOT NULL,
        [AllowedFormats] int NOT NULL,
        [MaxFileSizeMb] int NOT NULL,
        [RetentionYears] int NOT NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_DocumentTypes] PRIMARY KEY ([DocumentTypeId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [doc].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922124700_InitialDocuments'
)
BEGIN
    CREATE INDEX [IX_Documents_TenantId_DocumentTypeId] ON [doc].[Documents] ([TenantId], [DocumentTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [doc].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922124700_InitialDocuments'
)
BEGIN
    CREATE INDEX [IX_Documents_TenantId_Status_ExpiryDate] ON [doc].[Documents] ([TenantId], [Status], [ExpiryDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [doc].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922124700_InitialDocuments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Documents_Current] ON [doc].[Documents] ([TenantId], [OwnerType], [OwnerId], [DocumentTypeId]) WHERE [IsCurrent] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [doc].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922124700_InitialDocuments'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Documents_Version] ON [doc].[Documents] ([TenantId], [OwnerType], [OwnerId], [DocumentTypeId], [VersionNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [doc].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922124700_InitialDocuments'
)
BEGIN
    CREATE INDEX [IX_DocumentTypes_TenantId_AppliesTo] ON [doc].[DocumentTypes] ([TenantId], [AppliesTo]);
END;

IF NOT EXISTS (
    SELECT * FROM [doc].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922124700_InitialDocuments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DocumentTypes_TenantId_Code] ON [doc].[DocumentTypes] ([TenantId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [doc].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922124700_InitialDocuments'
)
BEGIN
    INSERT INTO [doc].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260922124700_InitialDocuments', N'9.0.2');
END;

COMMIT;
GO

