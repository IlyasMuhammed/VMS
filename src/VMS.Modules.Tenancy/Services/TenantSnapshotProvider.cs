using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VMS.Modules.Tenancy.Data;
using VMS.Shared.Common;

namespace VMS.Modules.Tenancy.Services;

/// <summary>
/// Cached 5 minutes per tenant so tenant resolution does not hit the database on every request;
/// <see cref="TenantService"/> invalidates the entry on writes so a deactivation is felt at once.
/// </summary>
internal sealed class TenantSnapshotProvider(TenancyDbContext db, IMemoryCache cache) : ITenantSnapshotProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static string CacheKey(Guid tenantId) => $"tenant-snapshot:{tenantId}";

    public async Task<TenantSnapshot?> GetSnapshotAsync(Guid tenantId)
    {
        if (cache.TryGetValue<TenantSnapshot>(CacheKey(tenantId), out var cached))
            return cached;

        var isActive = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (bool?)t.IsActive)
            .FirstOrDefaultAsync();

        if (isActive is null) return null;

        var snapshot = new TenantSnapshot(isActive.Value);
        cache.Set(CacheKey(tenantId), snapshot, CacheTtl);
        return snapshot;
    }

    public void Invalidate(Guid tenantId) => cache.Remove(CacheKey(tenantId));
}
