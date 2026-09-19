using Microsoft.EntityFrameworkCore;
using VMS.Modules.Tenancy.Domain;
using VMS.Shared.Common;

namespace VMS.Modules.Tenancy.Data;

internal sealed class TenancyDataSeeder(TenancyDbContext db)
{
    /// <summary>Seeds the platform's own tenant. Idempotent — safe to run on every startup.</summary>
    public async Task SeedAsync()
    {
        if (await db.Tenants.AnyAsync(t => t.Id == TenantDefaults.PlatformTenantId))
            return;

        db.Tenants.Add(new Tenant
        {
            Id = TenantDefaults.PlatformTenantId,
            TenantCode = TenantDefaults.PlatformTenantCode,
            TenantName = "VMS Platform",
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
