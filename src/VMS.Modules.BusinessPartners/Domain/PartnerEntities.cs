using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Common;

namespace VMS.Modules.BusinessPartners.Domain;

/// <summary>
/// The partner master (FSD §5, §6): one row for anyone the business deals with, whatever role they play. Never
/// physically deleted (BR-BP-015). <c>BpCode</c> is system-generated and immutable (BR-BP-003).
/// </summary>
internal class BusinessPartner : ITenantScopedEntity, IAuditRooted
{
    public int BusinessPartnerId { get; set; }

    public AuditRoot GetAuditRoot() => new("BusinessPartner", BusinessPartnerId.ToString());
    public Guid TenantId { get; set; }

    public string BpCode { get; set; } = string.Empty;
    public string PartyType { get; set; } = PartyTypes.Person;
    public string LegalName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }

    public string? Cnic { get; set; }
    public string? Ntn { get; set; }
    public string? Strn { get; set; }
    public string FilerStatus { get; set; } = FilerStatuses.Unknown;

    public string PrimaryMobile { get; set; } = string.Empty;
    public string? AlternatePhone { get; set; }
    public string? Email { get; set; }

    /// <summary>A value of the City master. No foreign key: the master lives in the Core module.</summary>
    public int CityId { get; set; }
    public string AddressLine { get; set; } = string.Empty;
    /// <summary>The owning branch. No foreign key yet: branches arrive with S0-FND-15.</summary>
    public Guid? BranchId { get; set; }

    public string Status { get; set; } = PartnerStatuses.Active;
    public string? StatusReason { get; set; }

    /// <summary>Positive: we owe the partner. Negative: the partner owes us. Entered once, at creation (BR-BP-018).</summary>
    [FieldPermission(PermissionCodes.BP_FIELD_OPENING_VIEW)] public decimal OpeningBalance { get; set; }
    [FieldPermission(PermissionCodes.BP_FIELD_OPENING_VIEW)] public DateOnly? OpeningBalanceDate { get; set; }
    public string Currency { get; set; } = "PKR";
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    [NotAudited] public int? ModifiedBy { get; set; }
    [NotAudited] public DateTime ModifiedOn { get; set; }

    /// <summary>Optimistic concurrency (NFR §24.3): a stale save is refused instead of overwriting.</summary>
    public byte[] RowVersion { get; set; } = [];

    public List<BusinessPartnerRole> Roles { get; } = [];
    public List<BpContact> Contacts { get; } = [];
    public List<BpAddress> Addresses { get; } = [];
    public List<BpBankAccount> BankAccounts { get; } = [];
    public BpDriverDetail? Driver { get; set; }
    public BpVendorDetail? Vendor { get; set; }
    public BpCustomerDetail? Customer { get; set; }
}

/// <summary>One row per role the partner has held. Deactivating stamps <c>EffectiveTo</c>; the row and its detail stay for history (BR-BP-001).</summary>
internal class BusinessPartnerRole : ITenantScopedEntity, IAuditRooted
{
    public int BpRoleId { get; set; }
    public Guid TenantId { get; set; }
    public int BusinessPartnerId { get; set; }

    public AuditRoot GetAuditRoot() => new("BusinessPartner", BusinessPartnerId.ToString());
    public string RoleCode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

/// <summary>Exists once the Driver role has been active. Never deleted (BR-BP-001).</summary>
internal class BpDriverDetail : ITenantScopedEntity, IAuditRooted
{
    public int BusinessPartnerId { get; set; }

    public AuditRoot GetAuditRoot() => new("BusinessPartner", BusinessPartnerId.ToString());
    public Guid TenantId { get; set; }
    public string LicenceNo { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public DateOnly? LicenceIssueDate { get; set; }
    public DateOnly LicenceExpiryDate { get; set; }
    public string EmploymentType { get; set; } = string.Empty;
    public DateOnly? DateOfJoining { get; set; }
    [FieldPermission(PermissionCodes.BP_FIELD_SALARY_VIEW)] public decimal? MonthlyRate { get; set; }
    [FieldPermission(PermissionCodes.BP_FIELD_SALARY_VIEW)] public string CommissionBasis { get; set; } = RoleFieldValues.CommissionNone;
    [FieldPermission(PermissionCodes.BP_FIELD_SALARY_VIEW)] public decimal? CommissionValue { get; set; }
    public string? BloodGroup { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Guarantor { get; set; }
    public bool DriverAppAccess { get; set; }
}

internal class BpVendorDetail : ITenantScopedEntity, IAuditRooted
{
    public int BusinessPartnerId { get; set; }

    public AuditRoot GetAuditRoot() => new("BusinessPartner", BusinessPartnerId.ToString());
    public Guid TenantId { get; set; }
    /// <summary>Comma-separated values of <see cref="RoleFieldValues.SupplyCategories"/>.</summary>
    public string SupplyCategories { get; set; } = string.Empty;
    [FieldPermission(PermissionCodes.BP_FIELD_CREDIT_VIEW)] public int PaymentTermDays { get; set; }
    [FieldPermission(PermissionCodes.BP_FIELD_CREDIT_VIEW)] public decimal? CreditLimit { get; set; }
}

internal class BpCustomerDetail : ITenantScopedEntity, IAuditRooted
{
    public int BusinessPartnerId { get; set; }

    public AuditRoot GetAuditRoot() => new("BusinessPartner", BusinessPartnerId.ToString());
    public Guid TenantId { get; set; }
    public string CustomerType { get; set; } = string.Empty;
    public string BillingCycle { get; set; } = string.Empty;
    public string? RateBasis { get; set; }
    public decimal? DefaultRate { get; set; }
    [FieldPermission(PermissionCodes.BP_FIELD_CREDIT_VIEW)] public decimal? CreditLimit { get; set; }
    [FieldPermission(PermissionCodes.BP_FIELD_CREDIT_VIEW)] public int? CreditDays { get; set; }
}

/// <summary>A named person at the partner (FSD §8.1). At most one is primary (BR-BP-002).</summary>
internal class BpContact : ITenantScopedEntity, IAuditRooted
{
    public int BpContactId { get; set; }
    public Guid TenantId { get; set; }
    public int BusinessPartnerId { get; set; }

    public AuditRoot GetAuditRoot() => new("BusinessPartner", BusinessPartnerId.ToString());
    public string ContactName { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string Mobile { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsPrimary { get; set; }
    public string? Notes { get; set; }
}

internal class BpAddress : ITenantScopedEntity, IAuditRooted
{
    public int BpAddressId { get; set; }
    public Guid TenantId { get; set; }
    public int BusinessPartnerId { get; set; }

    public AuditRoot GetAuditRoot() => new("BusinessPartner", BusinessPartnerId.ToString());
    public string AddressType { get; set; } = AddressTypes.Registered;
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public int CityId { get; set; }
    /// <summary>The city's province, filled in from the City master where it is mapped.</summary>
    public string? ProvinceCode { get; set; }
    public string? Landmark { get; set; }
    public bool IsPrimary { get; set; }
}

internal class BpBankAccount : ITenantScopedEntity, IAuditRooted
{
    public int BpBankAccountId { get; set; }
    public Guid TenantId { get; set; }
    public int BusinessPartnerId { get; set; }

    public AuditRoot GetAuditRoot() => new("BusinessPartner", BusinessPartnerId.ToString());
    public string AccountTitle { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string? BranchCode { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string? Iban { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>Every role added or removed, with why and by whom: the evidence behind BR-BP-011 and BR-BP-016 (FSD §11).</summary>
[NotAudited]
internal class BpRoleLog : ITenantScopedEntity
{
    public int BpRoleLogId { get; set; }
    public Guid TenantId { get; set; }
    public int BusinessPartnerId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    /// <summary><c>Added</c> or <c>Removed</c>.</summary>
    public string Action { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
    public string? Reason { get; set; }
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime OccurredOn { get; set; }
}

/// <summary>Every status change: from, to, reason, effective date, user and time (FSD §13.1).</summary>
[NotAudited]
internal class BpStatusLog : ITenantScopedEntity
{
    public int BpStatusLogId { get; set; }
    public Guid TenantId { get; set; }
    public int BusinessPartnerId { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime OccurredOn { get; set; }
}
