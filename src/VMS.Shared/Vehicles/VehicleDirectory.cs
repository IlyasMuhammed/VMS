namespace VMS.Shared.Vehicles;

/// <summary>
/// What another module needs to know about a vehicle it points at. <see cref="IsOperational"/> and
/// <see cref="Category"/> were added for the Trip/Billing/Invoicing/Customer Ledger FSD's own vehicle-assignment
/// rule (§19: "Vehicle must be operational... not sold/disposed/under-repair-blocked"); <see cref="DefaultDriverId"/>
/// for §20's driver auto-fill. All additive, at the end, so the Documents module's own already-shipped positional
/// construction and property reads are unaffected.
/// </summary>
public sealed record VehicleInfo(int Id, string VehicleCode, string RegistrationNo, bool IsOperational = false, string? Category = null, int? DefaultDriverId = null);

/// <summary>
/// Looks vehicles up for the modules that point at one — the documents module names a vehicle on its Documents tab and the
/// fleet-wide Document Register without reading the vehicle table itself. Only the caller's own tenant's vehicles are visible.
/// </summary>
public interface IVehicleDirectory
{
    Task<VehicleInfo?> FindAsync(int vehicleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, VehicleInfo>> FindManyAsync(IEnumerable<int> vehicleIds, CancellationToken cancellationToken = default);

    /// <summary>Every vehicle of the tenant, for a report that must consider all of them (the Missing Documents report). Phase 1's NFR budget (under 2,000 vehicles) keeps a full scan inside its 2-second target.</summary>
    Task<IReadOnlyList<VehicleInfo>> AllAsync(CancellationToken cancellationToken = default);
}
