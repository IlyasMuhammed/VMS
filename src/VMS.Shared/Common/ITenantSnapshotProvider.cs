namespace VMS.Shared.Common;

/// <summary>Per-tenant read model cached with a short TTL so TenantMiddleware does not hit the database on every request.</summary>
/// <param name="TimeZone">The tenant's operating time zone (an IANA name), if it has set one.</param>
public sealed record TenantSnapshot(bool IsActive, string? TimeZone = null);

public interface ITenantSnapshotProvider
{
    /// <summary>Null means the id resolves to no tenant (a stale or forged claim) — treat as inactive.</summary>
    Task<TenantSnapshot?> GetSnapshotAsync(Guid tenantId);

    /// <summary>Drops the cached entry so a status change takes effect on the very next request.</summary>
    void Invalidate(Guid tenantId);
}
