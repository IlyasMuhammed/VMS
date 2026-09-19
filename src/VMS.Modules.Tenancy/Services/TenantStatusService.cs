using Microsoft.EntityFrameworkCore;
using VMS.Modules.Tenancy.Data;
using VMS.Shared.Common;

namespace VMS.Modules.Tenancy.Services;

internal sealed class TenantStatusService(TenancyDbContext db) : ITenantStatusService
{
    public Task<bool> IsTenantActiveAsync(Guid tenantId) =>
        db.Tenants.Where(t => t.Id == tenantId).Select(t => t.IsActive).FirstOrDefaultAsync();
}

internal sealed class SuperAdminService(TenancyDbContext db) : ISuperAdminService
{
    public Task<bool> IsSuperAdminAsync(int userId) =>
        db.SuperAdminUsers.AnyAsync(s => s.UserId == userId);

    public async Task GrantAsync(int userId)
    {
        if (await IsSuperAdminAsync(userId)) return;
        db.SuperAdminUsers.Add(new Domain.SuperAdminUser { UserId = userId, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
}
