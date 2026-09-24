IF OBJECT_ID(N'[veh].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'veh') IS NULL EXEC(N'CREATE SCHEMA [veh];');
    CREATE TABLE [veh].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    IF SCHEMA_ID(N'veh') IS NULL EXEC(N'CREATE SCHEMA [veh];');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE TABLE [veh].[Vehicles] (
        [VehicleId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [VehicleCode] nvarchar(20) NOT NULL,
        [RegistrationNo] nvarchar(20) NOT NULL,
        [RegNoKey] nvarchar(20) NOT NULL,
        [RegistrationCityId] int NULL,
        [ChassisNo] nvarchar(30) NULL,
        [EngineNo] nvarchar(30) NULL,
        [VehicleTypeId] int NOT NULL,
        [MakeId] int NOT NULL,
        [Model] nvarchar(60) NOT NULL,
        [ManufacturingYear] int NULL,
        [Colour] nvarchar(30) NULL,
        [FuelType] nvarchar(10) NOT NULL,
        [TankCapacity] decimal(10,2) NULL,
        [LoadCapacity] decimal(10,2) NULL,
        [CapacityUnit] nvarchar(12) NULL,
        [AxleConfigurationId] int NULL,
        [BodyTypeId] int NULL,
        [TyreCount] int NULL,
        [Gvw] decimal(10,2) NULL,
        [BranchId] uniqueidentifier NULL,
        [OpeningOdometer] int NULL,
        [OpeningOdometerDate] date NULL,
        [DefaultDriverId] int NULL,
        [FuelCardCompanyId] int NULL,
        [FuelCardNumber] nvarchar(30) NULL,
        [TrackerCompanyId] int NULL,
        [TrackerDeviceId] nvarchar(40) NULL,
        [Remarks] nvarchar(1000) NULL,
        [AcquisitionDate] date NULL,
        [AcquisitionType] nvarchar(20) NULL,
        [Status] nvarchar(24) NOT NULL,
        [CurrentCategory] nvarchar(20) NULL,
        [CurrentCounterpartyId] int NULL,
        [DraftData] nvarchar(max) NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        [ModifiedBy] int NULL,
        [ModifiedOn] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [IsLive] AS CAST(CASE WHEN [Status] IN ('Sold', 'Transferred') OR [IsDeleted] = 1 THEN 0 ELSE 1 END AS bit) PERSISTED,
        [IsInFleet] AS CAST(CASE WHEN [Status] IN ('Active', 'Assigned', 'UnderMaintenance', 'TemporarilyUnavailable') AND [IsDeleted] = 0 THEN 1 ELSE 0 END AS bit) PERSISTED,
        CONSTRAINT [PK_Vehicles] PRIMARY KEY ([VehicleId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE TABLE [veh].[DriverAssignments] (
        [DriverAssignmentId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [VehicleId] int NOT NULL,
        [DriverId] int NOT NULL,
        [EffectiveFrom] date NOT NULL,
        [EffectiveTo] date NULL,
        [EndReason] nvarchar(500) NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        CONSTRAINT [PK_DriverAssignments] PRIMARY KEY ([DriverAssignmentId]),
        CONSTRAINT [FK_DriverAssignments_Vehicles_VehicleId] FOREIGN KEY ([VehicleId]) REFERENCES [veh].[Vehicles] ([VehicleId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE TABLE [veh].[OdometerReadings] (
        [OdometerReadingId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [VehicleId] int NOT NULL,
        [ReadingDate] date NOT NULL,
        [Km] int NOT NULL,
        [Source] nvarchar(20) NOT NULL,
        [Notes] nvarchar(250) NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        CONSTRAINT [PK_OdometerReadings] PRIMARY KEY ([OdometerReadingId]),
        CONSTRAINT [FK_OdometerReadings_Vehicles_VehicleId] FOREIGN KEY ([VehicleId]) REFERENCES [veh].[Vehicles] ([VehicleId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE TABLE [veh].[VehicleAttachedItems] (
        [VehicleAttachedItemId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [VehicleId] int NOT NULL,
        [ItemTypeId] int NOT NULL,
        [Description] nvarchar(150) NOT NULL,
        [SerialNo] nvarchar(60) NULL,
        [SupplierId] int NULL,
        [InstallationDate] date NOT NULL,
        [Cost] decimal(18,2) NULL,
        [WarrantyUntil] date NULL,
        [Condition] nvarchar(12) NULL,
        [Status] nvarchar(12) NOT NULL,
        [DetachedOn] date NULL,
        [DetachReason] nvarchar(500) NULL,
        [TransferredFromItemId] int NULL,
        [TransferredToVehicleId] int NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        CONSTRAINT [PK_VehicleAttachedItems] PRIMARY KEY ([VehicleAttachedItemId]),
        CONSTRAINT [FK_VehicleAttachedItems_Vehicles_VehicleId] FOREIGN KEY ([VehicleId]) REFERENCES [veh].[Vehicles] ([VehicleId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE TABLE [veh].[VehicleLifecycle] (
        [VehicleLifecycleEntryId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [VehicleId] int NOT NULL,
        [EventType] nvarchar(20) NOT NULL,
        [FromStatus] nvarchar(24) NULL,
        [ToStatus] nvarchar(24) NULL,
        [FromCategory] nvarchar(20) NULL,
        [ToCategory] nvarchar(20) NULL,
        [EffectiveDate] date NOT NULL,
        [Reason] nvarchar(500) NULL,
        [Reference] nvarchar(60) NULL,
        [CounterpartyId] int NULL,
        [Amount] decimal(18,2) NULL,
        [UserId] int NULL,
        [UserName] nvarchar(200) NULL,
        [OccurredOn] datetime2 NOT NULL,
        CONSTRAINT [PK_VehicleLifecycle] PRIMARY KEY ([VehicleLifecycleEntryId]),
        CONSTRAINT [FK_VehicleLifecycle_Vehicles_VehicleId] FOREIGN KEY ([VehicleId]) REFERENCES [veh].[Vehicles] ([VehicleId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE TABLE [veh].[VehicleRelations] (
        [VehicleRelationId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [VehicleId] int NOT NULL,
        [Category] nvarchar(20) NOT NULL,
        [CounterpartyId] int NULL,
        [EffectiveFrom] date NOT NULL,
        [EffectiveTo] date NULL,
        [AgreementEndDate] date NULL,
        [AgreementReference] nvarchar(60) NULL,
        [SharePercent] decimal(5,2) NULL,
        [SharingBasis] nvarchar(20) NULL,
        [FixedMonthlyAmount] decimal(18,2) NULL,
        [ExpenseSharingRule] nvarchar(24) NULL,
        [RentAmount] decimal(18,2) NULL,
        [RentFrequency] nvarchar(10) NULL,
        [RentDueDay] int NULL,
        [SecurityDeposit] decimal(18,2) NULL,
        [ArrangementType] nvarchar(20) NULL,
        [AgreedAmount] decimal(18,2) NULL,
        [RevenueSharePercent] decimal(5,2) NULL,
        CONSTRAINT [PK_VehicleRelations] PRIMARY KEY ([VehicleRelationId]),
        CONSTRAINT [FK_VehicleRelations_Vehicles_VehicleId] FOREIGN KEY ([VehicleId]) REFERENCES [veh].[Vehicles] ([VehicleId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE INDEX [IX_DriverAssignments_VehicleId] ON [veh].[DriverAssignments] ([VehicleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_DriverAssignments_Driver] ON [veh].[DriverAssignments] ([TenantId], [DriverId]) WHERE [EffectiveTo] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_DriverAssignments_Vehicle] ON [veh].[DriverAssignments] ([TenantId], [VehicleId]) WHERE [EffectiveTo] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE INDEX [IX_OdometerReadings_TenantId_VehicleId_ReadingDate] ON [veh].[OdometerReadings] ([TenantId], [VehicleId], [ReadingDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE INDEX [IX_OdometerReadings_VehicleId] ON [veh].[OdometerReadings] ([VehicleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE INDEX [IX_VehicleAttachedItems_TenantId_VehicleId_Status] ON [veh].[VehicleAttachedItems] ([TenantId], [VehicleId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE INDEX [IX_VehicleAttachedItems_VehicleId] ON [veh].[VehicleAttachedItems] ([VehicleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_VehicleItems_Serial] ON [veh].[VehicleAttachedItems] ([TenantId], [SerialNo]) WHERE [SerialNo] IS NOT NULL AND [Status] = ''Attached''');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE INDEX [IX_VehicleLifecycle_TenantId_VehicleId_OccurredOn] ON [veh].[VehicleLifecycle] ([TenantId], [VehicleId], [OccurredOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE INDEX [IX_VehicleLifecycle_VehicleId] ON [veh].[VehicleLifecycle] ([VehicleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE INDEX [IX_VehicleRelations_TenantId_CounterpartyId] ON [veh].[VehicleRelations] ([TenantId], [CounterpartyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE INDEX [IX_VehicleRelations_VehicleId] ON [veh].[VehicleRelations] ([VehicleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_VehicleRelations_Open] ON [veh].[VehicleRelations] ([TenantId], [VehicleId]) WHERE [EffectiveTo] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE INDEX [IX_Vehicles_TenantId_CurrentCategory] ON [veh].[Vehicles] ([TenantId], [CurrentCategory]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE INDEX [IX_Vehicles_TenantId_DefaultDriverId] ON [veh].[Vehicles] ([TenantId], [DefaultDriverId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE INDEX [IX_Vehicles_TenantId_Status_ModifiedOn] ON [veh].[Vehicles] ([TenantId], [Status], [ModifiedOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Vehicles_TenantId_VehicleCode] ON [veh].[Vehicles] ([TenantId], [VehicleCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    CREATE INDEX [IX_Vehicles_TenantId_VehicleTypeId] ON [veh].[Vehicles] ([TenantId], [VehicleTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Vehicles_Chassis] ON [veh].[Vehicles] ([TenantId], [ChassisNo]) WHERE [ChassisNo] IS NOT NULL AND [Status] <> ''Sold'' AND [Status] <> ''Transferred'' AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Vehicles_Engine] ON [veh].[Vehicles] ([TenantId], [EngineNo]) WHERE [EngineNo] IS NOT NULL AND [Status] <> ''Sold'' AND [Status] <> ''Transferred'' AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Vehicles_FuelCard] ON [veh].[Vehicles] ([TenantId], [FuelCardNumber]) WHERE [FuelCardNumber] IS NOT NULL AND [Status] IN (''Active'', ''Assigned'', ''UnderMaintenance'', ''TemporarilyUnavailable'') AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Vehicles_RegNo] ON [veh].[Vehicles] ([TenantId], [RegNoKey]) WHERE [Status] <> ''Sold'' AND [Status] <> ''Transferred'' AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920110501_InitialVehicles'
)
BEGIN
    INSERT INTO [veh].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260920110501_InitialVehicles', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    CREATE TABLE [veh].[VehicleAcquisitions] (
        [VehicleId] int NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SellerId] int NULL,
        [PurchasePrice] decimal(18,2) NULL,
        [AmountPaid] decimal(18,2) NULL,
        [PaymentMode] nvarchar(12) NULL,
        [PaymentReference] nvarchar(60) NULL,
        [RegistrationCost] decimal(18,2) NULL,
        [ModifiedBy] int NULL,
        [ModifiedOn] datetime2 NOT NULL,
        CONSTRAINT [PK_VehicleAcquisitions] PRIMARY KEY ([VehicleId]),
        CONSTRAINT [FK_VehicleAcquisitions_Vehicles_VehicleId] FOREIGN KEY ([VehicleId]) REFERENCES [veh].[Vehicles] ([VehicleId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    CREATE TABLE [veh].[VehicleFinanceAgreements] (
        [VehicleFinanceAgreementId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [VehicleId] int NOT NULL,
        [FinanceTypeId] int NOT NULL,
        [BankId] int NOT NULL,
        [AgreementNo] nvarchar(60) NOT NULL,
        [AgreementDate] date NOT NULL,
        [FinanceAmount] decimal(18,2) NOT NULL,
        [DownPayment] decimal(18,2) NOT NULL,
        [InstallmentAmount] decimal(18,2) NOT NULL,
        [Frequency] nvarchar(12) NOT NULL,
        [Tenure] int NOT NULL,
        [FirstDueDate] date NOT NULL,
        [MarkupRate] decimal(5,2) NULL,
        [ResidualAmount] decimal(18,2) NULL,
        [SecurityDeposit] decimal(18,2) NULL,
        [Status] nvarchar(10) NOT NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        [ModifiedBy] int NULL,
        [ModifiedOn] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_VehicleFinanceAgreements] PRIMARY KEY ([VehicleFinanceAgreementId]),
        CONSTRAINT [FK_VehicleFinanceAgreements_Vehicles_VehicleId] FOREIGN KEY ([VehicleId]) REFERENCES [veh].[Vehicles] ([VehicleId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    CREATE TABLE [veh].[VehicleInstallments] (
        [VehicleInstallmentId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [VehicleId] int NOT NULL,
        [VehicleFinanceAgreementId] int NOT NULL,
        [InstallmentNo] int NOT NULL,
        [DueDate] date NOT NULL,
        [ExpectedAmount] decimal(18,2) NOT NULL,
        [PaidAmount] decimal(18,2) NOT NULL,
        [PaidOn] date NULL,
        [IsResidual] bit NOT NULL,
        [Status] nvarchar(14) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_VehicleInstallments] PRIMARY KEY ([VehicleInstallmentId]),
        CONSTRAINT [FK_VehicleInstallments_VehicleFinanceAgreements_VehicleFinanceAgreementId] FOREIGN KEY ([VehicleFinanceAgreementId]) REFERENCES [veh].[VehicleFinanceAgreements] ([VehicleFinanceAgreementId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    CREATE TABLE [veh].[VehicleTransactions] (
        [VehicleTransactionId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [VehicleId] int NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [SubType] nvarchar(40) NULL,
        [Amount] decimal(18,2) NOT NULL,
        [TransactionDate] date NOT NULL,
        [PartnerId] int NULL,
        [Reference] nvarchar(120) NULL,
        [Source] nvarchar(20) NOT NULL,
        [IsSystemGenerated] bit NOT NULL,
        [AttachmentFileId] uniqueidentifier NULL,
        [ReversesTransactionId] int NULL,
        [InstallmentId] int NULL,
        [Reason] nvarchar(500) NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        CONSTRAINT [PK_VehicleTransactions] PRIMARY KEY ([VehicleTransactionId]),
        CONSTRAINT [FK_VehicleTransactions_VehicleInstallments_InstallmentId] FOREIGN KEY ([InstallmentId]) REFERENCES [veh].[VehicleInstallments] ([VehicleInstallmentId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_VehicleTransactions_VehicleTransactions_ReversesTransactionId] FOREIGN KEY ([ReversesTransactionId]) REFERENCES [veh].[VehicleTransactions] ([VehicleTransactionId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_VehicleTransactions_Vehicles_VehicleId] FOREIGN KEY ([VehicleId]) REFERENCES [veh].[Vehicles] ([VehicleId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    CREATE INDEX [IX_VehicleAcquisitions_TenantId] ON [veh].[VehicleAcquisitions] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    CREATE INDEX [IX_VehicleFinanceAgreements_VehicleId] ON [veh].[VehicleFinanceAgreements] ([VehicleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    CREATE UNIQUE INDEX [UX_VehicleFinance_BankAgreement] ON [veh].[VehicleFinanceAgreements] ([TenantId], [BankId], [AgreementNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_VehicleFinance_Open] ON [veh].[VehicleFinanceAgreements] ([TenantId], [VehicleId]) WHERE [Status] IN (''Draft'', ''Active'')');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    CREATE INDEX [IX_VehicleInstallments_TenantId_VehicleId_DueDate] ON [veh].[VehicleInstallments] ([TenantId], [VehicleId], [DueDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    CREATE UNIQUE INDEX [IX_VehicleInstallments_VehicleFinanceAgreementId_InstallmentNo] ON [veh].[VehicleInstallments] ([VehicleFinanceAgreementId], [InstallmentNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    CREATE INDEX [IX_VehicleTransactions_InstallmentId] ON [veh].[VehicleTransactions] ([InstallmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    CREATE INDEX [IX_VehicleTransactions_TenantId_VehicleId_TransactionDate] ON [veh].[VehicleTransactions] ([TenantId], [VehicleId], [TransactionDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    CREATE INDEX [IX_VehicleTransactions_TenantId_VehicleId_Type] ON [veh].[VehicleTransactions] ([TenantId], [VehicleId], [Type]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    CREATE INDEX [IX_VehicleTransactions_VehicleId] ON [veh].[VehicleTransactions] ([VehicleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_VehicleTransactions_Reversal] ON [veh].[VehicleTransactions] ([ReversesTransactionId]) WHERE [ReversesTransactionId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921094124_VehicleFinance'
)
BEGIN
    INSERT INTO [veh].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260921094124_VehicleFinance', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922094230_VehicleReceipts'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[veh].[VehicleTransactions]') AND [c].[name] = N'AttachmentFileId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [veh].[VehicleTransactions] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [veh].[VehicleTransactions] DROP COLUMN [AttachmentFileId];
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922094230_VehicleReceipts'
)
BEGIN
    ALTER TABLE [veh].[VehicleTransactions] ADD [ReceiptContentType] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922094230_VehicleReceipts'
)
BEGIN
    ALTER TABLE [veh].[VehicleTransactions] ADD [ReceiptFileName] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922094230_VehicleReceipts'
)
BEGIN
    ALTER TABLE [veh].[VehicleTransactions] ADD [ReceiptSha256] nvarchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922094230_VehicleReceipts'
)
BEGIN
    ALTER TABLE [veh].[VehicleTransactions] ADD [ReceiptSizeBytes] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922094230_VehicleReceipts'
)
BEGIN
    ALTER TABLE [veh].[VehicleTransactions] ADD [ReceiptStorageKey] nvarchar(300) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922094230_VehicleReceipts'
)
BEGIN
    INSERT INTO [veh].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260922094230_VehicleReceipts', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922110928_RecurringCharges'
)
BEGIN
    CREATE TABLE [veh].[VehicleRecurringCharges] (
        [VehicleRecurringChargeId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [VehicleId] int NOT NULL,
        [SeriesId] uniqueidentifier NOT NULL,
        [ChargeTypeId] int NOT NULL,
        [PayeeId] int NOT NULL,
        [ExpenseTypeId] int NOT NULL,
        [Amount] decimal(18,2) NULL,
        [AmountBasis] nvarchar(20) NOT NULL,
        [Frequency] nvarchar(12) NOT NULL,
        [DueDay] int NULL,
        [DueMonth] int NULL,
        [CustomIntervalDays] int NULL,
        [StartDate] date NOT NULL,
        [EndDate] date NULL,
        [OccurrenceCount] int NULL,
        [GeneratedCount] int NOT NULL,
        [PostingMode] nvarchar(14) NOT NULL,
        [GenerateLeadDays] int NOT NULL,
        [TaxWithholdingPercent] decimal(5,2) NULL,
        [NextDueDate] date NULL,
        [EffectiveTo] date NULL,
        [EndReason] nvarchar(500) NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        [ModifiedBy] int NULL,
        [ModifiedOn] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_VehicleRecurringCharges] PRIMARY KEY ([VehicleRecurringChargeId]),
        CONSTRAINT [FK_VehicleRecurringCharges_Vehicles_VehicleId] FOREIGN KEY ([VehicleId]) REFERENCES [veh].[Vehicles] ([VehicleId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922110928_RecurringCharges'
)
BEGIN
    CREATE TABLE [veh].[VehicleRecurringChargeEntries] (
        [VehicleRecurringChargeEntryId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [VehicleId] int NOT NULL,
        [VehicleRecurringChargeId] int NOT NULL,
        [PeriodKey] nvarchar(20) NOT NULL,
        [DueDate] date NOT NULL,
        [ExpectedAmount] decimal(18,2) NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [PaidAmount] decimal(18,2) NULL,
        [PaidOn] date NULL,
        [PaymentMode] nvarchar(12) NULL,
        [Reference] nvarchar(120) NULL,
        [Remarks] nvarchar(500) NULL,
        [TransactionId] int NULL,
        [WaiveReason] nvarchar(500) NULL,
        [ConfirmedBy] int NULL,
        [ConfirmedOn] datetime2 NULL,
        [CreatedOn] datetime2 NOT NULL,
        CONSTRAINT [PK_VehicleRecurringChargeEntries] PRIMARY KEY ([VehicleRecurringChargeEntryId]),
        CONSTRAINT [FK_VehicleRecurringChargeEntries_VehicleRecurringCharges_VehicleRecurringChargeId] FOREIGN KEY ([VehicleRecurringChargeId]) REFERENCES [veh].[VehicleRecurringCharges] ([VehicleRecurringChargeId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_VehicleRecurringChargeEntries_VehicleTransactions_TransactionId] FOREIGN KEY ([TransactionId]) REFERENCES [veh].[VehicleTransactions] ([VehicleTransactionId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922110928_RecurringCharges'
)
BEGIN
    CREATE INDEX [IX_VehicleRecurringChargeEntries_TenantId_VehicleId_Status_DueDate] ON [veh].[VehicleRecurringChargeEntries] ([TenantId], [VehicleId], [Status], [DueDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922110928_RecurringCharges'
)
BEGIN
    CREATE INDEX [IX_VehicleRecurringChargeEntries_TransactionId] ON [veh].[VehicleRecurringChargeEntries] ([TransactionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922110928_RecurringCharges'
)
BEGIN
    CREATE UNIQUE INDEX [UX_VehicleRecurringChargeEntries_Period] ON [veh].[VehicleRecurringChargeEntries] ([VehicleRecurringChargeId], [PeriodKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922110928_RecurringCharges'
)
BEGIN
    CREATE INDEX [IX_VehicleRecurringCharges_TenantId_NextDueDate] ON [veh].[VehicleRecurringCharges] ([TenantId], [NextDueDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922110928_RecurringCharges'
)
BEGIN
    CREATE INDEX [IX_VehicleRecurringCharges_TenantId_VehicleId_EffectiveTo] ON [veh].[VehicleRecurringCharges] ([TenantId], [VehicleId], [EffectiveTo]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922110928_RecurringCharges'
)
BEGIN
    CREATE INDEX [IX_VehicleRecurringCharges_VehicleId] ON [veh].[VehicleRecurringCharges] ([VehicleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922110928_RecurringCharges'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_VehicleRecurringCharges_Open] ON [veh].[VehicleRecurringCharges] ([TenantId], [SeriesId]) WHERE [EffectiveTo] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [veh].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922110928_RecurringCharges'
)
BEGIN
    INSERT INTO [veh].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260922110928_RecurringCharges', N'9.0.2');
END;

COMMIT;
GO

