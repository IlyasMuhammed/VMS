using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>The old/new snapshot recorded whenever a trip is re-priced (§26/§32.1): "records the old/new snapshot
/// in TripRateHistory." Both "Resolve missing rates" and "Re-price trips" write here — <see cref="Action"/> tells
/// them apart, since they have different permission and reason requirements but the same shape of change.</summary>
internal sealed class TripRateHistory : ITenantScopedEntity, IAuditRooted
{
    public long TripRateHistoryId { get; set; }
    public Guid TenantId { get; set; }
    public long TripId { get; set; }

    public AuditRoot GetAuditRoot() => new("Trip", TripId.ToString());

    public long? OldTripRateId { get; set; }
    public decimal? OldRateAmount { get; set; }
    public string OldRateSource { get; set; } = string.Empty;
    public string? OldCurrencyCode { get; set; }

    public long? NewTripRateId { get; set; }
    public decimal? NewRateAmount { get; set; }
    public string NewRateSource { get; set; } = string.Empty;
    public string? NewCurrencyCode { get; set; }

    public string Action { get; set; } = TripRateHistoryActions.Reprice;
    /// <summary>Required for Reprice (§26: "with ... a reason"); always null for Resolve Missing Rates, which
    /// carries no reason of its own — it is just filling in what should already have been there.</summary>
    public string? Reason { get; set; }
    public int PerformedBy { get; set; }
    public DateTime PerformedAtUtc { get; set; }
}

public static class TripRateHistoryActions
{
    public const string ResolveMissingRates = "ResolveMissingRates";
    public const string Reprice = "Reprice";
}
