using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Vehicles.Models;

/// <summary>What is sent to save a vehicle's identity, technical and operational fields (FSD §16). Everything else about a vehicle has its own action.</summary>
public class VehicleInput
{
    public string RegistrationNo { get; set; } = string.Empty;
    public int? RegistrationCityId { get; set; }
    public string? ChassisNo { get; set; }
    public string? EngineNo { get; set; }
    /// <summary>A value of the Vehicle Type list.</summary>
    public int VehicleTypeId { get; set; }
    /// <summary>A value of the Make list.</summary>
    public int MakeId { get; set; }
    public string Model { get; set; } = string.Empty;
    public int? ManufacturingYear { get; set; }
    public string? Colour { get; set; }

    /// <summary>Diesel, Petrol, CNG, LPG, Hybrid or Electric.</summary>
    public string FuelType { get; set; } = string.Empty;
    public decimal? TankCapacity { get; set; }
    /// <summary>Required for a Truck, Trailer, Tanker or Prime Mover.</summary>
    public decimal? LoadCapacity { get; set; }
    /// <summary>Tonne, Kg, Litre, CFT or Passengers. Required when a load capacity is entered.</summary>
    public string? CapacityUnit { get; set; }
    public int? AxleConfigurationId { get; set; }
    public int? BodyTypeId { get; set; }
    public int? TyreCount { get; set; }
    public decimal? Gvw { get; set; }

    public Guid? BranchId { get; set; }
    public int? OpeningOdometer { get; set; }
    /// <summary>Required when an opening odometer is entered. Not in the future.</summary>
    public DateOnly? OpeningOdometerDate { get; set; }
    /// <summary>A Driver partner. On a vehicle already in the fleet the default driver is changed with the assign action, not here.</summary>
    public int? DefaultDriverId { get; set; }
    public int? FuelCardCompanyId { get; set; }
    /// <summary>Required when a fuel card company is chosen. On one vehicle in the fleet at a time.</summary>
    public string? FuelCardNumber { get; set; }
    public int? TrackerCompanyId { get; set; }
    public string? TrackerDeviceId { get; set; }
    public string? Remarks { get; set; }

    /// <summary>Not in the future. Needs the acquisition permission; ignored for anyone without it.</summary>
    public DateOnly? AcquisitionDate { get; set; }
    /// <summary>Purchase, Lease, Rent, SharedInduction or CustomerInduction.</summary>
    public string? AcquisitionType { get; set; }

    /// <summary>The wizard's answers for steps 2 to 5 while the vehicle is a Draft, kept as they were sent so the wizard can be reopened (FR-VH-012). Up to 200 KB of JSON.</summary>
    public string? DraftData { get; set; }

    /// <summary>The fuel card number is on another vehicle in the fleet: say true to take it from that one (VAL-VH-018).</summary>
    public bool ReassignFuelCard { get; set; }
}

public class CreateVehicleRequest : VehicleInput;

public class UpdateVehicleRequest : VehicleInput
{
    /// <summary>The <c>rowVersion</c> the screen was loaded with. If the vehicle changed since, the save is refused.</summary>
    public string RowVersion { get; set; } = string.Empty;
}

public class PartnerRef
{
    public int Id { get; set; }
    public string BpCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>One dated ownership relation, current or past.</summary>
public class RelationModel
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public PartnerRef? Counterparty { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public DateOnly? AgreementEndDate { get; set; }
    public string? AgreementReference { get; set; }
    public decimal? SharePercent { get; set; }
    public string? SharingBasis { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? FixedMonthlyAmount { get; set; }
    public string? ExpenseSharingRule { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? RentAmount { get; set; }
    public string? RentFrequency { get; set; }
    public int? RentDueDay { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? SecurityDeposit { get; set; }
    public string? ArrangementType { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? AgreedAmount { get; set; }
    public decimal? RevenueSharePercent { get; set; }
}

/// <summary>A vehicle as the screen shows it.</summary>
public class VehicleModel : VehicleInput
{
    public int Id { get; set; }
    public string VehicleCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CurrentCategory { get; set; }
    public PartnerRef? CurrentCounterparty { get; set; }
    public PartnerRef? DefaultDriver { get; set; }
    public PartnerRef? FuelCardCompany { get; set; }
    public PartnerRef? TrackerCompany { get; set; }
    /// <summary>The open relation, or null for a Self Owned or Draft vehicle.</summary>
    public RelationModel? Relation { get; set; }
    /// <summary>Every relation the vehicle has had, newest first.</summary>
    public List<RelationModel> RelationHistory { get; set; } = [];
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

// ── Category ──────────────────────────────────────────────────────────────────────

/// <summary>The fields of an ownership category (FSD §17). Only the ones of the chosen category are read.</summary>
public class CategoryDetails
{
    /// <summary>The bank, lessor, customer or sharing partner. Not used for Self Owned.</summary>
    public int? CounterpartyId { get; set; }
    /// <summary>When the arrangement starts. Not in the future.</summary>
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? AgreementReference { get; set; }

    public decimal? SharePercent { get; set; }
    /// <summary>ProfitShare, RevenueShare or FixedMonthly.</summary>
    public string? SharingBasis { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? FixedMonthlyAmount { get; set; }
    /// <summary>AllExpensesSameRatio, OnlyMajorExpenses or EachBearsOwn.</summary>
    public string? ExpenseSharingRule { get; set; }

    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? RentAmount { get; set; }
    /// <summary>Monthly, Weekly or PerTrip.</summary>
    public string? RentFrequency { get; set; }
    public int? RentDueDay { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? SecurityDeposit { get; set; }

    /// <summary>DedicatedMonthly, PerTrip or RevenueShare.</summary>
    public string? ArrangementType { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? AgreedAmount { get; set; }
    public decimal? RevenueSharePercent { get; set; }
}

public class ChangeCategoryRequest
{
    /// <summary>SelfOwned, Shared, Rented, CustomerArrangement or BankLeased.</summary>
    public string Category { get; set; } = string.Empty;
    /// <summary>The day the new arrangement starts. The old one ends the day before. Not in the future.</summary>
    public DateOnly? EffectiveDate { get; set; }
    public string? Reason { get; set; }
    public CategoryDetails Details { get; set; } = new();
}

// ── Status, disposal ──────────────────────────────────────────────────────────────

public class ChangeStatusRequest
{
    /// <summary>Active, UnderMaintenance or TemporarilyUnavailable.</summary>
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    /// <summary>Defaults to today. Not in the future.</summary>
    public DateOnly? EffectiveDate { get; set; }
}

public class DisposeRequest
{
    /// <summary>Retire, Sell or Transfer.</summary>
    public string Kind { get; set; } = string.Empty;
    public DateOnly? Date { get; set; }
    public string? Reason { get; set; }
    /// <summary>The buyer, or the party the vehicle goes to. Required to sell or transfer.</summary>
    public int? CounterpartyId { get; set; }
    /// <summary>Required to sell.</summary>
    public decimal? Amount { get; set; }
    public string? Reference { get; set; }
}

public class LifecycleItem
{
    public string EventType { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? FromCategory { get; set; }
    public string? ToCategory { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string? Reason { get; set; }
    public string? Reference { get; set; }
    public PartnerRef? Counterparty { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? Amount { get; set; }
    public string? UserName { get; set; }
    public DateTime OccurredOn { get; set; }
}

/// <summary>One change to a vehicle or to what belongs to it, from the audit trail.</summary>
public class VehicleHistoryChange
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid GroupId { get; set; }
    public string? UserName { get; set; }
    public string Entity { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Field { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public bool Restricted { get; set; }
}

public class VehicleHistory
{
    public PaginatedResponse<VehicleHistoryChange> Changes { get; set; } = new();
    public List<LifecycleItem> Lifecycle { get; set; } = [];
}

// ── Attached items ────────────────────────────────────────────────────────────────

public class AttachItemRequest
{
    /// <summary>A value of the Attached Item Type list.</summary>
    public int ItemTypeId { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>Unique among attached items. A container number for a container.</summary>
    public string? SerialNo { get; set; }
    /// <summary>A Vendor or Body Maker partner.</summary>
    public int? SupplierId { get; set; }
    /// <summary>Not before the vehicle's acquisition date, not in the future.</summary>
    public DateOnly? InstallationDate { get; set; }
    /// <summary>Needs the cost permission; ignored for anyone without it.</summary>
    public decimal? Cost { get; set; }
    public DateOnly? WarrantyUntil { get; set; }
    /// <summary>New, Used or Refurbished.</summary>
    public string? Condition { get; set; }
}

public class ItemModel
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public int ItemTypeId { get; set; }
    public string? ItemType { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? SerialNo { get; set; }
    public PartnerRef? Supplier { get; set; }
    public DateOnly InstallationDate { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? Cost { get; set; }
    public DateOnly? WarrantyUntil { get; set; }
    public string? Condition { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateOnly? DetachedOn { get; set; }
    public string? DetachReason { get; set; }
    public int? TransferredFromItemId { get; set; }
    public int? TransferredToVehicleId { get; set; }
}

public class DetachItemRequest
{
    public DateOnly? Date { get; set; }
    public string? Reason { get; set; }
}

public class TransferItemRequest
{
    public int TargetVehicleId { get; set; }
    public DateOnly? Date { get; set; }
    public string? Reason { get; set; }
}

// ── Odometer, driver ──────────────────────────────────────────────────────────────

public class AddOdometerRequest
{
    public DateOnly? ReadingDate { get; set; }
    public int Km { get; set; }
    public string? Notes { get; set; }
}

public class OdometerModel
{
    public int Id { get; set; }
    public DateOnly ReadingDate { get; set; }
    public int Km { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class AssignDriverRequest
{
    public int DriverId { get; set; }
    /// <summary>Defaults to today. Not in the future.</summary>
    public DateOnly? EffectiveDate { get; set; }
    /// <summary>The driver is the default driver of another vehicle: say true to release that assignment (VAL-VH-017).</summary>
    public bool ReleaseFromOther { get; set; }
}

public class ReleaseDriverRequest
{
    public DateOnly? Date { get; set; }
    public string? Reason { get; set; }
}

// ── List ──────────────────────────────────────────────────────────────────────────

public class VehicleListItem
{
    public int Id { get; set; }
    public string VehicleCode { get; set; } = string.Empty;
    public string RegistrationNo { get; set; } = string.Empty;
    public int VehicleTypeId { get; set; }
    public string? VehicleType { get; set; }
    public int MakeId { get; set; }
    public string? Make { get; set; }
    public string Model { get; set; } = string.Empty;
    public string? CurrentCategory { get; set; }
    public PartnerRef? Counterparty { get; set; }
    public Guid? BranchId { get; set; }
    public string? Branch { get; set; }
    public string Status { get; set; } = string.Empty;
    public PartnerRef? Driver { get; set; }
    public DateTime ModifiedOn { get; set; }
}

public class VehicleListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    /// <summary>Matches registration number, chassis, engine and vehicle code: partial, from 3 characters.</summary>
    public string? Search { get; set; }
    /// <summary>modifiedOn (default, newest first), registrationNo, vehicleCode, status, category. Append <c>,asc</c> or <c>,desc</c>.</summary>
    public string? Sort { get; set; }
    public List<string>? Category { get; set; }
    public List<string>? Status { get; set; }
    public int? VehicleTypeId { get; set; }
    public int? MakeId { get; set; }
    public Guid? BranchId { get; set; }
    public int? DriverId { get; set; }
}
