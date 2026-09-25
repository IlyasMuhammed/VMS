namespace VMS.Modules.Trips.Models;

public sealed class CustomerContactModel
{
    public long CustomerContactId { get; set; }
    public int CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string Mobile1 { get; set; } = string.Empty;
    public string? Mobile2 { get; set; }
    public string? Telephone { get; set; }
    public string? Email { get; set; }
    public string? AvailabilityTime { get; set; }
    public IReadOnlyList<string> Purpose { get; set; } = [];
    public bool IsPrimary { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class SaveCustomerContactRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string Mobile1 { get; set; } = string.Empty;
    public string? Mobile2 { get; set; }
    public string? Telephone { get; set; }
    public string? Email { get; set; }
    public string? AvailabilityTime { get; set; }
    public IReadOnlyList<string>? Purpose { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>A save's result, plus any non-blocking warning the FSD calls for (e.g. deactivating a customer's last
/// active contact — §11: "warning if the last one is deactivated", never a block).</summary>
public sealed class CustomerContactSaveResult
{
    public CustomerContactModel Contact { get; set; } = null!;
    public IReadOnlyList<string> Warnings { get; set; } = [];
}

public sealed class CustomerBillingAddressModel
{
    public long CustomerBillingAddressId { get; set; }
    public int CustomerId { get; set; }
    public string AddressName { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public int CityId { get; set; }
    public string? ProvinceState { get; set; }
    public int CountryId { get; set; }
    public string? PostalCode { get; set; }
    public string? Ntn { get; set; }
    public string? Strn { get; set; }
    public bool IsDefault { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class SaveCustomerBillingAddressRequest
{
    public string AddressName { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public int CityId { get; set; }
    public string? ProvinceState { get; set; }
    public int? CountryId { get; set; }
    public string? PostalCode { get; set; }
    public string? Ntn { get; set; }
    public string? Strn { get; set; }
    public bool IsDefault { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}
