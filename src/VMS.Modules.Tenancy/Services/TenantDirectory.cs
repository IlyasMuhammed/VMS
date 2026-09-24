using Microsoft.EntityFrameworkCore;
using VMS.Modules.Tenancy.Data;
using VMS.Shared.Tenancy;

namespace VMS.Modules.Tenancy.Services;

/// <summary>The Tenancy module's own answer to <see cref="ITenantDirectory"/>: every active tenant, for a nightly job to loop over.</summary>
internal sealed class TenantDirectory(TenancyDbContext db) : ITenantDirectory
{
    public async Task<IReadOnlyList<Guid>> ActiveTenantIdsAsync(CancellationToken cancellationToken = default) =>
        await db.Tenants.AsNoTracking().Where(t => t.IsActive).Select(t => t.Id).ToListAsync(cancellationToken);
}
