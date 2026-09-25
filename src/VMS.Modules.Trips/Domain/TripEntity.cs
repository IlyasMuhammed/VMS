using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>A fixed or open trip (FSD §21/§22/§46.4). CC-12 builds Fixed trips only — Open trips (CC-13) reuse this
/// same table, adding their own columns then, the same incremental-schema-growth pattern every other task in this
/// register follows rather than pre-building columns nothing yet uses.</summary>
internal sealed class Trip : ITenantScopedEntity, IAuditRooted
{
    public long TripId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("Trip", TripId.ToString());

    public string TripNumber { get; set; } = string.Empty;
    public string TripType { get; set; } = TripTypes.Fixed;
    public int CustomerId { get; set; }
    /// <summary>§23: duplicate behaviour (Allow/Warn/Block) per the customer's own billing configuration; unique
    /// only in the sense that behaviour enforces, never at the database level (Warn/Allow both permit a repeat).</summary>
    public string? CustomerTripReference { get; set; }
    public long? TripConfigurationId { get; set; }
    public int? RouteId { get; set; }
    public int VehicleId { get; set; }

    /// <summary>The driver on the trip — null until §20's own rule 3: "if none exists, Driver is empty and
    /// required before the trip moves to Assigned" (CC-15's job to enforce at that transition, not here).</summary>
    public int? DriverId { get; set; }
    /// <summary>What the system proposed (the vehicle's own default) before any override — null when the vehicle
    /// had no default to propose in the first place, in which case providing a driver is not an override at all.</summary>
    public int? DefaultDriverId { get; set; }
    public bool IsDriverOverridden { get; set; }
    public string? DriverOverrideReason { get; set; }

    public DateOnly TripDate { get; set; }
    public DateTime? PlannedStart { get; set; }
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }
    public decimal? StartOdometer { get; set; }
    public decimal? EndOdometer { get; set; }

    // ── Open trips only (§22, CC-13). Null for Fixed trips, which use RouteId/TripConfigurationId instead. ──────
    public string? FromLocationType { get; set; }
    public int? FromCityId { get; set; }
    public string? FromOtherLocationType { get; set; }
    public string? FromOtherLocationName { get; set; }
    public int? FromOtherNearestCityId { get; set; }
    public string? ToLocationType { get; set; }
    public int? ToCityId { get; set; }
    public string? ToOtherLocationType { get; set; }
    public string? ToOtherLocationName { get; set; }
    public int? ToOtherNearestCityId { get; set; }
    public bool IsRoundTrip { get; set; }

    /// <summary>The rate snapshot (§26) — kept even if the <see cref="TripRate"/> row is later edited or deleted.</summary>
    public long? TripRateId { get; set; }
    public decimal? TripRateAmount { get; set; }
    public DateOnly? RateEffectiveFrom { get; set; }
    public DateOnly? RateEffectiveTo { get; set; }
    public string RateSource { get; set; } = TripRateSources.Missing;
    public decimal? TripAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public bool RateMissing { get; set; }

    /// <summary>Full lifecycle (§24, CC-15).</summary>
    public string Status { get; set; } = TripStatuses.Draft;
    /// <summary>What <see cref="Status"/> was the moment it went On Hold — "resume returns to previous status"
    /// (§24). Null whenever the trip is not currently On Hold.</summary>
    public string? HeldFromStatus { get; set; }
    public string? HoldReason { get; set; }
    public string? CancelReason { get; set; }
    /// <summary>Set once, on the transition to Completed: "the ActualEnd date in the tenant's business time zone,
    /// which decides the billing period" (§24, §32.1 — the later invoicing task reads this, not ActualEnd itself,
    /// since ActualEnd is a UTC instant and this is already the tenant's own business date).</summary>
    public DateOnly? CompletionDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? InactiveReason { get; set; }
    public long? InvoiceId { get; set; }
    public string? Remarks { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class TripTypes
{
    public const string Fixed = "Fixed";
    public const string Open = "Open";
    public static readonly IReadOnlyList<string> All = [Fixed, Open];
}

/// <summary>Whether a trip's From/To/intermediate stop is a known <see cref="City"/> or a one-off site (§22).</summary>
public static class TripLocationTypes
{
    public const string City = "City";
    public const string Other = "Other";
    public static readonly IReadOnlyList<string> All = [City, Other];
}

/// <summary>§22's own list for an "Other Location."</summary>
public static class OtherLocationTypes
{
    public const string Warehouse = "Warehouse";
    public const string Factory = "Factory";
    public const string CustomerSite = "CustomerSite";
    public const string Depot = "Depot";
    public const string Terminal = "Terminal";
    public const string ConstructionSite = "ConstructionSite";
    public const string Other = "Other";
    public static readonly IReadOnlyList<string> All = [Warehouse, Factory, CustomerSite, Depot, Terminal, ConstructionSite, Other];
}

/// <summary>An open trip's intermediate stop (§22: "Ordered; City or Other") — the From/To themselves live directly
/// on <see cref="Trip"/>, not as rows here.</summary>
internal sealed class TripStop : ITenantScopedEntity, IAuditRooted
{
    public long TripStopId { get; set; }
    public Guid TenantId { get; set; }
    public long TripId { get; set; }

    public AuditRoot GetAuditRoot() => new("Trip", TripId.ToString());

    public int Sequence { get; set; }
    public string LocationType { get; set; } = TripLocationTypes.City;
    public int? CityId { get; set; }
    public string? OtherLocationType { get; set; }
    public string? OtherLocationName { get; set; }
    public int? OtherNearestCityId { get; set; }
}

public static class TripRateSources
{
    public const string Configured = "Configured";
    public const string Manual = "Manual";
    public const string Missing = "Missing";
}

/// <summary>The full state diagram (§24, CC-15's own task) named here so every later task shares one set of
/// spellings — CC-12 itself only ever assigns <see cref="Draft"/>.</summary>
public static class TripStatuses
{
    public const string Draft = "Draft";
    public const string Planned = "Planned";
    public const string Assigned = "Assigned";
    public const string Started = "Started";
    public const string InTransit = "InTransit";
    public const string AtPickup = "AtPickup";
    public const string Loaded = "Loaded";
    public const string AtDelivery = "AtDelivery";
    public const string Delivered = "Delivered";
    public const string Completed = "Completed";
    public const string OnHold = "OnHold";
    public const string Cancelled = "Cancelled";
    public static readonly IReadOnlyList<string> All =
        [Draft, Planned, Assigned, Started, InTransit, AtPickup, Loaded, AtDelivery, Delivered, Completed, OnHold, Cancelled];
}
