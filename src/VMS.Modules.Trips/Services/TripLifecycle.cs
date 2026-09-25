using VMS.Modules.Trips.Domain;

namespace VMS.Modules.Trips.Services;

/// <summary>
/// §24's own transition table as a pure, DB-free-testable lookup (public for the same reason as
/// <c>TripRateOverlap</c>/<c>VMS.Modules.Vehicles.Services.ChargeSchedule</c>). "On Hold" resume and "Completed"
/// reopen are deliberately *not* rows here — their target depends on data (what status was held) or needs its own
/// extra permission and condition, so each is its own service method rather than a generic transition.
/// </summary>
public static class TripLifecycle
{
    /// <summary>Every (From, To) pair the table allows *without* skipping a step — anything else is either
    /// Cancel (allowed from anywhere non-terminal, its own row below) or a genuine skip needing
    /// <c>TRP.TRIP.SKIPSTATUS</c>.</summary>
    private static readonly HashSet<(string From, string To)> NormalEdges = new()
    {
        (TripStatuses.Draft, TripStatuses.Planned),
        (TripStatuses.Planned, TripStatuses.Assigned),
        (TripStatuses.Assigned, TripStatuses.Started),
        (TripStatuses.Started, TripStatuses.InTransit),
        (TripStatuses.Started, TripStatuses.AtPickup),
        (TripStatuses.InTransit, TripStatuses.AtPickup),
        (TripStatuses.InTransit, TripStatuses.AtDelivery),
        (TripStatuses.AtPickup, TripStatuses.Loaded),
        (TripStatuses.Loaded, TripStatuses.InTransit),
        (TripStatuses.Loaded, TripStatuses.AtDelivery),
        (TripStatuses.AtDelivery, TripStatuses.Delivered),
        (TripStatuses.Delivered, TripStatuses.Completed),
    };

    /// <summary>Every status a trip can be On Hold *from* — everything except the two terminal states and Hold itself.</summary>
    private static readonly HashSet<string> CanHoldFrom =
        [TripStatuses.Planned, TripStatuses.Assigned, TripStatuses.Started, TripStatuses.InTransit, TripStatuses.AtPickup, TripStatuses.Loaded, TripStatuses.AtDelivery, TripStatuses.Delivered];

    /// <summary>Every status Cancel is allowed from — everything except the two terminal states (Cancel is itself terminal).</summary>
    private static readonly HashSet<string> CanCancelFrom =
        [TripStatuses.Draft, TripStatuses.Planned, TripStatuses.Assigned, TripStatuses.Started, TripStatuses.InTransit,
         TripStatuses.AtPickup, TripStatuses.Loaded, TripStatuses.AtDelivery, TripStatuses.Delivered, TripStatuses.OnHold];

    public static bool IsNormalTransition(string from, string to) => NormalEdges.Contains((from, to));
    public static bool CanHold(string from) => CanHoldFrom.Contains(from);
    public static bool CanCancel(string from) => CanCancelFrom.Contains(from);

    /// <summary>§24's own edge case: "Cancel after Started requires Fleet/Admin" — Assigned and earlier is a plain
    /// back-office/driver cancel like any other transition; from Started onward it needs the stronger permission.</summary>
    public static bool CancelNeedsElevatedPermission(string from) => from is not (TripStatuses.Draft or TripStatuses.Planned or TripStatuses.Assigned);
}
