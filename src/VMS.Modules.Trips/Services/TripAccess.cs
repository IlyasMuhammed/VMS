using VMS.Modules.Trips.Domain;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;

namespace VMS.Modules.Trips.Services;

/// <summary>
/// The one recurring authorization shape this module keeps needing beyond a flat permission: "the back-office
/// permission, or being this trip's own assigned driver" — the driver-app channel table (§23) lists status steps,
/// documents, POD and issues as all available "for own trips." CC-15's own <c>TripLifecycleService</c> wrote its
/// own copy of this check before this file existed and is left as-is (already shipped and tested, not worth
/// churning); every service this task (CC-16) adds shares this one instead of redefining it slightly differently
/// three more times.
/// </summary>
internal static class TripAccess
{
    public static bool IsOwnDriver(Trip trip, ICallerScope scope) => trip.DriverId is not null && scope.LinkedPartnerId == trip.DriverId;

    /// <summary>The one check every wrapper below applies with a different permission/label — kept as a single
    /// implementation so a fifth caller (this task's own <c>TripFuelService</c>) needs only a one-line wrapper.</summary>
    public static void Require(Trip trip, TripCaller caller, ICallerScope scope, string permission, string permissionLabel, string action)
    {
        if (caller.Has(permission) || IsOwnDriver(trip, scope)) return;
        throw new ForbiddenException($"{action} needs the {permissionLabel} permission, or being the trip's own assigned driver.");
    }

    public static void RequireViewOrOwnDriver(Trip trip, TripCaller caller, ICallerScope scope, string action) =>
        Require(trip, caller, scope, PermissionCodes.TRP_TRIP_VIEW, "Trip.View", action);

    public static void RequireStatusOrOwnDriver(Trip trip, TripCaller caller, ICallerScope scope, string action) =>
        Require(trip, caller, scope, PermissionCodes.TRP_TRIP_STATUS, "Trip.Status", action);

    public static void RequireDocumentsOrOwnDriver(Trip trip, TripCaller caller, ICallerScope scope, string action) =>
        Require(trip, caller, scope, PermissionCodes.TRP_TRIP_DOCUMENTS, "Trip.Documents", action);

    public static void RequireFuelOrOwnDriver(Trip trip, TripCaller caller, ICallerScope scope, string action) =>
        Require(trip, caller, scope, PermissionCodes.TRP_FUEL_EDIT, "Fuel.Edit", action);
}
