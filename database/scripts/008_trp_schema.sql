IF OBJECT_ID(N'[trp].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'trp') IS NULL EXEC(N'CREATE SCHEMA [trp];');
    CREATE TABLE [trp].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924231350_InitialTrips'
)
BEGIN
    IF SCHEMA_ID(N'trp') IS NULL EXEC(N'CREATE SCHEMA [trp];');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924231350_InitialTrips'
)
BEGIN
    CREATE TABLE [trp].[IdempotencyRecords] (
        [IdempotencyRecordId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [Key] nvarchar(200) NOT NULL,
        [Route] nvarchar(200) NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        [ResponseStatusCode] int NOT NULL,
        [ResponseBody] nvarchar(max) NOT NULL,
        [ResponseContentType] nvarchar(100) NULL,
        [CreatedOn] datetime2 NOT NULL,
        CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([IdempotencyRecordId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924231350_InitialTrips'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IdempotencyRecords_TenantId_Key_Route] ON [trp].[IdempotencyRecords] ([TenantId], [Key], [Route]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924231350_InitialTrips'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924231350_InitialTrips', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924233622_Currencies'
)
BEGIN
    CREATE TABLE [trp].[Currencies] (
        [CurrencyId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CurrencyCode] nchar(3) NOT NULL,
        [CurrencyName] nvarchar(50) NOT NULL,
        [Symbol] nvarchar(5) NULL,
        [DecimalPlaces] tinyint NOT NULL,
        [IsBase] bit NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        CONSTRAINT [PK_Currencies] PRIMARY KEY ([CurrencyId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924233622_Currencies'
)
BEGIN
    CREATE TABLE [trp].[CurrencySettings] (
        [CurrencySettingsId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [BaseCurrencyCode] nchar(3) NOT NULL,
        [MultiCurrencyEnabled] bit NOT NULL,
        CONSTRAINT [PK_CurrencySettings] PRIMARY KEY ([CurrencySettingsId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924233622_Currencies'
)
BEGIN
    CREATE TABLE [trp].[ExchangeRates] (
        [ExchangeRateId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [FromCurrencyCode] nchar(3) NOT NULL,
        [ToCurrencyCode] nchar(3) NOT NULL,
        [RateDate] date NOT NULL,
        [Rate] decimal(18,6) NOT NULL,
        CONSTRAINT [PK_ExchangeRates] PRIMARY KEY ([ExchangeRateId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924233622_Currencies'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Currencies_TenantId] ON [trp].[Currencies] ([TenantId]) WHERE [IsBase] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924233622_Currencies'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Currencies_TenantId_CurrencyCode] ON [trp].[Currencies] ([TenantId], [CurrencyCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924233622_Currencies'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CurrencySettings_TenantId] ON [trp].[CurrencySettings] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924233622_Currencies'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ExchangeRates_TenantId_FromCurrencyCode_ToCurrencyCode_RateDate] ON [trp].[ExchangeRates] ([TenantId], [FromCurrencyCode], [ToCurrencyCode], [RateDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924233622_Currencies'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924233622_Currencies', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924235009_Customers'
)
BEGIN
    CREATE TABLE [trp].[Customers] (
        [CustomerId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerCode] nvarchar(20) NOT NULL,
        [CustomerName] nvarchar(200) NOT NULL,
        [ShortName] nvarchar(50) NULL,
        [AddressLine1] nvarchar(200) NOT NULL,
        [AddressLine2] nvarchar(200) NULL,
        [CountryId] int NOT NULL,
        [ProvinceState] nvarchar(100) NULL,
        [CityId] int NULL,
        [PostalCode] nvarchar(20) NULL,
        [Ntn] nvarchar(20) NULL,
        [Strn] nvarchar(30) NULL,
        [OtherRegistrationNo] nvarchar(50) NULL,
        [CurrencyCode] nchar(3) NOT NULL,
        [PaymentTermsDays] int NOT NULL,
        [CreditLimit] decimal(18,2) NULL,
        [Status] nvarchar(10) NOT NULL,
        [InactiveReason] nvarchar(500) NULL,
        [Remarks] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Customers] PRIMARY KEY ([CustomerId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924235009_Customers'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Customers_TenantId_CustomerCode] ON [trp].[Customers] ([TenantId], [CustomerCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924235009_Customers'
)
BEGIN
    CREATE INDEX [IX_Customers_TenantId_Status] ON [trp].[Customers] ([TenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924235009_Customers'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924235009_Customers', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925030826_CustomerContactsAndBillingAddresses'
)
BEGIN
    CREATE TABLE [trp].[CustomerBillingAddresses] (
        [CustomerBillingAddressId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] int NOT NULL,
        [AddressName] nvarchar(100) NOT NULL,
        [AddressLine1] nvarchar(200) NOT NULL,
        [AddressLine2] nvarchar(200) NULL,
        [CityId] int NOT NULL,
        [ProvinceState] nvarchar(100) NULL,
        [CountryId] int NOT NULL,
        [PostalCode] nvarchar(20) NULL,
        [Ntn] nvarchar(20) NULL,
        [Strn] nvarchar(30) NULL,
        [IsDefault] bit NOT NULL,
        [EffectiveFrom] date NOT NULL,
        [EffectiveTo] date NULL,
        [Status] nvarchar(10) NOT NULL,
        CONSTRAINT [PK_CustomerBillingAddresses] PRIMARY KEY ([CustomerBillingAddressId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925030826_CustomerContactsAndBillingAddresses'
)
BEGIN
    CREATE TABLE [trp].[CustomerContacts] (
        [CustomerContactId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] int NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Designation] nvarchar(100) NULL,
        [Mobile1] nvarchar(20) NOT NULL,
        [Mobile2] nvarchar(20) NULL,
        [Telephone] nvarchar(20) NULL,
        [Email] nvarchar(150) NULL,
        [AvailabilityTime] nvarchar(100) NULL,
        [Purpose] nvarchar(50) NULL,
        [IsPrimary] bit NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        CONSTRAINT [PK_CustomerContacts] PRIMARY KEY ([CustomerContactId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925030826_CustomerContactsAndBillingAddresses'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CustomerBillingAddresses_CustomerId] ON [trp].[CustomerBillingAddresses] ([CustomerId]) WHERE [IsDefault] = 1 AND [Status] = ''Active''');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925030826_CustomerContactsAndBillingAddresses'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerBillingAddresses_CustomerId_AddressName] ON [trp].[CustomerBillingAddresses] ([CustomerId], [AddressName]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925030826_CustomerContactsAndBillingAddresses'
)
BEGIN
    CREATE INDEX [IX_CustomerBillingAddresses_TenantId_CustomerId] ON [trp].[CustomerBillingAddresses] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925030826_CustomerContactsAndBillingAddresses'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CustomerContacts_CustomerId] ON [trp].[CustomerContacts] ([CustomerId]) WHERE [IsPrimary] = 1 AND [Status] = ''Active''');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925030826_CustomerContactsAndBillingAddresses'
)
BEGIN
    CREATE INDEX [IX_CustomerContacts_TenantId_CustomerId] ON [trp].[CustomerContacts] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925030826_CustomerContactsAndBillingAddresses'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925030826_CustomerContactsAndBillingAddresses', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925032241_CustomerBillingConfiguration'
)
BEGIN
    CREATE TABLE [trp].[CustomerBillingConfigurations] (
        [CustomerBillingConfigurationId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] int NOT NULL,
        [PaymentTermsDays] int NOT NULL,
        [CurrencyCode] nchar(3) NOT NULL,
        [DefaultBillingAddressId] bigint NULL,
        [DefaultInvoiceTemplateId] bigint NULL,
        [InvoiceNumberPrefix] nvarchar(10) NOT NULL,
        [PodRequired] bit NOT NULL,
        [EvidenceRequired] bit NOT NULL,
        [EvidencePageSize] int NOT NULL,
        [DuplicateReferenceBehaviour] nvarchar(10) NOT NULL,
        [CustomerReferenceRequired] bit NOT NULL,
        [StatementEmailContactId] bigint NULL,
        [EffectiveFrom] date NOT NULL,
        [EffectiveTo] date NULL,
        CONSTRAINT [PK_CustomerBillingConfigurations] PRIMARY KEY ([CustomerBillingConfigurationId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925032241_CustomerBillingConfiguration'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CustomerBillingConfigurations_CustomerId] ON [trp].[CustomerBillingConfigurations] ([CustomerId]) WHERE [EffectiveTo] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925032241_CustomerBillingConfiguration'
)
BEGIN
    CREATE INDEX [IX_CustomerBillingConfigurations_TenantId_CustomerId] ON [trp].[CustomerBillingConfigurations] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925032241_CustomerBillingConfiguration'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925032241_CustomerBillingConfiguration', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925035015_CustomerTaxRules'
)
BEGIN
    CREATE TABLE [trp].[CustomerTaxRules] (
        [CustomerTaxRuleId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] int NOT NULL,
        [TaxName] nvarchar(100) NOT NULL,
        [TaxNameKey] nvarchar(100) NOT NULL,
        [TaxCode] nvarchar(20) NOT NULL,
        [TaxType] nvarchar(12) NOT NULL,
        [TaxPercentage] decimal(9,4) NULL,
        [FixedAmount] decimal(18,2) NULL,
        [Applicable] bit NOT NULL,
        [CalculationBasis] nvarchar(20) NOT NULL,
        [Sequence] int NOT NULL,
        [EffectiveFrom] date NOT NULL,
        [EffectiveTo] date NULL,
        [Status] nvarchar(10) NOT NULL,
        [SupersedesRuleId] bigint NULL,
        [Remarks] nvarchar(500) NULL,
        CONSTRAINT [PK_CustomerTaxRules] PRIMARY KEY ([CustomerTaxRuleId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925035015_CustomerTaxRules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CustomerTaxRules_CustomerId_TaxNameKey_TaxCode] ON [trp].[CustomerTaxRules] ([CustomerId], [TaxNameKey], [TaxCode]) WHERE [EffectiveTo] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925035015_CustomerTaxRules'
)
BEGIN
    CREATE INDEX [IX_CustomerTaxRules_TenantId_CustomerId] ON [trp].[CustomerTaxRules] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925035015_CustomerTaxRules'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925035015_CustomerTaxRules', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925040244_CustomerInvoiceTemplates'
)
BEGIN
    CREATE TABLE [trp].[CustomerInvoiceTemplates] (
        [CustomerInvoiceTemplateId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] int NOT NULL,
        [TemplateName] nvarchar(100) NOT NULL,
        [Version] int NOT NULL,
        [TemplateType] nvarchar(20) NOT NULL,
        [TemplateReference] nvarchar(500) NOT NULL,
        [EffectiveFrom] date NOT NULL,
        [EffectiveTo] date NULL,
        [IsDefault] bit NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        CONSTRAINT [PK_CustomerInvoiceTemplates] PRIMARY KEY ([CustomerInvoiceTemplateId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925040244_CustomerInvoiceTemplates'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CustomerInvoiceTemplates_CustomerId] ON [trp].[CustomerInvoiceTemplates] ([CustomerId]) WHERE [IsDefault] = 1 AND [Status] = ''Active''');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925040244_CustomerInvoiceTemplates'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CustomerInvoiceTemplates_CustomerId_TemplateName] ON [trp].[CustomerInvoiceTemplates] ([CustomerId], [TemplateName]) WHERE [EffectiveTo] IS NULL AND [Status] = ''Active''');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925040244_CustomerInvoiceTemplates'
)
BEGIN
    CREATE INDEX [IX_CustomerInvoiceTemplates_TenantId_CustomerId] ON [trp].[CustomerInvoiceTemplates] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925040244_CustomerInvoiceTemplates'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925040244_CustomerInvoiceTemplates', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925042132_Cities'
)
BEGIN
    CREATE TABLE [trp].[Cities] (
        [CityId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CityName] nvarchar(100) NOT NULL,
        [Abbreviation] nvarchar(5) NOT NULL,
        [CountryId] int NOT NULL,
        [ProvinceState] nvarchar(100) NULL,
        [Status] nvarchar(10) NOT NULL,
        CONSTRAINT [PK_Cities] PRIMARY KEY ([CityId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925042132_Cities'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Cities_TenantId_Abbreviation] ON [trp].[Cities] ([TenantId], [Abbreviation]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925042132_Cities'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Cities_TenantId_CountryId_ProvinceState_CityName] ON [trp].[Cities] ([TenantId], [CountryId], [ProvinceState], [CityName]) WHERE [ProvinceState] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925042132_Cities'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925042132_Cities', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925043059_Routes'
)
BEGIN
    CREATE TABLE [trp].[Routes] (
        [RouteId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [RouteCode] nvarchar(30) NOT NULL,
        [RouteName] nvarchar(150) NOT NULL,
        [OriginCityId] int NOT NULL,
        [DestinationCityId] int NOT NULL,
        [IsRoundTrip] bit NOT NULL,
        [DistanceKm] decimal(9,2) NULL,
        [StandardDurationMin] int NULL,
        [Status] nvarchar(10) NOT NULL,
        [Remarks] nvarchar(1000) NULL,
        [IsLocked] bit NOT NULL,
        CONSTRAINT [PK_Routes] PRIMARY KEY ([RouteId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925043059_Routes'
)
BEGIN
    CREATE TABLE [trp].[RouteStops] (
        [RouteStopId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [RouteId] int NOT NULL,
        [CityId] int NOT NULL,
        [Sequence] int NOT NULL,
        [StopType] nvarchar(12) NOT NULL,
        [PlannedDurationMin] int NULL,
        [Remarks] nvarchar(500) NULL,
        CONSTRAINT [PK_RouteStops] PRIMARY KEY ([RouteStopId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925043059_Routes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Routes_TenantId_RouteCode] ON [trp].[Routes] ([TenantId], [RouteCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925043059_Routes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RouteStops_TenantId_RouteId_Sequence] ON [trp].[RouteStops] ([TenantId], [RouteId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925043059_Routes'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925043059_Routes', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925044725_TripConfigurations'
)
BEGIN
    CREATE TABLE [trp].[TripConfigurations] (
        [TripConfigurationId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] int NOT NULL,
        [TripCode] nvarchar(40) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [RouteId] int NOT NULL,
        [DirectionType] nvarchar(12) NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [Remarks] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TripConfigurations] PRIMARY KEY ([TripConfigurationId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925044725_TripConfigurations'
)
BEGIN
    CREATE TABLE [trp].[TripConfigurationStops] (
        [TripConfigurationStopId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [TripConfigurationId] bigint NOT NULL,
        [CityId] int NULL,
        [OtherLocation] nvarchar(200) NULL,
        [Sequence] int NOT NULL,
        [StopType] nvarchar(12) NOT NULL,
        CONSTRAINT [PK_TripConfigurationStops] PRIMARY KEY ([TripConfigurationStopId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925044725_TripConfigurations'
)
BEGIN
    CREATE TABLE [trp].[TripConfigurationVehicles] (
        [TripConfigurationVehicleId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [TripConfigurationId] bigint NOT NULL,
        [VehicleId] int NOT NULL,
        [EffectiveFrom] date NOT NULL,
        [EffectiveTo] date NULL,
        [Status] nvarchar(10) NOT NULL,
        [Remarks] nvarchar(500) NULL,
        CONSTRAINT [PK_TripConfigurationVehicles] PRIMARY KEY ([TripConfigurationVehicleId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925044725_TripConfigurations'
)
BEGIN
    CREATE INDEX [IX_TripConfigurations_TenantId_CustomerId] ON [trp].[TripConfigurations] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925044725_TripConfigurations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TripConfigurations_TenantId_CustomerId_TripCode] ON [trp].[TripConfigurations] ([TenantId], [CustomerId], [TripCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925044725_TripConfigurations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TripConfigurationStops_TenantId_TripConfigurationId_Sequence] ON [trp].[TripConfigurationStops] ([TenantId], [TripConfigurationId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925044725_TripConfigurations'
)
BEGIN
    CREATE INDEX [IX_TripConfigurationVehicles_TenantId_TripConfigurationId] ON [trp].[TripConfigurationVehicles] ([TenantId], [TripConfigurationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925044725_TripConfigurations'
)
BEGIN
    CREATE INDEX [IX_TripConfigurationVehicles_TenantId_VehicleId] ON [trp].[TripConfigurationVehicles] ([TenantId], [VehicleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925044725_TripConfigurations'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925044725_TripConfigurations', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925050619_TripRates'
)
BEGIN
    CREATE TABLE [trp].[TripRates] (
        [TripRateId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] int NOT NULL,
        [TripConfigurationId] bigint NOT NULL,
        [EffectiveFrom] date NOT NULL,
        [EffectiveTo] date NULL,
        [RateAmount] decimal(18,2) NOT NULL,
        [CurrencyCode] nchar(3) NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [Remarks] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TripRates] PRIMARY KEY ([TripRateId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925050619_TripRates'
)
BEGIN
    CREATE INDEX [IX_TripRates_TenantId_TripConfigurationId] ON [trp].[TripRates] ([TenantId], [TripConfigurationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925050619_TripRates'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TripRates_TripConfigurationId] ON [trp].[TripRates] ([TripConfigurationId]) WHERE [EffectiveTo] IS NULL AND [Status] = ''Active''');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925050619_TripRates'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925050619_TripRates', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925052421_Trips'
)
BEGIN
    CREATE TABLE [trp].[TripNumberCounters] (
        [TripNumberCounterId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [Year] int NOT NULL,
        [LastNumber] int NOT NULL,
        CONSTRAINT [PK_TripNumberCounters] PRIMARY KEY ([TripNumberCounterId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925052421_Trips'
)
BEGIN
    CREATE TABLE [trp].[Trips] (
        [TripId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [TripNumber] nvarchar(20) NOT NULL,
        [TripType] nvarchar(10) NOT NULL,
        [CustomerId] int NOT NULL,
        [CustomerTripReference] nvarchar(50) NULL,
        [TripConfigurationId] bigint NULL,
        [RouteId] int NULL,
        [VehicleId] int NOT NULL,
        [DriverId] int NOT NULL,
        [IsDriverOverridden] bit NOT NULL,
        [DriverOverrideReason] nvarchar(500) NULL,
        [TripDate] date NOT NULL,
        [PlannedStart] datetime2 NULL,
        [ActualStart] datetime2 NULL,
        [ActualEnd] datetime2 NULL,
        [StartOdometer] decimal(10,1) NULL,
        [EndOdometer] decimal(10,1) NULL,
        [TripRateId] bigint NULL,
        [TripRateAmount] decimal(18,2) NULL,
        [RateEffectiveFrom] date NULL,
        [RateEffectiveTo] date NULL,
        [RateSource] nvarchar(12) NOT NULL,
        [TripAmount] decimal(18,2) NULL,
        [CurrencyCode] nchar(3) NULL,
        [RateMissing] bit NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [IsActive] bit NOT NULL,
        [InvoiceId] bigint NULL,
        [Remarks] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Trips] PRIMARY KEY ([TripId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925052421_Trips'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TripNumberCounters_TenantId_Year] ON [trp].[TripNumberCounters] ([TenantId], [Year]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925052421_Trips'
)
BEGIN
    CREATE INDEX [IX_Trips_TenantId_CustomerId] ON [trp].[Trips] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925052421_Trips'
)
BEGIN
    CREATE INDEX [IX_Trips_TenantId_TripConfigurationId] ON [trp].[Trips] ([TenantId], [TripConfigurationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925052421_Trips'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Trips_TenantId_TripNumber] ON [trp].[Trips] ([TenantId], [TripNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925052421_Trips'
)
BEGIN
    CREATE INDEX [IX_Trips_TenantId_VehicleId] ON [trp].[Trips] ([TenantId], [VehicleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925052421_Trips'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925052421_Trips', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [FromCityId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [FromLocationType] nvarchar(10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [FromOtherLocationName] nvarchar(150) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [FromOtherLocationType] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [FromOtherNearestCityId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [IsRoundTrip] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [ToCityId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [ToLocationType] nvarchar(10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [ToOtherLocationName] nvarchar(150) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [ToOtherLocationType] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [ToOtherNearestCityId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    CREATE TABLE [trp].[TripStops] (
        [TripStopId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [TripId] bigint NOT NULL,
        [Sequence] int NOT NULL,
        [LocationType] nvarchar(10) NOT NULL,
        [CityId] int NULL,
        [OtherLocationType] nvarchar(20) NULL,
        [OtherLocationName] nvarchar(150) NULL,
        [OtherNearestCityId] int NULL,
        CONSTRAINT [PK_TripStops] PRIMARY KEY ([TripStopId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TripStops_TenantId_TripId_Sequence] ON [trp].[TripStops] ([TenantId], [TripId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925053941_OpenTrips'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925053941_OpenTrips', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925055200_TripDriverDefaultOverride'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[trp].[Trips]') AND [c].[name] = N'DriverId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [trp].[Trips] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [trp].[Trips] ALTER COLUMN [DriverId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925055200_TripDriverDefaultOverride'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [DefaultDriverId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925055200_TripDriverDefaultOverride'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925055200_TripDriverDefaultOverride', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925061147_TripLifecycle'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [CancelReason] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925061147_TripLifecycle'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [CompletionDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925061147_TripLifecycle'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [HeldFromStatus] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925061147_TripLifecycle'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [HoldReason] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925061147_TripLifecycle'
)
BEGIN
    ALTER TABLE [trp].[Trips] ADD [InactiveReason] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925061147_TripLifecycle'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925061147_TripLifecycle', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925063945_TripEventsDocumentsPodIssues'
)
BEGIN
    CREATE TABLE [trp].[TripDocuments] (
        [TripDocumentId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [TripId] bigint NOT NULL,
        [DocumentType] nvarchar(20) NOT NULL,
        [StorageKey] nvarchar(300) NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [OriginalFileName] nvarchar(260) NOT NULL,
        [UploadedBy] int NOT NULL,
        [UploadedAtUtc] datetime2 NOT NULL,
        [Source] nvarchar(10) NOT NULL,
        CONSTRAINT [PK_TripDocuments] PRIMARY KEY ([TripDocumentId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925063945_TripEventsDocumentsPodIssues'
)
BEGIN
    CREATE TABLE [trp].[TripEvents] (
        [TripEventId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [TripId] bigint NOT NULL,
        [EventType] nvarchar(20) NOT NULL,
        [EventDateTime] datetime2 NOT NULL,
        [CityId] int NULL,
        [LocationText] nvarchar(200) NULL,
        [Latitude] float NULL,
        [Longitude] float NULL,
        [Odometer] decimal(10,1) NULL,
        [UserId] int NOT NULL,
        [Remarks] nvarchar(500) NULL,
        [AttachmentId] bigint NULL,
        [Source] nvarchar(10) NOT NULL,
        [ClientEventId] uniqueidentifier NULL,
        CONSTRAINT [PK_TripEvents] PRIMARY KEY ([TripEventId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925063945_TripEventsDocumentsPodIssues'
)
BEGIN
    CREATE TABLE [trp].[TripIssues] (
        [TripIssueId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [TripId] bigint NOT NULL,
        [IssueType] nvarchar(20) NOT NULL,
        [Severity] nvarchar(10) NOT NULL,
        [Description] nvarchar(1000) NOT NULL,
        [PhotoDocumentId] bigint NULL,
        [ReportedBy] int NOT NULL,
        [ReportedAtUtc] datetime2 NOT NULL,
        [IsResolved] bit NOT NULL,
        [ResolvedBy] int NULL,
        [ResolvedAtUtc] datetime2 NULL,
        [ResolutionNotes] nvarchar(1000) NULL,
        CONSTRAINT [PK_TripIssues] PRIMARY KEY ([TripIssueId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925063945_TripEventsDocumentsPodIssues'
)
BEGIN
    CREATE TABLE [trp].[TripPODs] (
        [TripPODId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [TripId] bigint NOT NULL,
        [StorageKey] nvarchar(300) NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [OriginalFileName] nvarchar(260) NOT NULL,
        [UploadedBy] int NOT NULL,
        [UploadedAtUtc] datetime2 NOT NULL,
        [Source] nvarchar(10) NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [ApprovedBy] int NULL,
        [ApprovedAtUtc] datetime2 NULL,
        [RejectedReason] nvarchar(500) NULL,
        CONSTRAINT [PK_TripPODs] PRIMARY KEY ([TripPODId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925063945_TripEventsDocumentsPodIssues'
)
BEGIN
    CREATE INDEX [IX_TripDocuments_TenantId_TripId] ON [trp].[TripDocuments] ([TenantId], [TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925063945_TripEventsDocumentsPodIssues'
)
BEGIN
    CREATE INDEX [IX_TripEvents_TenantId_TripId_EventDateTime] ON [trp].[TripEvents] ([TenantId], [TripId], [EventDateTime]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925063945_TripEventsDocumentsPodIssues'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TripEvents_TripId_ClientEventId] ON [trp].[TripEvents] ([TripId], [ClientEventId]) WHERE [ClientEventId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925063945_TripEventsDocumentsPodIssues'
)
BEGIN
    CREATE INDEX [IX_TripIssues_TenantId_TripId] ON [trp].[TripIssues] ([TenantId], [TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925063945_TripEventsDocumentsPodIssues'
)
BEGIN
    CREATE INDEX [IX_TripPODs_TenantId_TripId] ON [trp].[TripPODs] ([TenantId], [TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925063945_TripEventsDocumentsPodIssues'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925063945_TripEventsDocumentsPodIssues', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925065704_TripRateHistory'
)
BEGIN
    CREATE TABLE [trp].[TripRateHistories] (
        [TripRateHistoryId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [TripId] bigint NOT NULL,
        [OldTripRateId] bigint NULL,
        [OldRateAmount] decimal(18,2) NULL,
        [OldRateSource] nvarchar(12) NOT NULL,
        [OldCurrencyCode] nchar(3) NULL,
        [NewTripRateId] bigint NULL,
        [NewRateAmount] decimal(18,2) NULL,
        [NewRateSource] nvarchar(12) NOT NULL,
        [NewCurrencyCode] nchar(3) NULL,
        [Action] nvarchar(20) NOT NULL,
        [Reason] nvarchar(500) NULL,
        [PerformedBy] int NOT NULL,
        [PerformedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_TripRateHistories] PRIMARY KEY ([TripRateHistoryId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925065704_TripRateHistory'
)
BEGIN
    CREATE INDEX [IX_TripRateHistories_TenantId_TripId] ON [trp].[TripRateHistories] ([TenantId], [TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925065704_TripRateHistory'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925065704_TripRateHistory', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925071726_FuelCards'
)
BEGIN
    CREATE TABLE [trp].[FuelCardAssignments] (
        [FuelCardAssignmentId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [FuelCardId] int NOT NULL,
        [VehicleId] int NULL,
        [DriverId] int NULL,
        [AssignedFrom] date NOT NULL,
        [AssignedTo] date NULL,
        [Reason] nvarchar(500) NULL,
        CONSTRAINT [PK_FuelCardAssignments] PRIMARY KEY ([FuelCardAssignmentId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925071726_FuelCards'
)
BEGIN
    CREATE TABLE [trp].[FuelCards] (
        [FuelCardId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CardNumber] nvarchar(30) NOT NULL,
        [FuelCardCompanyId] int NOT NULL,
        [VehicleId] int NULL,
        [DriverId] int NULL,
        [CardHolderName] nvarchar(100) NULL,
        [ExpiryDate] date NOT NULL,
        [MonthlyLimit] decimal(18,2) NULL,
        [Status] nvarchar(10) NOT NULL,
        [Remarks] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_FuelCards] PRIMARY KEY ([FuelCardId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925071726_FuelCards'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_FuelCardAssignments_FuelCardId] ON [trp].[FuelCardAssignments] ([FuelCardId]) WHERE [AssignedTo] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925071726_FuelCards'
)
BEGIN
    CREATE INDEX [IX_FuelCardAssignments_TenantId_FuelCardId] ON [trp].[FuelCardAssignments] ([TenantId], [FuelCardId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925071726_FuelCards'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FuelCards_TenantId_CardNumber] ON [trp].[FuelCards] ([TenantId], [CardNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925071726_FuelCards'
)
BEGIN
    CREATE INDEX [IX_FuelCards_TenantId_FuelCardCompanyId] ON [trp].[FuelCards] ([TenantId], [FuelCardCompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925071726_FuelCards'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925071726_FuelCards', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925073135_TripFuel'
)
BEGIN
    CREATE TABLE [trp].[TripFuels] (
        [TripFuelId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [TripId] bigint NOT NULL,
        [VehicleId] int NOT NULL,
        [FuelDateTime] datetime2 NOT NULL,
        [FuelType] nvarchar(10) NOT NULL,
        [Quantity] decimal(10,3) NOT NULL,
        [Rate] decimal(10,2) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Odometer] decimal(10,1) NULL,
        [StationName] nvarchar(150) NULL,
        [CityId] int NULL,
        [PaymentMethod] nvarchar(10) NOT NULL,
        [FuelCardId] int NULL,
        [OtherPaymentText] nvarchar(100) NULL,
        [AttachmentId] bigint NULL,
        [Remarks] nvarchar(500) NULL,
        [CurrencyCode] nchar(3) NOT NULL,
        [Source] nvarchar(10) NOT NULL,
        [IsVoided] bit NOT NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] int NULL,
        [VoidedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_TripFuels] PRIMARY KEY ([TripFuelId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925073135_TripFuel'
)
BEGIN
    CREATE INDEX [IX_TripFuels_TenantId_TripId] ON [trp].[TripFuels] ([TenantId], [TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925073135_TripFuel'
)
BEGIN
    CREATE INDEX [IX_TripFuels_TenantId_VehicleId_FuelDateTime] ON [trp].[TripFuels] ([TenantId], [VehicleId], [FuelDateTime]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925073135_TripFuel'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925073135_TripFuel', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925074200_TripExpense'
)
BEGIN
    CREATE TABLE [trp].[TripExpenses] (
        [TripExpenseId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [TripId] bigint NOT NULL,
        [ExpenseDate] datetime2 NOT NULL,
        [ExpenseTypeId] int NOT NULL,
        [OtherExpenseType] nvarchar(100) NULL,
        [Description] nvarchar(300) NULL,
        [Quantity] decimal(18,3) NULL,
        [Rate] decimal(18,2) NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Reference] nvarchar(50) NULL,
        [BusinessPartnerId] int NULL,
        [PaymentMethod] nvarchar(15) NOT NULL,
        [AttachmentId] bigint NULL,
        [ApprovalStatus] nvarchar(10) NOT NULL,
        [RejectionReason] nvarchar(500) NULL,
        [DecidedBy] int NULL,
        [DecidedAtUtc] datetime2 NULL,
        [Source] nvarchar(10) NOT NULL,
        [IsVoided] bit NOT NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] int NULL,
        [VoidedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_TripExpenses] PRIMARY KEY ([TripExpenseId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925074200_TripExpense'
)
BEGIN
    CREATE INDEX [IX_TripExpenses_TenantId_ApprovalStatus] ON [trp].[TripExpenses] ([TenantId], [ApprovalStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925074200_TripExpense'
)
BEGIN
    CREATE INDEX [IX_TripExpenses_TenantId_TripId] ON [trp].[TripExpenses] ([TenantId], [TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925074200_TripExpense'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925074200_TripExpense', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925074852_TripIncome'
)
BEGIN
    CREATE TABLE [trp].[TripIncomes] (
        [TripIncomeId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [TripId] bigint NOT NULL,
        [CustomerId] int NOT NULL,
        [IncomeTypeId] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [CurrencyCode] nchar(3) NOT NULL,
        [IncomeDate] date NOT NULL,
        [IsBillable] bit NOT NULL,
        [InvoiceLineId] bigint NULL,
        [Reference] nvarchar(50) NULL,
        [Remarks] nvarchar(500) NULL,
        [IsVoided] bit NOT NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] int NULL,
        [VoidedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_TripIncomes] PRIMARY KEY ([TripIncomeId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925074852_TripIncome'
)
BEGIN
    CREATE INDEX [IX_TripIncomes_TenantId_CustomerId_IsBillable] ON [trp].[TripIncomes] ([TenantId], [CustomerId], [IsBillable]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925074852_TripIncome'
)
BEGIN
    CREATE INDEX [IX_TripIncomes_TenantId_TripId] ON [trp].[TripIncomes] ([TenantId], [TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925074852_TripIncome'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925074852_TripIncome', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE TABLE [trp].[InvoiceAdjustments] (
        [InvoiceAdjustmentId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [AdjustmentMonth] nvarchar(30) NOT NULL,
        [AdjustmentAmount] decimal(18,2) NOT NULL,
        [AdjustmentNote] nvarchar(500) NOT NULL,
        [ReferenceInvoiceNo] nvarchar(30) NULL,
        [Sequence] int NOT NULL,
        CONSTRAINT [PK_InvoiceAdjustments] PRIMARY KEY ([InvoiceAdjustmentId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE TABLE [trp].[InvoiceHistories] (
        [InvoiceHistoryId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [FromStatus] nvarchar(10) NOT NULL,
        [ToStatus] nvarchar(10) NOT NULL,
        [Reason] nvarchar(500) NULL,
        [ChangedBy] int NOT NULL,
        [ChangedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_InvoiceHistories] PRIMARY KEY ([InvoiceHistoryId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE TABLE [trp].[InvoiceLines] (
        [InvoiceLineId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [TripId] bigint NULL,
        [LineType] nvarchar(10) NOT NULL,
        [TripNumber] nvarchar(20) NULL,
        [TripDate] date NULL,
        [TripType] nvarchar(10) NULL,
        [CustomerTripReference] nvarchar(50) NULL,
        [RouteId] int NULL,
        [RouteCode] nvarchar(30) NULL,
        [RouteLabel] nvarchar(300) NULL,
        [TripConfigurationId] bigint NULL,
        [TripCode] nvarchar(40) NULL,
        [VehicleId] int NULL,
        [VehicleRegNo] nvarchar(20) NULL,
        [DriverId] int NULL,
        [DriverName] nvarchar(150) NULL,
        [Description] nvarchar(300) NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [Rate] decimal(18,2) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [TripRateId] bigint NULL,
        [RateEffectiveFrom] date NULL,
        [RateEffectiveTo] date NULL,
        [RateSource] nvarchar(12) NULL,
        [CustomerId] int NOT NULL,
        [CustomerCode] nvarchar(20) NOT NULL,
        [CustomerName] nvarchar(200) NOT NULL,
        CONSTRAINT [PK_InvoiceLines] PRIMARY KEY ([InvoiceLineId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE TABLE [trp].[InvoiceReplacements] (
        [InvoiceReplacementId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [OldInvoiceId] bigint NOT NULL,
        [NewInvoiceId] bigint NOT NULL,
        [Reason] nvarchar(500) NULL,
        [CreatedBy] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_InvoiceReplacements] PRIMARY KEY ([InvoiceReplacementId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE TABLE [trp].[Invoices] (
        [InvoiceId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [InvoiceNumber] nvarchar(30) NOT NULL,
        [Version] int NOT NULL,
        [RootInvoiceId] bigint NOT NULL,
        [PreviousInvoiceId] bigint NULL,
        [ReplacedByInvoiceId] bigint NULL,
        [CustomerId] int NOT NULL,
        [PeriodFrom] date NOT NULL,
        [PeriodTo] date NOT NULL,
        [InvoiceDate] date NOT NULL,
        [DueDate] date NULL,
        [CustomerInvoiceTemplateId] bigint NULL,
        [TemplateVersion] int NULL,
        [CustomerBillingAddressId] bigint NULL,
        [BillToAddressName] nvarchar(100) NULL,
        [BillToAddressLine1] nvarchar(200) NULL,
        [BillToAddressLine2] nvarchar(200) NULL,
        [BillToCityId] int NULL,
        [BillToProvinceState] nvarchar(100) NULL,
        [BillToCountryId] int NULL,
        [BillToPostalCode] nvarchar(20) NULL,
        [BillToNtn] nvarchar(20) NULL,
        [BillToStrn] nvarchar(30) NULL,
        [CustomerCode] nvarchar(20) NOT NULL,
        [CustomerName] nvarchar(200) NOT NULL,
        [Ntn] nvarchar(20) NULL,
        [Strn] nvarchar(30) NULL,
        [CurrencyCode] nchar(3) NOT NULL,
        [PaymentTermsDays] int NOT NULL,
        [TotalTripAmount] decimal(18,2) NOT NULL,
        [TotalAdjustment] decimal(18,2) NOT NULL,
        [GrossAmount] decimal(18,2) NOT NULL,
        [TotalDeduction] decimal(18,2) NOT NULL,
        [NetAmount] decimal(18,2) NOT NULL,
        [PaidAmount] decimal(18,2) NOT NULL,
        [AdvanceAppliedAmount] decimal(18,2) NOT NULL,
        [WriteOffAmount] decimal(18,2) NOT NULL,
        [DiscountAmount] decimal(18,2) NOT NULL,
        [TransferredInAmount] decimal(18,2) NOT NULL,
        [TransferredOutAmount] decimal(18,2) NOT NULL,
        [CarryForwardInAmount] decimal(18,2) NOT NULL,
        [CarryForwardOutAmount] decimal(18,2) NOT NULL,
        [RefundedAmount] decimal(18,2) NOT NULL,
        [BalanceAmount] decimal(18,2) NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [PaymentStatus] nvarchar(15) NOT NULL,
        [IsActive] bit NOT NULL,
        [GeneratedBy] int NULL,
        [GeneratedOn] datetime2 NULL,
        [SubmittedBy] int NULL,
        [SubmittedOn] date NULL,
        [SubmissionChannel] nvarchar(10) NULL,
        [CancelledBy] int NULL,
        [CancelledOn] datetime2 NULL,
        [CancelReason] nvarchar(500) NULL,
        [RegenerationReason] nvarchar(500) NULL,
        [RegeneratedBy] int NULL,
        [RegeneratedOn] datetime2 NULL,
        [PODRuleApplied] bit NULL,
        [EvidencePageSize] int NULL,
        [Remarks] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Invoices] PRIMARY KEY ([InvoiceId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE TABLE [trp].[InvoiceTaxLines] (
        [InvoiceTaxLineId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [CustomerTaxRuleId] bigint NULL,
        [TaxName] nvarchar(100) NOT NULL,
        [TaxCode] nvarchar(20) NOT NULL,
        [TaxType] nvarchar(12) NOT NULL,
        [TaxPercentage] decimal(9,4) NULL,
        [FixedAmount] decimal(18,2) NULL,
        [CalculationBasis] nvarchar(20) NOT NULL,
        [Sequence] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_InvoiceTaxLines] PRIMARY KEY ([InvoiceTaxLineId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE TABLE [trp].[InvoiceTripLinks] (
        [InvoiceTripLinkId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [TripId] bigint NOT NULL,
        [InvoiceLineId] bigint NOT NULL,
        [IsActive] bit NOT NULL,
        [LinkedOn] datetime2 NOT NULL,
        [UnlinkedOn] datetime2 NULL,
        [UnlinkReason] nvarchar(500) NULL,
        CONSTRAINT [PK_InvoiceTripLinks] PRIMARY KEY ([InvoiceTripLinkId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE INDEX [IX_InvoiceAdjustments_TenantId_InvoiceId] ON [trp].[InvoiceAdjustments] ([TenantId], [InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE INDEX [IX_InvoiceHistories_TenantId_InvoiceId] ON [trp].[InvoiceHistories] ([TenantId], [InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE INDEX [IX_InvoiceLines_TenantId_InvoiceId] ON [trp].[InvoiceLines] ([TenantId], [InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE INDEX [IX_InvoiceLines_TenantId_TripId] ON [trp].[InvoiceLines] ([TenantId], [TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE INDEX [IX_InvoiceReplacements_TenantId_NewInvoiceId] ON [trp].[InvoiceReplacements] ([TenantId], [NewInvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE INDEX [IX_InvoiceReplacements_TenantId_OldInvoiceId] ON [trp].[InvoiceReplacements] ([TenantId], [OldInvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE INDEX [IX_Invoices_TenantId_CustomerId_IsActive_PeriodFrom_PeriodTo] ON [trp].[Invoices] ([TenantId], [CustomerId], [IsActive], [PeriodFrom], [PeriodTo]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Invoices_TenantId_InvoiceNumber] ON [trp].[Invoices] ([TenantId], [InvoiceNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE INDEX [IX_InvoiceTaxLines_TenantId_InvoiceId] ON [trp].[InvoiceTaxLines] ([TenantId], [InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    CREATE INDEX [IX_InvoiceTripLinks_TenantId_InvoiceId] ON [trp].[InvoiceTripLinks] ([TenantId], [InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_InvoiceTripLinks_TripId] ON [trp].[InvoiceTripLinks] ([TripId]) WHERE [IsActive] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN

    EXEC(N'
    CREATE TRIGGER [trp].[TR_InvoiceLines_Immutable] ON [trp].[InvoiceLines]
    INSTEAD OF UPDATE, DELETE
    AS
    BEGIN
        SET NOCOUNT ON;
        THROW 51000, ''Invoice lines are immutable: they cannot be changed or deleted.'', 1;
    END')
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092855_InvoiceSchema'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925092855_InvoiceSchema', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925102627_InvoiceCreation'
)
BEGIN
    ALTER TABLE [trp].[InvoiceTaxLines] ADD [Applicable] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925102627_InvoiceCreation'
)
BEGIN
    CREATE TABLE [trp].[InvoiceNumberCounters] (
        [InvoiceNumberCounterId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [Year] int NOT NULL,
        [LastNumber] int NOT NULL,
        CONSTRAINT [PK_InvoiceNumberCounters] PRIMARY KEY ([InvoiceNumberCounterId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925102627_InvoiceCreation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InvoiceNumberCounters_TenantId_Year] ON [trp].[InvoiceNumberCounters] ([TenantId], [Year]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925102627_InvoiceCreation'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925102627_InvoiceCreation', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925104609_InvoiceDocuments'
)
BEGIN
    CREATE TABLE [trp].[InvoiceDocuments] (
        [InvoiceDocumentId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [DocumentType] nvarchar(20) NOT NULL,
        [Version] int NOT NULL,
        [StorageKey] nvarchar(300) NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [OriginalFileName] nvarchar(260) NOT NULL,
        [GeneratedBy] int NOT NULL,
        [GeneratedAtUtc] datetime2 NOT NULL,
        [IsCurrent] bit NOT NULL,
        CONSTRAINT [PK_InvoiceDocuments] PRIMARY KEY ([InvoiceDocumentId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925104609_InvoiceDocuments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_InvoiceDocuments_InvoiceId] ON [trp].[InvoiceDocuments] ([InvoiceId]) WHERE [IsCurrent] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925104609_InvoiceDocuments'
)
BEGIN
    CREATE INDEX [IX_InvoiceDocuments_TenantId_InvoiceId] ON [trp].[InvoiceDocuments] ([TenantId], [InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925104609_InvoiceDocuments'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925104609_InvoiceDocuments', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925110628_InvoiceEvidence'
)
BEGIN
    CREATE TABLE [trp].[InvoiceEvidences] (
        [InvoiceEvidenceId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [EvidenceVersion] int NOT NULL,
        [GroupBy] nvarchar(20) NOT NULL,
        [PageSize] int NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [PageCount] int NOT NULL,
        [LineCount] int NOT NULL,
        [StorageKey] nvarchar(300) NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [OriginalFileName] nvarchar(260) NOT NULL,
        [GeneratedBy] int NULL,
        [GeneratedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_InvoiceEvidences] PRIMARY KEY ([InvoiceEvidenceId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925110628_InvoiceEvidence'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InvoiceEvidences_InvoiceId_EvidenceVersion] ON [trp].[InvoiceEvidences] ([InvoiceId], [EvidenceVersion]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925110628_InvoiceEvidence'
)
BEGIN
    CREATE INDEX [IX_InvoiceEvidences_TenantId_InvoiceId] ON [trp].[InvoiceEvidences] ([TenantId], [InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925110628_InvoiceEvidence'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925110628_InvoiceEvidence', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115624_CustomerLedger'
)
BEGIN
    CREATE TABLE [trp].[CustomerBalances] (
        [CustomerBalanceId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] int NOT NULL,
        [CurrencyCode] nchar(3) NOT NULL,
        [BalanceAmount] decimal(18,2) NOT NULL,
        [LastEntryId] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_CustomerBalances] PRIMARY KEY ([CustomerBalanceId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115624_CustomerLedger'
)
BEGIN
    CREATE TABLE [trp].[CustomerLedgerEntries] (
        [CustomerLedgerEntryId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [EntryNumber] nvarchar(20) NOT NULL,
        [CustomerId] int NOT NULL,
        [InvoiceId] bigint NULL,
        [TripId] bigint NULL,
        [EntryType] nvarchar(30) NOT NULL,
        [EntryDate] date NOT NULL,
        [PostedOn] datetime2 NOT NULL,
        [DebitAmount] decimal(18,2) NOT NULL,
        [CreditAmount] decimal(18,2) NOT NULL,
        [CurrencyCode] nchar(3) NOT NULL,
        [SourceType] nvarchar(30) NOT NULL,
        [SourceId] bigint NOT NULL,
        [ReversesEntryId] bigint NULL,
        [DocumentNo] nvarchar(30) NOT NULL,
        [Narration] nvarchar(300) NOT NULL,
        [CustomerSeq] bigint NOT NULL,
        [IsSystemGenerated] bit NOT NULL,
        [CreatedBy] int NOT NULL,
        CONSTRAINT [PK_CustomerLedgerEntries] PRIMARY KEY ([CustomerLedgerEntryId]),
        CONSTRAINT [CK_CustomerLedgerEntries_OneOfDebitCredit] CHECK (([DebitAmount] > 0 AND [CreditAmount] = 0) OR ([CreditAmount] > 0 AND [DebitAmount] = 0))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115624_CustomerLedger'
)
BEGIN
    CREATE TABLE [trp].[CustomerLedgerSequenceCounters] (
        [CustomerLedgerSequenceCounterId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] int NOT NULL,
        [LastSeq] bigint NOT NULL,
        CONSTRAINT [PK_CustomerLedgerSequenceCounters] PRIMARY KEY ([CustomerLedgerSequenceCounterId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115624_CustomerLedger'
)
BEGIN
    CREATE TABLE [trp].[LedgerNumberCounters] (
        [LedgerNumberCounterId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [Year] int NOT NULL,
        [LastNumber] int NOT NULL,
        CONSTRAINT [PK_LedgerNumberCounters] PRIMARY KEY ([LedgerNumberCounterId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115624_CustomerLedger'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerBalances_TenantId_CustomerId_CurrencyCode] ON [trp].[CustomerBalances] ([TenantId], [CustomerId], [CurrencyCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115624_CustomerLedger'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerLedgerEntries_SourceType_SourceId_EntryType] ON [trp].[CustomerLedgerEntries] ([SourceType], [SourceId], [EntryType]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115624_CustomerLedger'
)
BEGIN
    CREATE INDEX [IX_CustomerLedgerEntries_TenantId_CustomerId_CustomerSeq] ON [trp].[CustomerLedgerEntries] ([TenantId], [CustomerId], [CustomerSeq]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115624_CustomerLedger'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerLedgerEntries_TenantId_EntryNumber] ON [trp].[CustomerLedgerEntries] ([TenantId], [EntryNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115624_CustomerLedger'
)
BEGIN
    CREATE INDEX [IX_CustomerLedgerEntries_TenantId_InvoiceId] ON [trp].[CustomerLedgerEntries] ([TenantId], [InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115624_CustomerLedger'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerLedgerSequenceCounters_TenantId_CustomerId] ON [trp].[CustomerLedgerSequenceCounters] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115624_CustomerLedger'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LedgerNumberCounters_TenantId_Year] ON [trp].[LedgerNumberCounters] ([TenantId], [Year]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115624_CustomerLedger'
)
BEGIN

    EXEC(N'
    CREATE TRIGGER [trp].[TR_CustomerLedgerEntries_Immutable] ON [trp].[CustomerLedgerEntries]
    INSTEAD OF UPDATE, DELETE
    AS
    BEGIN
        SET NOCOUNT ON;
        THROW 51000, ''Customer ledger entries are immutable: they cannot be changed or deleted.'', 1;
    END')
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115624_CustomerLedger'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925115624_CustomerLedger', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925121717_PaymentReceipts'
)
BEGIN
    CREATE TABLE [trp].[BankCashAccounts] (
        [BankCashAccountId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [AccountTitle] nvarchar(200) NOT NULL,
        [BankName] nvarchar(200) NOT NULL,
        [BranchName] nvarchar(200) NOT NULL,
        [AccountNumberLast4] nvarchar(4) NOT NULL,
        [CurrencyCode] nchar(3) NOT NULL,
        [IsActive] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_BankCashAccounts] PRIMARY KEY ([BankCashAccountId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925121717_PaymentReceipts'
)
BEGIN
    CREATE TABLE [trp].[CustomerReceipts] (
        [CustomerReceiptId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [ReceiptNumber] nvarchar(20) NOT NULL,
        [CustomerId] int NOT NULL,
        [ReceiptType] nvarchar(20) NOT NULL,
        [ReceiptDate] date NOT NULL,
        [ReceiptAmount] decimal(18,2) NOT NULL,
        [CurrencyCode] nchar(3) NOT NULL,
        [PaymentMethod] nvarchar(20) NOT NULL,
        [BankCashAccountId] bigint NOT NULL,
        [InstrumentNo] nvarchar(50) NOT NULL,
        [InstrumentDate] date NULL,
        [DrawnOnBank] nvarchar(100) NULL,
        [PaymentReference] nvarchar(100) NULL,
        [AttachmentDocumentId] bigint NULL,
        [Status] nvarchar(20) NOT NULL,
        [Remarks] nvarchar(500) NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_CustomerReceipts] PRIMARY KEY ([CustomerReceiptId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925121717_PaymentReceipts'
)
BEGIN
    CREATE TABLE [trp].[InvoicePayments] (
        [InvoicePaymentId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerReceiptId] bigint NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [PaymentDate] date NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [PaymentMethod] nvarchar(20) NOT NULL,
        [PaymentReference] nvarchar(100) NULL,
        [BankCashAccountId] bigint NOT NULL,
        [LedgerEntryId] bigint NULL,
        [Status] nvarchar(20) NOT NULL,
        [Remarks] nvarchar(500) NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        CONSTRAINT [PK_InvoicePayments] PRIMARY KEY ([InvoicePaymentId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925121717_PaymentReceipts'
)
BEGIN
    CREATE TABLE [trp].[ReceiptNumberCounters] (
        [ReceiptNumberCounterId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [Year] int NOT NULL,
        [LastNumber] int NOT NULL,
        CONSTRAINT [PK_ReceiptNumberCounters] PRIMARY KEY ([ReceiptNumberCounterId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925121717_PaymentReceipts'
)
BEGIN
    CREATE INDEX [IX_BankCashAccounts_TenantId] ON [trp].[BankCashAccounts] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925121717_PaymentReceipts'
)
BEGIN
    CREATE INDEX [IX_CustomerReceipts_TenantId_CustomerId] ON [trp].[CustomerReceipts] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925121717_PaymentReceipts'
)
BEGIN
    CREATE INDEX [IX_CustomerReceipts_TenantId_CustomerId_InstrumentNo] ON [trp].[CustomerReceipts] ([TenantId], [CustomerId], [InstrumentNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925121717_PaymentReceipts'
)
BEGIN
    CREATE INDEX [IX_InvoicePayments_TenantId_CustomerReceiptId] ON [trp].[InvoicePayments] ([TenantId], [CustomerReceiptId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925121717_PaymentReceipts'
)
BEGIN
    CREATE INDEX [IX_InvoicePayments_TenantId_InvoiceId] ON [trp].[InvoicePayments] ([TenantId], [InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925121717_PaymentReceipts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ReceiptNumberCounters_TenantId_Year] ON [trp].[ReceiptNumberCounters] ([TenantId], [Year]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925121717_PaymentReceipts'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925121717_PaymentReceipts', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925123505_PaymentReversal'
)
BEGIN
    ALTER TABLE [trp].[InvoicePayments] ADD [ReversalReason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925123505_PaymentReversal'
)
BEGIN
    ALTER TABLE [trp].[InvoicePayments] ADD [ReversedBy] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925123505_PaymentReversal'
)
BEGIN
    ALTER TABLE [trp].[InvoicePayments] ADD [ReversedOn] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925123505_PaymentReversal'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925123505_PaymentReversal', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925125105_InvoiceSettlements'
)
BEGIN
    CREATE TABLE [trp].[InvoiceSettlements] (
        [InvoiceSettlementId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [SettlementNumber] nvarchar(20) NOT NULL,
        [SettlementType] nvarchar(20) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [SettlementDate] date NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [LedgerEntryId] bigint NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        [ReversalReason] nvarchar(500) NULL,
        [ReversedBy] int NULL,
        [ReversedOn] datetime2 NULL,
        CONSTRAINT [PK_InvoiceSettlements] PRIMARY KEY ([InvoiceSettlementId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925125105_InvoiceSettlements'
)
BEGIN
    CREATE TABLE [trp].[SettlementNumberCounters] (
        [SettlementNumberCounterId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [Year] int NOT NULL,
        [LastNumber] int NOT NULL,
        CONSTRAINT [PK_SettlementNumberCounters] PRIMARY KEY ([SettlementNumberCounterId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925125105_InvoiceSettlements'
)
BEGIN
    CREATE INDEX [IX_InvoiceSettlements_TenantId_InvoiceId] ON [trp].[InvoiceSettlements] ([TenantId], [InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925125105_InvoiceSettlements'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SettlementNumberCounters_TenantId_Year] ON [trp].[SettlementNumberCounters] ([TenantId], [Year]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925125105_InvoiceSettlements'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925125105_InvoiceSettlements', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925133640_CustomerAdvances'
)
BEGIN
    CREATE TABLE [trp].[AdvanceNumberCounters] (
        [AdvanceNumberCounterId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [Year] int NOT NULL,
        [LastNumber] int NOT NULL,
        CONSTRAINT [PK_AdvanceNumberCounters] PRIMARY KEY ([AdvanceNumberCounterId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925133640_CustomerAdvances'
)
BEGIN
    CREATE TABLE [trp].[CustomerAdvances] (
        [CustomerAdvanceId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [AdvanceNumber] nvarchar(20) NOT NULL,
        [CustomerReceiptId] bigint NOT NULL,
        [CustomerId] int NOT NULL,
        [TripId] bigint NOT NULL,
        [AdvanceDate] date NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [AppliedAmount] decimal(18,2) NOT NULL,
        [RefundedAmount] decimal(18,2) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [LedgerEntryId] bigint NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        [MovedFromTripId] bigint NULL,
        [MoveReason] nvarchar(500) NULL,
        [MovedBy] int NULL,
        [MovedOn] datetime2 NULL,
        [RefundReason] nvarchar(500) NULL,
        [RefundedBy] int NULL,
        [RefundedOn] datetime2 NULL,
        [RefundLedgerEntryId] bigint NULL,
        [ReversalReason] nvarchar(500) NULL,
        [ReversedBy] int NULL,
        [ReversedOn] datetime2 NULL,
        CONSTRAINT [PK_CustomerAdvances] PRIMARY KEY ([CustomerAdvanceId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925133640_CustomerAdvances'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AdvanceNumberCounters_TenantId_Year] ON [trp].[AdvanceNumberCounters] ([TenantId], [Year]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925133640_CustomerAdvances'
)
BEGIN
    CREATE INDEX [IX_CustomerAdvances_TenantId_CustomerId] ON [trp].[CustomerAdvances] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925133640_CustomerAdvances'
)
BEGIN
    CREATE INDEX [IX_CustomerAdvances_TenantId_TripId] ON [trp].[CustomerAdvances] ([TenantId], [TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925133640_CustomerAdvances'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925133640_CustomerAdvances', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925142130_PaymentTransfers'
)
BEGIN
    CREATE TABLE [trp].[InvoicePaymentTransfers] (
        [InvoicePaymentTransferId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [OldInvoiceId] bigint NOT NULL,
        [NewInvoiceId] bigint NOT NULL,
        [TransferNumber] nvarchar(20) NOT NULL,
        [SourceKind] nvarchar(30) NOT NULL,
        [SourceId] bigint NOT NULL,
        [AmountTransferred] decimal(18,2) NOT NULL,
        [TransferDate] date NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [TransferredBy] int NOT NULL,
        [TransferredOn] datetime2 NOT NULL,
        [LedgerEntryOutId] bigint NOT NULL,
        [LedgerEntryInId] bigint NOT NULL,
        CONSTRAINT [PK_InvoicePaymentTransfers] PRIMARY KEY ([InvoicePaymentTransferId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925142130_PaymentTransfers'
)
BEGIN
    CREATE TABLE [trp].[TransferNumberCounters] (
        [TransferNumberCounterId] int NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [Year] int NOT NULL,
        [LastNumber] int NOT NULL,
        CONSTRAINT [PK_TransferNumberCounters] PRIMARY KEY ([TransferNumberCounterId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925142130_PaymentTransfers'
)
BEGIN
    CREATE INDEX [IX_InvoicePaymentTransfers_TenantId_NewInvoiceId] ON [trp].[InvoicePaymentTransfers] ([TenantId], [NewInvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925142130_PaymentTransfers'
)
BEGIN
    CREATE INDEX [IX_InvoicePaymentTransfers_TenantId_OldInvoiceId] ON [trp].[InvoicePaymentTransfers] ([TenantId], [OldInvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925142130_PaymentTransfers'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TransferNumberCounters_TenantId_Year] ON [trp].[TransferNumberCounters] ([TenantId], [Year]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925142130_PaymentTransfers'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925142130_PaymentTransfers', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143153_CustomerCredit'
)
BEGIN
    CREATE TABLE [trp].[CustomerRefunds] (
        [CustomerRefundId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [RefundDate] date NOT NULL,
        [PaymentMethod] nvarchar(20) NOT NULL,
        [BankCashAccountId] bigint NOT NULL,
        [PaymentReference] nvarchar(100) NULL,
        [Reason] nvarchar(500) NOT NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        [LedgerEntryId] bigint NOT NULL,
        CONSTRAINT [PK_CustomerRefunds] PRIMARY KEY ([CustomerRefundId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143153_CustomerCredit'
)
BEGIN
    CREATE TABLE [trp].[InvoiceCreditCarryForwards] (
        [InvoiceCreditCarryForwardId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [SourceInvoiceId] bigint NOT NULL,
        [TargetInvoiceId] bigint NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [CarryForwardDate] date NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [CreatedBy] int NOT NULL,
        [CreatedOn] datetime2 NOT NULL,
        [LedgerEntryOutId] bigint NOT NULL,
        [LedgerEntryInId] bigint NOT NULL,
        CONSTRAINT [PK_InvoiceCreditCarryForwards] PRIMARY KEY ([InvoiceCreditCarryForwardId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143153_CustomerCredit'
)
BEGIN
    CREATE INDEX [IX_CustomerRefunds_TenantId_InvoiceId] ON [trp].[CustomerRefunds] ([TenantId], [InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143153_CustomerCredit'
)
BEGIN
    CREATE INDEX [IX_InvoiceCreditCarryForwards_TenantId_SourceInvoiceId] ON [trp].[InvoiceCreditCarryForwards] ([TenantId], [SourceInvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143153_CustomerCredit'
)
BEGIN
    CREATE INDEX [IX_InvoiceCreditCarryForwards_TenantId_TargetInvoiceId] ON [trp].[InvoiceCreditCarryForwards] ([TenantId], [TargetInvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925143153_CustomerCredit'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925143153_CustomerCredit', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925145126_LedgerReconciliationAndPeriodLock'
)
BEGIN
    CREATE TABLE [trp].[LedgerPeriods] (
        [LedgerPeriodId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [YearMonth] nvarchar(6) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [ClosedBy] int NULL,
        [ClosedOn] datetime2 NULL,
        [CloseReason] nvarchar(500) NULL,
        [ReopenedBy] int NULL,
        [ReopenedOn] datetime2 NULL,
        [ReopenReason] nvarchar(500) NULL,
        CONSTRAINT [PK_LedgerPeriods] PRIMARY KEY ([LedgerPeriodId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925145126_LedgerReconciliationAndPeriodLock'
)
BEGIN
    CREATE TABLE [trp].[LedgerReconciliationMismatches] (
        [LedgerReconciliationMismatchId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [LedgerReconciliationRunId] bigint NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [InvoiceNumber] nvarchar(max) NOT NULL,
        [LedgerBalance] decimal(18,2) NOT NULL,
        [InvoiceBalance] decimal(18,2) NOT NULL,
        [Difference] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_LedgerReconciliationMismatches] PRIMARY KEY ([LedgerReconciliationMismatchId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925145126_LedgerReconciliationAndPeriodLock'
)
BEGIN
    CREATE TABLE [trp].[LedgerReconciliationRuns] (
        [LedgerReconciliationRunId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [RunOn] datetime2 NOT NULL,
        [InvoicesChecked] int NOT NULL,
        [MismatchCount] int NOT NULL,
        CONSTRAINT [PK_LedgerReconciliationRuns] PRIMARY KEY ([LedgerReconciliationRunId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925145126_LedgerReconciliationAndPeriodLock'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LedgerPeriods_TenantId_YearMonth] ON [trp].[LedgerPeriods] ([TenantId], [YearMonth]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925145126_LedgerReconciliationAndPeriodLock'
)
BEGIN
    CREATE INDEX [IX_LedgerReconciliationMismatches_TenantId_LedgerReconciliationRunId] ON [trp].[LedgerReconciliationMismatches] ([TenantId], [LedgerReconciliationRunId]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925145126_LedgerReconciliationAndPeriodLock'
)
BEGIN
    CREATE INDEX [IX_LedgerReconciliationRuns_TenantId_RunOn] ON [trp].[LedgerReconciliationRuns] ([TenantId], [RunOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925145126_LedgerReconciliationAndPeriodLock'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925145126_LedgerReconciliationAndPeriodLock', N'9.0.2');
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925170315_ReportExportJobs'
)
BEGIN
    CREATE TABLE [trp].[ReportExportJobs] (
        [ReportExportJobId] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [FiltersJson] nvarchar(2000) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [StorageKey] nvarchar(300) NULL,
        [Sha256] nvarchar(max) NULL,
        [SizeBytes] bigint NULL,
        [ContentType] nvarchar(100) NULL,
        [OriginalFileName] nvarchar(260) NULL,
        [RequestedBy] int NOT NULL,
        [RequestedByName] nvarchar(200) NOT NULL,
        [RequestedOn] datetime2 NOT NULL,
        [CompletedOn] datetime2 NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        CONSTRAINT [PK_ReportExportJobs] PRIMARY KEY ([ReportExportJobId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925170315_ReportExportJobs'
)
BEGIN
    CREATE INDEX [IX_ReportExportJobs_TenantId_Status] ON [trp].[ReportExportJobs] ([TenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [trp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925170315_ReportExportJobs'
)
BEGIN
    INSERT INTO [trp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925170315_ReportExportJobs', N'9.0.2');
END;

COMMIT;
GO

