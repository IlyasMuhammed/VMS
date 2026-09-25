namespace VMS.Modules.Trips.Models;

public sealed class TripFuelModel
{
    public long TripFuelId { get; set; }
    public long TripId { get; set; }
    public int VehicleId { get; set; }
    public DateTime FuelDateTime { get; set; }
    public string FuelType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public decimal? Odometer { get; set; }
    public string? StationName { get; set; }
    public int? CityId { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public int? FuelCardId { get; set; }
    public string? OtherPaymentText { get; set; }
    public long? AttachmentId { get; set; }
    public string? Remarks { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
    public int? VoidedBy { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = [];
}

public sealed class CreateTripFuelRequest
{
    /// <summary>Defaults to now.</summary>
    public DateTime? FuelDateTime { get; set; }
    public string FuelType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    /// <summary>Defaults to Quantity × Rate; a caller-supplied value differing by more than 1 (base currency
    /// unit) triggers a non-blocking warning, never a rejection (§27).</summary>
    public decimal? Amount { get; set; }
    public decimal? Odometer { get; set; }
    public string? StationName { get; set; }
    public int? CityId { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public int? FuelCardId { get; set; }
    public string? OtherPaymentText { get; set; }
    public long? AttachmentId { get; set; }
    public string? Remarks { get; set; }
    /// <summary>Only read when multi-currency is on (§13A/AC-65); forced to the tenant base otherwise.</summary>
    public string? CurrencyCode { get; set; }
}

public sealed class VoidTripFuelRequest
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>§27: "Fuel efficiency (km/litre) is derived per trip where odometers exist" — computed from the
/// trip's own Start/End odometer against the total litres logged, not from consecutive fuel-entry odometers
/// (simpler, and matches what the trip itself already records at Started/Delivered).</summary>
public sealed class TripFuelListModel
{
    public IReadOnlyList<TripFuelModel> Entries { get; set; } = [];
    public decimal TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal? FuelEfficiencyKmPerLitre { get; set; }
}
