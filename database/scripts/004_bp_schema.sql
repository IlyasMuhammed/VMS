IF OBJECT_ID(N'[bp].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'bp') IS NULL EXEC(N'CREATE SCHEMA [bp];');
    CREATE TABLE [bp].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    IF SCHEMA_ID(N'bp') IS NULL EXEC(N'CREATE SCHEMA [bp];');
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE TABLE [bp].[BpRoleLog] (
        [BpRoleLogId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [BusinessPartnerId] int NOT NULL,
        [RoleCode] nvarchar(20) NOT NULL,
        [Action] nvarchar(10) NOT NULL,
        [EffectiveDate] date NOT NULL,
        [Reason] nvarchar(500) NULL,
        [UserId] int NOT NULL,
        [UserName] nvarchar(200) NULL,
        [OccurredOn] datetime2 NOT NULL,
        CONSTRAINT [PK_BpRoleLog] PRIMARY KEY ([BpRoleLogId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE TABLE [bp].[BpStatusLog] (
        [BpStatusLogId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [BusinessPartnerId] int NOT NULL,
        [FromStatus] nvarchar(12) NOT NULL,
        [ToStatus] nvarchar(12) NOT NULL,
        [Reason] nvarchar(500) NULL,
        [EffectiveDate] date NOT NULL,
        [UserId] int NOT NULL,
        [UserName] nvarchar(200) NULL,
        [OccurredOn] datetime2 NOT NULL,
        CONSTRAINT [PK_BpStatusLog] PRIMARY KEY ([BpStatusLogId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE TABLE [bp].[BusinessPartners] (
        [BusinessPartnerId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [BpCode] nvarchar(20) NOT NULL,
        [PartyType] nvarchar(10) NOT NULL,
        [LegalName] nvarchar(150) NOT NULL,
        [DisplayName] nvarchar(60) NULL,
        [Cnic] nvarchar(15) NULL,
        [Ntn] nvarchar(15) NULL,
        [Strn] nvarchar(20) NULL,
        [FilerStatus] nvarchar(10) NOT NULL,
        [PrimaryMobile] nvarchar(20) NOT NULL,
        [AlternatePhone] nvarchar(20) NULL,
        [Email] nvarchar(100) NULL,
        [CityId] int NOT NULL,
        [AddressLine] nvarchar(250) NOT NULL,
        [BranchId] uniqueidentifier NULL,
        [Status] nvarchar(12) NOT NULL,
        [StatusReason] nvarchar(500) NULL,
        [OpeningBalance] decimal(18,2) NOT NULL,
        [OpeningBalanceDate] date NULL,
        [Currency] nvarchar(3) NOT NULL,
        [Notes] nvarchar(1000) NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        [ModifiedBy] int NULL,
        [ModifiedOn] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_BusinessPartners] PRIMARY KEY ([BusinessPartnerId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE TABLE [bp].[BpAddresses] (
        [BpAddressId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [BusinessPartnerId] int NOT NULL,
        [AddressType] nvarchar(15) NOT NULL,
        [Line1] nvarchar(250) NOT NULL,
        [Line2] nvarchar(250) NULL,
        [CityId] int NOT NULL,
        [ProvinceCode] nvarchar(30) NULL,
        [Landmark] nvarchar(150) NULL,
        [IsPrimary] bit NOT NULL,
        CONSTRAINT [PK_BpAddresses] PRIMARY KEY ([BpAddressId]),
        CONSTRAINT [FK_BpAddresses_BusinessPartners_BusinessPartnerId] FOREIGN KEY ([BusinessPartnerId]) REFERENCES [bp].[BusinessPartners] ([BusinessPartnerId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE TABLE [bp].[BpBankAccounts] (
        [BpBankAccountId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [BusinessPartnerId] int NOT NULL,
        [AccountTitle] nvarchar(150) NOT NULL,
        [BankName] nvarchar(100) NOT NULL,
        [BranchCode] nvarchar(80) NULL,
        [AccountNumber] nvarchar(34) NOT NULL,
        [Iban] nvarchar(34) NULL,
        [IsPrimary] bit NOT NULL,
        CONSTRAINT [PK_BpBankAccounts] PRIMARY KEY ([BpBankAccountId]),
        CONSTRAINT [FK_BpBankAccounts_BusinessPartners_BusinessPartnerId] FOREIGN KEY ([BusinessPartnerId]) REFERENCES [bp].[BusinessPartners] ([BusinessPartnerId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE TABLE [bp].[BpContacts] (
        [BpContactId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [BusinessPartnerId] int NOT NULL,
        [ContactName] nvarchar(100) NOT NULL,
        [Designation] nvarchar(60) NULL,
        [Mobile] nvarchar(20) NOT NULL,
        [Email] nvarchar(100) NULL,
        [IsPrimary] bit NOT NULL,
        [Notes] nvarchar(250) NULL,
        CONSTRAINT [PK_BpContacts] PRIMARY KEY ([BpContactId]),
        CONSTRAINT [FK_BpContacts_BusinessPartners_BusinessPartnerId] FOREIGN KEY ([BusinessPartnerId]) REFERENCES [bp].[BusinessPartners] ([BusinessPartnerId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE TABLE [bp].[BpCustomerDetails] (
        [BusinessPartnerId] int NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerType] nvarchar(15) NOT NULL,
        [BillingCycle] nvarchar(15) NOT NULL,
        [RateBasis] nvarchar(15) NULL,
        [DefaultRate] decimal(18,2) NULL,
        [CreditLimit] decimal(18,2) NULL,
        [CreditDays] int NULL,
        CONSTRAINT [PK_BpCustomerDetails] PRIMARY KEY ([BusinessPartnerId]),
        CONSTRAINT [FK_BpCustomerDetails_BusinessPartners_BusinessPartnerId] FOREIGN KEY ([BusinessPartnerId]) REFERENCES [bp].[BusinessPartners] ([BusinessPartnerId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE TABLE [bp].[BpDriverDetails] (
        [BusinessPartnerId] int NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [LicenceNo] nvarchar(30) NOT NULL,
        [LicenceType] nvarchar(12) NOT NULL,
        [LicenceIssueDate] date NULL,
        [LicenceExpiryDate] date NOT NULL,
        [EmploymentType] nvarchar(12) NOT NULL,
        [DateOfJoining] date NULL,
        [MonthlyRate] decimal(18,2) NULL,
        [CommissionBasis] nvarchar(10) NOT NULL,
        [CommissionValue] decimal(18,2) NULL,
        [BloodGroup] nvarchar(5) NULL,
        [EmergencyContactName] nvarchar(100) NULL,
        [EmergencyContactPhone] nvarchar(20) NULL,
        [Guarantor] nvarchar(150) NULL,
        [DriverAppAccess] bit NOT NULL,
        CONSTRAINT [PK_BpDriverDetails] PRIMARY KEY ([BusinessPartnerId]),
        CONSTRAINT [FK_BpDriverDetails_BusinessPartners_BusinessPartnerId] FOREIGN KEY ([BusinessPartnerId]) REFERENCES [bp].[BusinessPartners] ([BusinessPartnerId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE TABLE [bp].[BpVendorDetails] (
        [BusinessPartnerId] int NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SupplyCategories] nvarchar(200) NOT NULL,
        [PaymentTermDays] int NOT NULL,
        [CreditLimit] decimal(18,2) NULL,
        CONSTRAINT [PK_BpVendorDetails] PRIMARY KEY ([BusinessPartnerId]),
        CONSTRAINT [FK_BpVendorDetails_BusinessPartners_BusinessPartnerId] FOREIGN KEY ([BusinessPartnerId]) REFERENCES [bp].[BusinessPartners] ([BusinessPartnerId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE TABLE [bp].[BusinessPartnerRoles] (
        [BpRoleId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [BusinessPartnerId] int NOT NULL,
        [RoleCode] nvarchar(20) NOT NULL,
        [IsActive] bit NOT NULL,
        [EffectiveFrom] date NOT NULL,
        [EffectiveTo] date NULL,
        CONSTRAINT [PK_BusinessPartnerRoles] PRIMARY KEY ([BpRoleId]),
        CONSTRAINT [FK_BusinessPartnerRoles_BusinessPartners_BusinessPartnerId] FOREIGN KEY ([BusinessPartnerId]) REFERENCES [bp].[BusinessPartners] ([BusinessPartnerId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BpAddresses_TenantId] ON [bp].[BpAddresses] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_BpAddresses_Primary] ON [bp].[BpAddresses] ([BusinessPartnerId]) WHERE [IsPrimary] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BpBankAccounts_BusinessPartnerId_BankName_AccountNumber] ON [bp].[BpBankAccounts] ([BusinessPartnerId], [BankName], [AccountNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BpBankAccounts_TenantId] ON [bp].[BpBankAccounts] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_BpBankAccounts_Primary] ON [bp].[BpBankAccounts] ([BusinessPartnerId]) WHERE [IsPrimary] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BpContacts_TenantId] ON [bp].[BpContacts] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_BpContacts_Primary] ON [bp].[BpContacts] ([BusinessPartnerId]) WHERE [IsPrimary] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BpCustomerDetails_TenantId] ON [bp].[BpCustomerDetails] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BpDriverDetails_TenantId_LicenceNo] ON [bp].[BpDriverDetails] ([TenantId], [LicenceNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BpRoleLog_BusinessPartnerId_OccurredOn] ON [bp].[BpRoleLog] ([BusinessPartnerId], [OccurredOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BpRoleLog_TenantId] ON [bp].[BpRoleLog] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BpStatusLog_BusinessPartnerId_OccurredOn] ON [bp].[BpStatusLog] ([BusinessPartnerId], [OccurredOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BpStatusLog_TenantId] ON [bp].[BpStatusLog] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BpVendorDetails_TenantId] ON [bp].[BpVendorDetails] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BusinessPartnerRoles_BusinessPartnerId_RoleCode] ON [bp].[BusinessPartnerRoles] ([BusinessPartnerId], [RoleCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BusinessPartnerRoles_TenantId_RoleCode_IsActive] ON [bp].[BusinessPartnerRoles] ([TenantId], [RoleCode], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BusinessPartners_TenantId_BpCode] ON [bp].[BusinessPartners] ([TenantId], [BpCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BusinessPartners_TenantId_CityId] ON [bp].[BusinessPartners] ([TenantId], [CityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BusinessPartners_TenantId_LegalName] ON [bp].[BusinessPartners] ([TenantId], [LegalName]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BusinessPartners_TenantId_PrimaryMobile] ON [bp].[BusinessPartners] ([TenantId], [PrimaryMobile]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    CREATE INDEX [IX_BusinessPartners_TenantId_Status_ModifiedOn] ON [bp].[BusinessPartners] ([TenantId], [Status], [ModifiedOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_BusinessPartners_Cnic] ON [bp].[BusinessPartners] ([TenantId], [Cnic]) WHERE [Cnic] IS NOT NULL AND [Status] <> ''Merged'' AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_BusinessPartners_Ntn] ON [bp].[BusinessPartners] ([TenantId], [Ntn]) WHERE [Ntn] IS NOT NULL AND [PartyType] = ''Company'' AND [Status] <> ''Merged'' AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_BusinessPartners_Strn] ON [bp].[BusinessPartners] ([TenantId], [Strn]) WHERE [Strn] IS NOT NULL AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [bp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920090512_InitialPartners'
)
BEGIN
    INSERT INTO [bp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260920090512_InitialPartners', N'9.0.2');
END;

COMMIT;
GO

