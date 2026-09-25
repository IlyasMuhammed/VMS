using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>Fuel fills recorded against a trip (§27) — always against the trip's own vehicle. "Vehicle-only fuel
/// (TripId null) for depot fills" is Recommended Design, not required, and has no endpoint of its own in §47.2's
/// literal list either (only the trip-scoped <c>/api/trips/{id}/fuel</c>) — not built, documented rather than
/// silently narrowed.</summary>
internal sealed class TripFuel : ITenantScopedEntity, IAuditRooted
{
    public long TripFuelId { get; set; }
    public Guid TenantId { get; set; }
    public long TripId { get; set; }

    public AuditRoot GetAuditRoot() => new("Trip", TripId.ToString());

    public int VehicleId { get; set; }
    public DateTime FuelDateTime { get; set; }
    public string FuelType { get; set; } = FuelTypes.Diesel;
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public decimal? Odometer { get; set; }
    public string? StationName { get; set; }
    public int? CityId { get; set; }
    public string PaymentMethod { get; set; } = FuelPaymentMethods.Cash;
    public int? FuelCardId { get; set; }
    public string? OtherPaymentText { get; set; }
    /// <summary>An already-uploaded <see cref="TripDocument"/> (a receipt) — reuses that upload path rather than
    /// a fourth near-duplicate multipart endpoint, the same choice CC-16 made for an issue's own photo.</summary>
    public long? AttachmentId { get; set; }
    public string? Remarks { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Source { get; set; } = TripEventSources.Manual;

    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
    public int? VoidedBy { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
}

public static class FuelTypes
{
    public const string Diesel = "Diesel";
    public const string Petrol = "Petrol";
    public const string Cng = "CNG";
    public const string Other = "Other";
    public static readonly IReadOnlyList<string> All = [Diesel, Petrol, Cng, Other];
}

public static class FuelPaymentMethods
{
    public const string Cash = "Cash";
    public const string FuelCard = "FuelCard";
    public const string Other = "Other";
    public static readonly IReadOnlyList<string> All = [Cash, FuelCard, Other];
}
