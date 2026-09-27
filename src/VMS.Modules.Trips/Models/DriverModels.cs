namespace VMS.Modules.Trips.Models;

// ── Create Trip in the app (§43) — "amounts hidden, rate resolved in the background." ──────────────────────

public sealed class DriverConfigurationOptionModel
{
    public long TripConfigurationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? RouteLabel { get; set; }
}

public sealed class DriverCustomerOptionModel
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public IReadOnlyList<DriverConfigurationOptionModel> Configurations { get; set; } = [];
}

public sealed class DriverCityOptionModel
{
    public int CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
}

/// <summary>§47.2's own literal <c>GET /api/driver/trip-options</c>: "customers, configurations for own vehicle,
/// cities."</summary>
public sealed class DriverTripOptionsModel
{
    public int VehicleId { get; set; }
    public string VehicleRegistrationNo { get; set; } = string.Empty;
    /// <summary>Only customers with at least one Active configuration this vehicle is currently allowed on
    /// (§43: "only customers with configurations that allow the driver's vehicle").</summary>
    public IReadOnlyList<DriverCustomerOptionModel> Customers { get; set; } = [];
    public IReadOnlyList<DriverCityOptionModel> Cities { get; set; } = [];
}

public sealed class CreateDriverFixedTripRequest
{
    public int CustomerId { get; set; }
    public long TripConfigurationId { get; set; }
    public DateOnly TripDate { get; set; }
    public string? CustomerTripReference { get; set; }
}

public sealed class CreateDriverOpenTripRequest
{
    public int CustomerId { get; set; }
    public LocationInput From { get; set; } = new();
    public LocationInput To { get; set; } = new();
    public IReadOnlyList<LocationInput> Stops { get; set; } = [];
    public bool IsRoundTrip { get; set; }
    public DateOnly TripDate { get; set; }
    public string? CustomerTripReference { get; set; }
}

// ── My Trips (§43) — amounts, rates, invoices and other drivers' data are never returned here. ──────────────

public sealed class DriverTripListItem
{
    public long TripId { get; set; }
    public string TripNumber { get; set; } = string.Empty;
    /// <summary>§43's own card content: "customer short name," never the full legal name.</summary>
    public string CustomerShortName { get; set; } = string.Empty;
    public string? RouteLabel { get; set; }
    public string VehicleRegistrationNo { get; set; } = string.Empty;
    public DateOnly TripDate { get; set; }
    public DateTime? PlannedStart { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>§43's own three My Trips card sections.</summary>
public sealed class DriverTripsModel
{
    public IReadOnlyList<DriverTripListItem> Today { get; set; } = [];
    public IReadOnlyList<DriverTripListItem> Upcoming { get; set; } = [];
    public IReadOnlyList<DriverTripListItem> Recent { get; set; } = [];
}

public sealed class DriverTripEventModel
{
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>The app's own Trip screen: "route stops with tick marks, customer reference, big next-step button."
/// <see cref="NextStatuses"/> is more than one only at a branch point (e.g. Started → InTransit or AtPickup) —
/// the app's own "one big button" framing holds for the ordinary, non-branching case.</summary>
public sealed class DriverTripDetailModel
{
    public long TripId { get; set; }
    public string TripNumber { get; set; } = string.Empty;
    public string TripType { get; set; } = string.Empty;
    public string CustomerShortName { get; set; } = string.Empty;
    public string? CustomerTripReference { get; set; }
    public string? RouteLabel { get; set; }
    public IReadOnlyList<LocationModel> Stops { get; set; } = [];
    public string VehicleRegistrationNo { get; set; } = string.Empty;
    public DateOnly TripDate { get; set; }
    public DateTime? PlannedStart { get; set; }
    public decimal? StartOdometer { get; set; }
    public decimal? EndOdometer { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? HoldReason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<string> NextStatuses { get; set; } = [];
    public IReadOnlyList<DriverTripEventModel> RecentEvents { get; set; } = [];
}

// ── Offline sync (§43, AC-54): "actions queue locally... sync on reconnect; server dedupes by ClientEventId."

public static class DriverSyncActionTypes
{
    public const string Step = "Step";
    public const string Fuel = "Fuel";
    public const string Expense = "Expense";
    public const string Issue = "Issue";
    public static readonly IReadOnlyList<string> All = [Step, Fuel, Expense, Issue];
}

public sealed class DriverSyncAction
{
    public string ActionType { get; set; } = string.Empty;
    public long TripId { get; set; }
    /// <summary>The same body <c>POST .../steps</c>, <c>/fuel</c>, <c>/expenses</c> or <c>/issues</c> would take
    /// (a <see cref="TransitionTripRequest"/>, <see cref="CreateTripFuelRequest"/>, etc.), serialized — resolved
    /// against <see cref="ActionType"/> when applied. For Step, the target status travels as its own
    /// <see cref="ToStatus"/> field rather than inside the payload, since <see cref="TransitionTripRequest"/>
    /// itself carries no status of its own (the office endpoint takes it as a route segment).</summary>
    public string? ToStatus { get; set; }
    public System.Text.Json.JsonElement Payload { get; set; }
}

public sealed class DriverSyncItemResult
{
    public string ActionType { get; set; } = string.Empty;
    public long TripId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public sealed class DriverSyncResultModel
{
    public IReadOnlyList<DriverSyncItemResult> Results { get; set; } = [];
}

// ── Pending review (§43, §47.2: "GET /api/trips/pending-review · POST /api/trips/{id}/release · /reject") ──

public sealed class TripPendingReviewItem
{
    public long TripId { get; set; }
    public string TripNumber { get; set; } = string.Empty;
    public string TripType { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int VehicleId { get; set; }
    public string VehicleRegistrationNo { get; set; } = string.Empty;
    public int? DriverId { get; set; }
    public string? DriverName { get; set; }
    public DateOnly TripDate { get; set; }
    /// <summary>True for a Fixed trip whose rate never resolved, or an Open trip still waiting for Operations'
    /// own amount — either way, <see cref="ReleaseTripRequest.TripAmount"/> is needed before this trip can be
    /// released.</summary>
    public bool RateMissing { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ReleaseTripRequest
{
    public string RowVersion { get; set; } = string.Empty;
    /// <summary>§43: "Operations enters the trip amount when reviewing" — only ever read for a trip whose
    /// <see cref="TripPendingReviewItem.RateMissing"/> was true (in practice, an Open trip created without one);
    /// ignored otherwise.</summary>
    public decimal? TripAmount { get; set; }
}

public sealed class RejectTripRequest
{
    public string Reason { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}
