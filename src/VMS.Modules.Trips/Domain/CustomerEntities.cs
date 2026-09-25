using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>The customer master (FSD §10, BR-C1) — deliberately independent of Business Partner: no
/// <c>BusinessPartnerId</c> anywhere on this entity or on anything that references a customer.</summary>
internal sealed class Customer : ITenantScopedEntity, IAuditRooted
{
    public int CustomerId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("Customer", CustomerId.ToString());

    /// <summary>Unique per tenant, case-insensitive (stored upper-cased so the database's own unique index is
    /// enough, without a second normalized column). FSD: "immutable after the customer is referenced by a trip or
    /// invoice" — neither exists yet in this module (later CC tasks), so nothing enforces that yet; always
    /// editable for now. Extend <see cref="Services.CustomerService.UpdateAsync"/> once Trip/Invoice land.</summary>
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? ShortName { get; set; }

    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    /// <summary>FK into <c>core.LookupValues</c> (<c>COUNTRY</c>) — the platform's own lookup mechanism, reused
    /// rather than a new Country table (FSD default: Pakistan).</summary>
    public int CountryId { get; set; }
    public string? ProvinceState { get; set; }
    /// <summary>FK into <c>core.LookupValues</c> (<c>CITY</c>) — the same City list Business Partner addresses
    /// already use. The Trip module's own richer City/Route master (CC-08/09) is a separate concept for routing,
    /// not a replacement for this simple address field.</summary>
    public int? CityId { get; set; }
    public string? PostalCode { get; set; }

    public string? Ntn { get; set; }
    public string? Strn { get; set; }
    public string? OtherRegistrationNo { get; set; }

    /// <summary>FK into <c>trp.Currencies</c> (BR-C3). "Default PKR"; "changing it is blocked once any invoice
    /// exists" — no Invoice table exists yet in this module, so nothing enforces that yet.</summary>
    public string CurrencyCode { get; set; } = "PKR";
    public int PaymentTermsDays { get; set; } = 30;
    public decimal? CreditLimit { get; set; }

    public string Status { get; set; } = CustomerStatuses.Draft;
    public string? InactiveReason { get; set; }
    public string? Remarks { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

public static class CustomerStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public static readonly IReadOnlyList<string> All = [Draft, Active, Inactive];
}
