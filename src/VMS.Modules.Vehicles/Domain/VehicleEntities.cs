using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Common;

namespace VMS.Modules.Vehicles.Domain;

/// <summary>
/// The vehicle master (FSD §15, §16). Never deleted once it has a transaction (BR-VH-023). Category and counterparty here are
/// convenience copies of the open relation and are changed only together with it (BR-VH-001); no total is stored on the vehicle (BR-VH-003).
/// </summary>
internal class Vehicle : ITenantScopedEntity, IAuditRooted
{
    public int VehicleId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("Vehicle", VehicleId.ToString());

    public string VehicleCode { get; set; } = string.Empty;

    /// <summary>As entered, in capitals: <c>LES-1234</c>.</summary>
    public string RegistrationNo { get; set; } = string.Empty;
    /// <summary>Capitals with spaces and dashes removed, so <c>LES-1234</c> and <c>LES 1234</c> are the same vehicle (BR-VH-017).</summary>
    [NotAudited] public string RegNoKey { get; set; } = string.Empty;
    public int? RegistrationCityId { get; set; }
    public string? ChassisNo { get; set; }
    public string? EngineNo { get; set; }
    public int VehicleTypeId { get; set; }
    public int MakeId { get; set; }
    public string Model { get; set; } = string.Empty;
    public int? ManufacturingYear { get; set; }
    public string? Colour { get; set; }

    public string FuelType { get; set; } = string.Empty;
    public decimal? TankCapacity { get; set; }
    public decimal? LoadCapacity { get; set; }
    public string? CapacityUnit { get; set; }
    public int? AxleConfigurationId { get; set; }
    public int? BodyTypeId { get; set; }
    public int? TyreCount { get; set; }
    public decimal? Gvw { get; set; }

    /// <summary>No foreign key yet: branches arrive with S0-FND-15 (OQ-10).</summary>
    public Guid? BranchId { get; set; }
    public int? OpeningOdometer { get; set; }
    public DateOnly? OpeningOdometerDate { get; set; }
    /// <summary>A partner holding the Driver role. On an active vehicle it is a copy of the open assignment and changes only with it.</summary>
    public int? DefaultDriverId { get; set; }
    public int? FuelCardCompanyId { get; set; }
    public string? FuelCardNumber { get; set; }
    public int? TrackerCompanyId { get; set; }
    public string? TrackerDeviceId { get; set; }
    public string? Remarks { get; set; }

    public DateOnly? AcquisitionDate { get; set; }
    public string? AcquisitionType { get; set; }

    public string Status { get; set; } = VehicleStatuses.Draft;

    public string? CurrentCategory { get; set; }
    public int? CurrentCounterpartyId { get; set; }

    /// <summary>What the wizard has collected for steps 2 to 5 while the vehicle is still a Draft, kept as the screen sent it (FR-VH-012). Cleared when the vehicle is activated.</summary>
    [NotAudited] public string? DraftData { get; set; }

    public bool IsDeleted { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    [NotAudited] public int? ModifiedBy { get; set; }
    [NotAudited] public DateTime ModifiedOn { get; set; }
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Set by the database from <see cref="Status"/> and <see cref="IsDeleted"/>: true unless Sold, Transferred or deleted. What registration, chassis and engine uniqueness is judged on.</summary>
    [NotAudited] public bool IsLive { get; private set; }
    /// <summary>Set by the database: true while the vehicle is in the fleet. What fuel card uniqueness is judged on (BR-VH-026).</summary>
    [NotAudited] public bool IsInFleet { get; private set; }
}

/// <summary>A dated link to the counterparty of a vehicle's ownership category (FSD §15, §17). Closed and superseded on change, never edited (BR-VH-005).</summary>
internal class VehicleRelation : ITenantScopedEntity, IAuditRooted
{
    public int VehicleRelationId { get; set; }
    public Guid TenantId { get; set; }
    public int VehicleId { get; set; }

    public AuditRoot GetAuditRoot() => new("Vehicle", VehicleId.ToString());

    public string Category { get; set; } = string.Empty;
    public int? CounterpartyId { get; set; }

    public DateOnly EffectiveFrom { get; set; }
    /// <summary>Null while the relation is open. At most one open relation per vehicle (BR-VH-002).</summary>
    public DateOnly? EffectiveTo { get; set; }
    /// <summary>When the arrangement is due to end, if it has an end (Shared, Rented, Customer Arrangement).</summary>
    public DateOnly? AgreementEndDate { get; set; }
    public string? AgreementReference { get; set; }

    // Shared
    public decimal? SharePercent { get; set; }
    public string? SharingBasis { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? FixedMonthlyAmount { get; set; }
    public string? ExpenseSharingRule { get; set; }

    // Rented
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? RentAmount { get; set; }
    public string? RentFrequency { get; set; }
    public int? RentDueDay { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? SecurityDeposit { get; set; }

    // Customer arrangement
    public string? ArrangementType { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? AgreedAmount { get; set; }
    public decimal? RevenueSharePercent { get; set; }
}

/// <summary>Every event in a vehicle's life in the fleet: created, status moves, category changes, disposal (FSD §23.1). Append-only.</summary>
[NotAudited]
internal class VehicleLifecycleEntry : ITenantScopedEntity
{
    public int VehicleLifecycleEntryId { get; set; }
    public Guid TenantId { get; set; }
    public int VehicleId { get; set; }

    /// <see cref="LifecycleEvents"/>.
    public string EventType { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? FromCategory { get; set; }
    public string? ToCategory { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string? Reason { get; set; }
    public string? Reference { get; set; }
    /// <summary>The buyer or new party of a sale or transfer.</summary>
    public int? CounterpartyId { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? Amount { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime OccurredOn { get; set; }
}

/// <summary>A major removable asset fitted to a vehicle: a container, an AC unit, a tyre set (FSD §20.1).</summary>
internal class VehicleAttachedItem : ITenantScopedEntity, IAuditRooted
{
    public int VehicleAttachedItemId { get; set; }
    public Guid TenantId { get; set; }
    public int VehicleId { get; set; }

    public AuditRoot GetAuditRoot() => new("Vehicle", VehicleId.ToString());

    public int ItemTypeId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? SerialNo { get; set; }
    /// <summary>A Vendor or Body Maker.</summary>
    public int? SupplierId { get; set; }
    public DateOnly InstallationDate { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? Cost { get; set; }
    public DateOnly? WarrantyUntil { get; set; }
    public string? Condition { get; set; }

    /// <see cref="ItemStatuses"/>.
    public string Status { get; set; } = ItemStatuses.Attached;
    public DateOnly? DetachedOn { get; set; }
    public string? DetachReason { get; set; }
    /// <summary>For an item that arrived by transfer: the row it came from, so the chain of vehicles it has been on can be followed.</summary>
    public int? TransferredFromItemId { get; set; }
    public int? TransferredToVehicleId { get; set; }

    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
}

/// <summary>A reading of the odometer (FSD §16.3). The opening reading is the floor for all later ones (BR-VH-025).</summary>
internal class OdometerReading : ITenantScopedEntity, IAuditRooted
{
    public int OdometerReadingId { get; set; }
    public Guid TenantId { get; set; }
    public int VehicleId { get; set; }

    public AuditRoot GetAuditRoot() => new("Vehicle", VehicleId.ToString());

    public DateOnly ReadingDate { get; set; }
    public int Km { get; set; }
    /// <see cref="OdometerSources"/>.
    public string Source { get; set; } = OdometerSources.Manual;
    public string? Notes { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
}

/// <summary>Who is a vehicle's default driver, and since when (BR-VH-022). A driver has one open assignment, a vehicle has one open assignment.</summary>
internal class DriverAssignment : ITenantScopedEntity, IAuditRooted
{
    public int DriverAssignmentId { get; set; }
    public Guid TenantId { get; set; }
    public int VehicleId { get; set; }

    public AuditRoot GetAuditRoot() => new("Vehicle", VehicleId.ToString());

    public int DriverId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? EndReason { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
}

/// <summary>
/// What was entered for a vehicle's acquisition (FSD §18.1) while it is still a Draft. Nothing is posted from it until activation,
/// when the acquisition, the initial payment and the registration cost become ledger rows (BR-VH-012). The acquisition date and type live on the vehicle.
/// </summary>
internal class VehicleAcquisition : ITenantScopedEntity, IAuditRooted
{
    public int VehicleId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("Vehicle", VehicleId.ToString());

    public int? SellerId { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? PurchasePrice { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? AmountPaid { get; set; }
    public string? PaymentMode { get; set; }
    public string? PaymentReference { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? RegistrationCost { get; set; }
    [NotAudited] public int? ModifiedBy { get; set; }
    [NotAudited] public DateTime ModifiedOn { get; set; }
}

/// <summary>An entry in the vehicle ledger (FSD §19). Never edited and never deleted: a mistake is corrected by a reversing entry (BR-VH-011).</summary>
internal class VehicleTransaction : ITenantScopedEntity, IAuditRooted
{
    public int VehicleTransactionId { get; set; }
    public Guid TenantId { get; set; }
    public int VehicleId { get; set; }

    public AuditRoot GetAuditRoot() => new("Vehicle", VehicleId.ToString());

    /// <see cref="TransactionTypes"/>.
    public string Type { get; set; } = string.Empty;
    /// <summary>For a major expense: Registration, or the kind of attached item.</summary>
    public string? SubType { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal Amount { get; set; }
    public DateOnly TransactionDate { get; set; }
    public int? PartnerId { get; set; }
    public string? Reference { get; set; }
    /// <see cref="TransactionSources"/>.
    public string Source { get; set; } = TransactionSources.Manual;
    public bool IsSystemGenerated { get; set; }
    /// <summary>The receipt behind the entry, if one was uploaded (source §7). Set together, never one without the others.</summary>
    public string? ReceiptStorageKey { get; set; }
    public string? ReceiptSha256 { get; set; }
    public string? ReceiptContentType { get; set; }
    public string? ReceiptFileName { get; set; }
    public long? ReceiptSizeBytes { get; set; }
    /// <summary>For an adjustment: the entry it reverses.</summary>
    public int? ReversesTransactionId { get; set; }
    /// <summary>For an installment payment: the installment it paid, so reversing it can reopen that installment.</summary>
    public int? InstallmentId { get; set; }
    /// <summary>Why an adjustment was posted. Required for one.</summary>
    public string? Reason { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
}

/// <summary>A vehicle's finance agreement (FSD §18.2). A vehicle has at most one open (Draft or Active) at a time (BR-VH-004); settled and closed ones stay as history.</summary>
internal class VehicleFinanceAgreement : ITenantScopedEntity, IAuditRooted
{
    public int VehicleFinanceAgreementId { get; set; }
    public Guid TenantId { get; set; }
    public int VehicleId { get; set; }

    public AuditRoot GetAuditRoot() => new("Vehicle", VehicleId.ToString());

    /// <summary>A value of the Finance Type list.</summary>
    public int FinanceTypeId { get; set; }
    public int BankId { get; set; }
    public string AgreementNo { get; set; } = string.Empty;
    public DateOnly AgreementDate { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal FinanceAmount { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal DownPayment { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal InstallmentAmount { get; set; }
    public string Frequency { get; set; } = FinanceFrequencies.Monthly;
    public int Tenure { get; set; }
    public DateOnly FirstDueDate { get; set; }
    /// <summary>Recorded for reference only: version 1 does not split principal and markup (OQ-07).</summary>
    public decimal? MarkupRate { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? ResidualAmount { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? SecurityDeposit { get; set; }

    /// <see cref="AgreementStatuses"/>.
    public string Status { get; set; } = AgreementStatuses.Draft;
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    [NotAudited] public int? ModifiedBy { get; set; }
    [NotAudited] public DateTime ModifiedOn { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>One expected payment of an agreement's schedule (FSD §18.3). What is outstanding is worked out from these, never stored (FR-VH-006).</summary>
internal class VehicleInstallment : ITenantScopedEntity, IAuditRooted
{
    public int VehicleInstallmentId { get; set; }
    public Guid TenantId { get; set; }
    public int VehicleId { get; set; }

    public AuditRoot GetAuditRoot() => new("Vehicle", VehicleId.ToString());

    public int VehicleFinanceAgreementId { get; set; }
    public int InstallmentNo { get; set; }
    public DateOnly DueDate { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal ExpectedAmount { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal PaidAmount { get; set; }
    public DateOnly? PaidOn { get; set; }
    /// <summary>The balloon due at the end of the tenure, as a final row.</summary>
    public bool IsResidual { get; set; }
    /// <see cref="InstallmentStatuses"/>.
    public string Status { get; set; } = InstallmentStatuses.Pending;
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// A recurring obligation configured on a vehicle (FSD §19A.1): insurance, tracker fee, rent and the rest. Bank Installment is not
/// configured this way — its schedule is the finance agreement's own (BR-VH-029). An amount change is effective-dated, not an edit
/// in place (BR-VH-033): this row's <see cref="EffectiveTo"/> is set and a new row takes over, so an entry already generated keeps
/// the terms that applied when it was made.
/// </summary>
internal class VehicleRecurringCharge : ITenantScopedEntity, IAuditRooted
{
    public int VehicleRecurringChargeId { get; set; }
    public Guid TenantId { get; set; }
    public int VehicleId { get; set; }

    public AuditRoot GetAuditRoot() => new("Vehicle", VehicleId.ToString());

    /// <summary>Stable across the amendment rows of one charge, so its entries and history can be found across all of them.</summary>
    public Guid SeriesId { get; set; }

    public int ChargeTypeId { get; set; }
    public int PayeeId { get; set; }
    public int ExpenseTypeId { get; set; }

    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? Amount { get; set; }
    /// <see cref="ChargeAmountBases"/>.
    public string AmountBasis { get; set; } = ChargeAmountBases.Fixed;

    /// <see cref="ChargeFrequencies"/>.
    public string Frequency { get; set; } = ChargeFrequencies.Monthly;
    /// <summary>Day of month for Monthly, Quarterly, Half-yearly and Yearly (with <see cref="DueMonth"/>); 29 to 31 falls back to month end.</summary>
    public int? DueDay { get; set; }
    /// <summary>The anniversary month, for Yearly.</summary>
    public int? DueMonth { get; set; }
    /// <summary>The step in days, for Custom Days.</summary>
    public int? CustomIntervalDays { get; set; }

    /// <summary>Not before the vehicle's acquisition date.</summary>
    public DateOnly StartDate { get; set; }
    /// <summary>Blank = open-ended until the vehicle is disposed. Mutually exclusive with <see cref="OccurrenceCount"/>.</summary>
    public DateOnly? EndDate { get; set; }
    public int? OccurrenceCount { get; set; }
    /// <summary>How many occurrences the generation job has produced so far, against <see cref="OccurrenceCount"/>.</summary>
    public int GeneratedCount { get; set; }

    /// <see cref="ChargePostingModes"/>.
    public string PostingMode { get; set; } = ChargePostingModes.GenerateAsDue;
    public int GenerateLeadDays { get; set; } = 7;
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? TaxWithholdingPercent { get; set; }

    /// <summary>The cursor the generation job advances: the next occurrence not yet generated. Null once the charge has produced its last one.</summary>
    public DateOnly? NextDueDate { get; set; }

    /// <summary>Null while this is the version in force. Set to the day it was superseded by an amendment, ended by hand, or end-dated by
    /// disposal (BR-VH-034) or by a category change closing the relation it belongs to (BR-VH-035).</summary>
    public DateOnly? EffectiveTo { get; set; }
    public string? EndReason { get; set; }

    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    [NotAudited] public int? ModifiedBy { get; set; }
    [NotAudited] public DateTime ModifiedOn { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// One occurrence the generation job produced for a charge (FSD §19A.4), or a Bank Installment surfaced from the finance schedule
/// (BR-VH-029, which never materialises a row here — see <c>IPayablesQueryService</c>). Idempotent on (ChargeId, PeriodKey), BR-VH-030.
/// </summary>
internal class VehicleRecurringChargeEntry : ITenantScopedEntity, IAuditRooted
{
    public int VehicleRecurringChargeEntryId { get; set; }
    public Guid TenantId { get; set; }
    public int VehicleId { get; set; }

    public AuditRoot GetAuditRoot() => new("Vehicle", VehicleId.ToString());

    public int VehicleRecurringChargeId { get; set; }
    /// <summary>The occurrence this entry is for, e.g. <c>2026-10</c> for a monthly charge or the due date itself for Custom Days. Together with the charge, the idempotency key (BR-VH-030).</summary>
    public string PeriodKey { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal ExpectedAmount { get; set; }

    /// <see cref="ChargeEntryStatuses"/>.
    public string Status { get; set; } = ChargeEntryStatuses.Due;

    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? PaidAmount { get; set; }
    public DateOnly? PaidOn { get; set; }
    public string? PaymentMode { get; set; }
    public string? Reference { get; set; }
    public string? Remarks { get; set; }
    /// <summary>The ledger entry a confirmation or an auto-post wrote (BR-VH-031).</summary>
    public int? TransactionId { get; set; }

    /// <summary>Why a Waived or a by-hand Cancelled entry was set aside. Required for both.</summary>
    public string? WaiveReason { get; set; }
    /// <summary>Null for an auto-posted entry (BR-VH-028): nobody confirmed it.</summary>
    public int? ConfirmedBy { get; set; }
    public DateTime? ConfirmedOn { get; set; }

    public DateTime CreatedOn { get; set; }
}