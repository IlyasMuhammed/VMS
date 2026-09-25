using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Data;
using VMS.Shared.Common;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Vehicles.Services;

/// <summary>Answers other modules' questions about vehicles (the documents module names one on its Documents tab and register) without letting them read the vehicle table.</summary>
internal sealed class VehicleDirectory(VehicleDbContext db, ITenantContext tenantContext) : IVehicleDirectory
{
    public async Task<VehicleInfo?> FindAsync(int vehicleId, CancellationToken cancellationToken = default) =>
        (await FindManyAsync([vehicleId], cancellationToken)).GetValueOrDefault(vehicleId);

    public async Task<IReadOnlyDictionary<int, VehicleInfo>> FindManyAsync(IEnumerable<int> vehicleIds, CancellationToken cancellationToken = default)
    {
        var ids = vehicleIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, VehicleInfo>();

        var tenant = tenantContext.TenantId;
        var rows = await db.Vehicles.AsNoTracking().Where(v => v.TenantId == tenant && !v.IsDeleted && ids.Contains(v.VehicleId))
            .Select(v => new { v.VehicleId, v.VehicleCode, v.RegistrationNo, v.IsInFleet, v.CurrentCategory, v.DefaultDriverId }).ToListAsync(cancellationToken);
        return rows.ToDictionary(v => v.VehicleId, v => new VehicleInfo(v.VehicleId, v.VehicleCode, v.RegistrationNo, v.IsInFleet, v.CurrentCategory, v.DefaultDriverId));
    }

    public async Task<IReadOnlyList<VehicleInfo>> AllAsync(CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.TenantId;
        var rows = await db.Vehicles.AsNoTracking().Where(v => v.TenantId == tenant && !v.IsDeleted)
            .Select(v => new { v.VehicleId, v.VehicleCode, v.RegistrationNo, v.IsInFleet, v.CurrentCategory, v.DefaultDriverId }).ToListAsync(cancellationToken);
        return rows.Select(v => new VehicleInfo(v.VehicleId, v.VehicleCode, v.RegistrationNo, v.IsInFleet, v.CurrentCategory, v.DefaultDriverId)).ToList();
    }
}
