namespace VMS.Modules.Trips.Models;

public sealed class CustomerModel
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public int CountryId { get; set; }
    public string? ProvinceState { get; set; }
    public int? CityId { get; set; }
    public string? PostalCode { get; set; }
    public string? Ntn { get; set; }
    public string? Strn { get; set; }
    public string? OtherRegistrationNo { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public int PaymentTermsDays { get; set; }
    public decimal? CreditLimit { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? InactiveReason { get; set; }
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveCustomerRequest
{
    /// <summary>Auto-suggested (<c>CUS-00001</c>) when left blank; editable before first use (FSD §10).</summary>
    public string? CustomerCode { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public int? CountryId { get; set; }
    public string? ProvinceState { get; set; }
    public int? CityId { get; set; }
    public string? PostalCode { get; set; }
    public string? Ntn { get; set; }
    public string? Strn { get; set; }
    public string? OtherRegistrationNo { get; set; }
    public string? CurrencyCode { get; set; }
    public int? PaymentTermsDays { get; set; }
    public decimal? CreditLimit { get; set; }
    public string? Remarks { get; set; }
    /// <summary>Required by every save once the record exists (optimistic concurrency); ignored on create.</summary>
    public string? RowVersion { get; set; }
}

public sealed class ChangeCustomerStatusRequest
{
    /// <summary>Required when setting Inactive; an override reason when force-activating past a failed checklist.</summary>
    public string? Reason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ActivationCheckModel
{
    public bool CanActivate { get; set; }
    public IReadOnlyList<string> MissingItems { get; set; } = [];
}

/// <summary>§48.1's own common pattern: "History button on every record opens audit rows (who, when, field, old
/// → new, reason)." Reads the shared, Core-owned `core.AuditEntries` table directly — the same pattern every
/// other module's own history endpoint already hand-rolls (there is no shared history service to call into).
/// No separate "lifecycle" list the way Vehicle has one: a customer's own status changes already show up as
/// ordinary Status field-change rows in <see cref="Changes"/>, so a second table isn't needed to see them.</summary>
public sealed class CustomerHistoryChange
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
    /// <summary>True when the caller lacks the field permission the change is restricted to (BR-SEC-003) — the
    /// old/new values are withheld, not the row itself, so the change is still visible as having happened.</summary>
    public bool Restricted { get; set; }
}

public sealed class CustomerHistory
{
    public VMS.Shared.Pagination.PaginatedResponse<CustomerHistoryChange> Changes { get; set; } = new();
}
