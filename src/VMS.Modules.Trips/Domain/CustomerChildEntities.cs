using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>A customer's contact person (FSD §11). Never a single field on <see cref="Customer"/> — a customer
/// has many. No physical delete, only deactivation.</summary>
internal sealed class CustomerContact : ITenantScopedEntity, IAuditRooted
{
    public long CustomerContactId { get; set; }
    public Guid TenantId { get; set; }
    public int CustomerId { get; set; }

    public AuditRoot GetAuditRoot() => new("Customer", CustomerId.ToString());

    public string Name { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string Mobile1 { get; set; } = string.Empty;
    public string? Mobile2 { get; set; }
    public string? Telephone { get; set; }
    public string? Email { get; set; }
    public string? AvailabilityTime { get; set; }
    /// <summary>Comma-separated <see cref="ContactPurposes"/> codes (a lightweight multi-select tag, not a
    /// permissioned child list the way a Business Partner's roles are).</summary>
    public string? Purpose { get; set; }
    public bool IsPrimary { get; set; }
    public string Status { get; set; } = ActiveInactiveStatuses.Active;
}

public static class ContactPurposes
{
    public const string Billing = "Billing";
    public const string Operations = "Operations";
    public const string Pod = "POD";
    public const string Other = "Other";
    public static readonly IReadOnlyList<string> All = [Billing, Operations, Pod, Other];
}

/// <summary>Shared by every simple Active/Inactive child of Customer (contacts, billing addresses) so the two
/// don't each spell the same two strings out.</summary>
public static class ActiveInactiveStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public static readonly IReadOnlyList<string> All = [Active, Inactive];
}

/// <summary>A customer's billing address (FSD §12). The one used on an invoice is snapshotted onto the invoice
/// itself (later CC-23+), so editing or deactivating this row never changes an already-generated invoice.</summary>
internal sealed class CustomerBillingAddress : ITenantScopedEntity, IAuditRooted
{
    public long CustomerBillingAddressId { get; set; }
    public Guid TenantId { get; set; }
    public int CustomerId { get; set; }

    public AuditRoot GetAuditRoot() => new("Customer", CustomerId.ToString());

    /// <summary>Unique per customer, e.g. "Head Office", "Plant 2" — not unique across the whole tenant.</summary>
    public string AddressName { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public int CityId { get; set; }
    public string? ProvinceState { get; set; }
    public int CountryId { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>Overrides the customer-level registration numbers on an invoice, if present (§12).</summary>
    public string? Ntn { get; set; }
    public string? Strn { get; set; }
    public bool IsDefault { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string Status { get; set; } = ActiveInactiveStatuses.Active;
}
