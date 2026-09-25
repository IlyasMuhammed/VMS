using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Models;

/// <summary>§48.1: "History button on every record" — one row per audited change to the trip itself or to any of
/// its child rows (events, stops, fuel, expenses, income, documents, POD, issues, rate history all root back to
/// "Trip" — see each entity's own <c>GetAuditRoot()</c>), newest first.</summary>
public sealed class TripHistoryChange
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

public sealed class TripHistory
{
    public PaginatedResponse<TripHistoryChange> Changes { get; set; } = new();
}

public sealed class TripModel
{
    public long TripId { get; set; }
    public string TripNumber { get; set; } = string.Empty;
    public string TripType { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? CustomerTripReference { get; set; }
    public long? TripConfigurationId { get; set; }
    public int? RouteId { get; set; }
    public int VehicleId { get; set; }
    public int? DriverId { get; set; }
    public int? DefaultDriverId { get; set; }
    public bool IsDriverOverridden { get; set; }
    public string? DriverOverrideReason { get; set; }
    public DateOnly TripDate { get; set; }
    public DateTime? PlannedStart { get; set; }
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }
    public decimal? StartOdometer { get; set; }
    public decimal? EndOdometer { get; set; }
    public long? TripRateId { get; set; }
    public decimal? TripRateAmount { get; set; }
    public DateOnly? RateEffectiveFrom { get; set; }
    public DateOnly? RateEffectiveTo { get; set; }
    public string RateSource { get; set; } = string.Empty;
    public decimal? TripAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public bool RateMissing { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? HeldFromStatus { get; set; }
    public string? HoldReason { get; set; }
    public string? CancelReason { get; set; }
    public DateOnly? CompletionDate { get; set; }
    public bool IsActive { get; set; }
    public string? InactiveReason { get; set; }
    public long? InvoiceId { get; set; }
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; set; } = [];

    // ── Open trips only (§22, CC-13) — null/empty for Fixed trips. ─────────────────────
    public LocationModel? From { get; set; }
    public LocationModel? To { get; set; }
    public IReadOnlyList<LocationModel> Stops { get; set; } = [];
    public bool IsRoundTrip { get; set; }
    /// <summary>§22: "the route label on invoices is built from stops, e.g. LHR → DGK" — computed here so
    /// whatever later reads a trip (an invoice line, a report) never re-derives it differently.</summary>
    public string? RouteLabel { get; set; }
}

public sealed class CreateFixedTripRequest
{
    public int CustomerId { get; set; }
    public long TripConfigurationId { get; set; }
    public int VehicleId { get; set; }
    /// <summary>Left blank to accept the vehicle's own default driver (§20); a value different from that default
    /// is an override, needing the TRP.TRIP.OVERRIDEDRIVER permission and a <see cref="DriverOverrideReason"/>.</summary>
    public int? DriverId { get; set; }
    public string? DriverOverrideReason { get; set; }
    public DateOnly TripDate { get; set; }
    public string? CustomerTripReference { get; set; }
    public DateTime? PlannedStart { get; set; }
    public string? Remarks { get; set; }
}
