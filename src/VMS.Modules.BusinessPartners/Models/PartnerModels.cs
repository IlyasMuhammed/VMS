using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.BusinessPartners.Models;

// Values that are amounts a person may be barred from seeing carry [FieldPermission]: they are left out of the JSON
// (absent, not zero) for a caller without the permission (BR-SEC-001). On a save, values the caller cannot see are ignored,
// so an edit by someone who cannot see a salary never wipes it.

public class DriverModel
{
    public string LicenceNo { get; set; } = string.Empty;
    /// <summary>LTV, HTV, Motorcycle or Other.</summary>
    public string LicenceType { get; set; } = string.Empty;
    public DateOnly? LicenceIssueDate { get; set; }
    public DateOnly? LicenceExpiryDate { get; set; }
    /// <summary>Employee, Contractor or AdHoc.</summary>
    public string EmploymentType { get; set; } = string.Empty;
    public DateOnly? DateOfJoining { get; set; }
    [FieldPermission(PermissionCodes.BP_FIELD_SALARY_VIEW)] public decimal? MonthlyRate { get; set; }
    /// <summary>None, Percent or Fixed.</summary>
    [FieldPermission(PermissionCodes.BP_FIELD_SALARY_VIEW)] public string? CommissionBasis { get; set; }
    [FieldPermission(PermissionCodes.BP_FIELD_SALARY_VIEW)] public decimal? CommissionValue { get; set; }
    public string? BloodGroup { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Guarantor { get; set; }
    public bool DriverAppAccess { get; set; }
}

public class VendorModel
{
    /// <summary>Any of Parts, Tyres, Lubricants, Fuel, Toll, Services, Other.</summary>
    public List<string> SupplyCategories { get; set; } = [];
    [FieldPermission(PermissionCodes.BP_FIELD_CREDIT_VIEW)] public int? PaymentTermDays { get; set; }
    [FieldPermission(PermissionCodes.BP_FIELD_CREDIT_VIEW)] public decimal? CreditLimit { get; set; }
}

public class CustomerModel
{
    /// <summary>Adda, CargoCompany, Factory, Trader or Other.</summary>
    public string CustomerType { get; set; } = string.Empty;
    /// <summary>PerTrip, Weekly, Fortnightly or Monthly.</summary>
    public string BillingCycle { get; set; } = string.Empty;
    /// <summary>PerTrip, PerTonne, PerKm or MonthlyFixed.</summary>
    public string? RateBasis { get; set; }
    public decimal? DefaultRate { get; set; }
    [FieldPermission(PermissionCodes.BP_FIELD_CREDIT_VIEW)] public decimal? CreditLimit { get; set; }
    [FieldPermission(PermissionCodes.BP_FIELD_CREDIT_VIEW)] public int? CreditDays { get; set; }
}

public class ContactModel
{
    /// <summary>Present for a row that exists; leave out to add one. A row left out of an update is removed.</summary>
    public int? Id { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string Mobile { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsPrimary { get; set; }
    public string? Notes { get; set; }
}

public class AddressModel
{
    public int? Id { get; set; }
    /// <summary>Registered, Billing, Workshop, Yard or Correspondence.</summary>
    public string AddressType { get; set; } = string.Empty;
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public int CityId { get; set; }
    /// <summary>Filled in from the city's province; not accepted on a save.</summary>
    public string? ProvinceCode { get; set; }
    public string? Landmark { get; set; }
    public bool IsPrimary { get; set; }
}

public class BankAccountModel
{
    public int? Id { get; set; }
    public string AccountTitle { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string? BranchCode { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string? Iban { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>What is sent to create or update a partner (FSD §6). Role panels are only used for roles the partner holds.</summary>
public class PartnerInput
{
    /// <summary>Person or Company.</summary>
    public string PartyType { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Cnic { get; set; }
    public string? Ntn { get; set; }
    public string? Strn { get; set; }
    /// <summary>Filer, NonFiler or Unknown (the default).</summary>
    public string? FilerStatus { get; set; }
    public string PrimaryMobile { get; set; } = string.Empty;
    public string? AlternatePhone { get; set; }
    public string? Email { get; set; }
    /// <summary>A value of the City master.</summary>
    public int CityId { get; set; }
    public string AddressLine { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public string? Notes { get; set; }

    /// <summary>Entered once, at creation; ignored on an update (BR-BP-018).</summary>
    [FieldPermission(PermissionCodes.BP_FIELD_OPENING_VIEW)] public decimal? OpeningBalance { get; set; }
    [FieldPermission(PermissionCodes.BP_FIELD_OPENING_VIEW)] public DateOnly? OpeningBalanceDate { get; set; }

    public DriverModel? Driver { get; set; }
    public VendorModel? Vendor { get; set; }
    public CustomerModel? Customer { get; set; }

    public List<ContactModel> Contacts { get; set; } = [];
    public List<AddressModel> Addresses { get; set; } = [];
    public List<BankAccountModel> BankAccounts { get; set; } = [];

    /// <summary>
    /// Partners the user has seen listed as possible duplicates and chosen to save alongside (FSD §12.1). Every
    /// soft duplicate must be named here or the save is refused; each is recorded in the audit trail (BR-BP-021).
    /// </summary>
    public List<int> AcknowledgedDuplicateIds { get; set; } = [];
}

public class CreatePartnerRequest : PartnerInput
{
    /// <summary>At least one of Driver, Workshop, Bank, Vendor, Customer, RunningCustomer, TrackerCompany, BodyMaker, FuelCardCompany.</summary>
    public List<string> Roles { get; set; } = [];
}

public class UpdatePartnerRequest : PartnerInput
{
    /// <summary>The <c>rowVersion</c> the screen was loaded with. If the partner changed since, the save is refused.</summary>
    public string RowVersion { get; set; } = string.Empty;
}

public class PartnerRoleModel
{
    public string RoleCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

/// <summary>A partner as the screen shows it.</summary>
public class PartnerModel : PartnerInput
{
    public int Id { get; set; }
    public string BpCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? StatusReason { get; set; }
    public string Currency { get; set; } = "PKR";
    public List<PartnerRoleModel> RoleHistory { get; set; } = [];
    /// <summary>The roles held now.</summary>
    public List<string> Roles { get; set; } = [];
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
    /// <summary>Send this back with an update so a stale save can be refused.</summary>
    public string RowVersion { get; set; } = string.Empty;
    /// <summary>A document a held role expects but has not been attached (BR-BP-005) — a warning, never a reason the save was refused (OQ-04).</summary>
    public List<string> DocumentWarnings { get; set; } = [];
}

public class AddRoleRequest
{
    public string RoleCode { get; set; } = string.Empty;
    public string? Reason { get; set; }
    /// <summary>Required when adding Driver, Vendor or Customer to a partner that has never held it.</summary>
    public DriverModel? Driver { get; set; }
    public VendorModel? Vendor { get; set; }
    public CustomerModel? Customer { get; set; }
}

public class ChangeStatusRequest
{
    /// <summary>Active, Inactive or Blacklisted.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Required to blacklist (at least 10 characters).</summary>
    public string? Reason { get; set; }
    /// <summary>Defaults to today. Not in the future.</summary>
    public DateOnly? EffectiveDate { get; set; }
}

public class DuplicateCheckRequest
{
    /// <summary>Person or Company. An NTN only counts as a duplicate of another Company's, so leave it out to check as a Company.</summary>
    public string? PartyType { get; set; }
    public string? LegalName { get; set; }
    public string? Cnic { get; set; }
    public string? Ntn { get; set; }
    public string? PrimaryMobile { get; set; }
    public int? CityId { get; set; }
    /// <summary>The partner being edited, so it is not reported as a duplicate of itself.</summary>
    public int? ExcludeId { get; set; }
}

/// <summary>A partner that looks like the one being entered (FSD §12.1).</summary>
public class DuplicateMatch
{
    public int Id { get; set; }
    public string BpCode { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public int CityId { get; set; }
    public string? City { get; set; }
    public string Status { get; set; } = string.Empty;
    /// <summary>Cnic, Ntn (hard: the save is refused), or NameAndCity, Mobile, NameSimilar (soft: warn, allow with acknowledgement).</summary>
    public string MatchType { get; set; } = string.Empty;
    public bool IsHard { get; set; }
    /// <summary>For NameSimilar: 0 to 1.</summary>
    public double? Score { get; set; }
    /// <summary>True when the candidate is in the same city as the partner being entered.</summary>
    public bool SameCity { get; set; }
}

public class DuplicateCheckResult
{
    public List<DuplicateMatch> Matches { get; set; } = [];
}

public class PartnerListItem
{
    public int Id { get; set; }
    public string BpCode { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PartyType { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public int CityId { get; set; }
    public string? City { get; set; }
    public string PrimaryMobile { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public string? Branch { get; set; }
    public DateTime ModifiedOn { get; set; }
}

public class PartnerListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    /// <summary>Matches BP code, legal and display name (and former names), CNIC, NTN, mobile: partial, from 3 characters.</summary>
    public string? Search { get; set; }
    /// <summary>modifiedOn (default, newest first), bpCode, legalName, status, city. Append <c>,asc</c> or <c>,desc</c>.</summary>
    public string? Sort { get; set; }
    /// <summary>Partners holding any of these roles now. Repeat the parameter for several.</summary>
    public List<string>? Roles { get; set; }
    public string? Status { get; set; }
    public int? CityId { get; set; }
    public Guid? BranchId { get; set; }
    public string? PartyType { get; set; }
}

public class PartnerPickerItem
{
    public int Id { get; set; }
    public string BpCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public int CityId { get; set; }
    public string? City { get; set; }
}

/// <summary>One change to a partner or to something that belongs to it (its contacts, addresses, bank accounts, role panels).</summary>
public class HistoryChange
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? UserName { get; set; }
    /// <summary>The kind of record changed: BusinessPartner, BpContact, BpAddress, BpBankAccount, BusinessPartnerRole, BpDriverDetail, …</summary>
    public string Entity { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    /// <summary>Created, Updated, Deleted, or a named event such as DuplicateOverridden.</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>The field changed, for an update. Empty for a whole record.</summary>
    public string? Field { get; set; }
    /// <summary>For an update the old and new value; for a created or deleted record a JSON snapshot in one of them.</summary>
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    /// <summary>True when the values are left out because the caller may not see them (a salary, a credit limit).</summary>
    public bool Restricted { get; set; }
}

public class RoleLogItem
{
    public string RoleCode { get; set; } = string.Empty;
    /// <summary>Added or Removed.</summary>
    public string Action { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
    public string? Reason { get; set; }
    public string? UserName { get; set; }
    public DateTime OccurredOn { get; set; }
}

public class StatusLogItem
{
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string? UserName { get; set; }
    public DateTime OccurredOn { get; set; }
}

/// <summary>Everything that happened to a partner (FSD §9.2 History tab): field changes, role changes, status changes.</summary>
public class PartnerHistory
{
    public PaginatedResponse<HistoryChange> Changes { get; set; } = new();
    public List<RoleLogItem> Roles { get; set; } = [];
    public List<StatusLogItem> Statuses { get; set; } = [];
}

public class PartnerUsageModel
{
    public string Kind { get; set; } = string.Empty;
    public string RecordCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
