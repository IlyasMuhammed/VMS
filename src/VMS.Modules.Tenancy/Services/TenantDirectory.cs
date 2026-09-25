using Microsoft.EntityFrameworkCore;
using VMS.Modules.Tenancy.Data;
using VMS.Shared.Tenancy;

namespace VMS.Modules.Tenancy.Services;

/// <summary>The Tenancy module's own answer to <see cref="ITenantDirectory"/> (every active tenant, for a nightly
/// job to loop over) and <see cref="ITenantProfileDirectory"/> (one tenant's own identity, for a rendered
/// document's company header).</summary>
internal sealed class TenantDirectory(TenancyDbContext db) : ITenantDirectory, ITenantProfileDirectory
{
    public async Task<IReadOnlyList<Guid>> ActiveTenantIdsAsync(CancellationToken cancellationToken = default) =>
        await db.Tenants.AsNoTracking().Where(t => t.IsActive).Select(t => t.Id).ToListAsync(cancellationToken);

    public async Task<TenantProfile?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        return tenant is null ? null : new TenantProfile(tenant.Id, tenant.TenantName, tenant.Address, tenant.ContactEmail, tenant.ContactPhone);
    }
}
