namespace VMS.Modules.Trips.Services;

/// <summary>Who is acting, for the one check in this module that is conditional rather than a flat
/// <c>[RequirePermission]</c> gate (§20's driver-override permission) — mirrors
/// <c>VMS.Modules.Vehicles.Services.VehicleCaller</c>'s exact shape.</summary>
public sealed record TripCaller(int UserId, string? UserName, bool IsSuperAdmin, IReadOnlySet<string> Permissions)
{
    public bool Has(string permission) => IsSuperAdmin || Permissions.Contains(permission);
}
