using Microsoft.AspNetCore.Http;

namespace VMS.Shared.Common;

/// <summary>
/// The ambient tenant for code with no HTTP request to read one from — a nightly job, looping tenant by tenant. Flows with the
/// async call chain (<see cref="AsyncLocal{T}"/>), so setting it inside one tenant's iteration never leaks into a concurrent
/// request's or another tenant's. Nothing but a background host sets this; a request is always its own JWT's tenant.
/// </summary>
public static class BackgroundTenantScope
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public static Guid? TenantId => Current.Value;

    /// <summary>Runs <paramref name="action"/> as one tenant, then restores whatever the ambient tenant was before (usually none).</summary>
    public static async Task RunAsAsync(Guid tenantId, Func<Task> action)
    {
        var previous = Current.Value;
        Current.Value = tenantId;
        try { await action(); }
        finally { Current.Value = previous; }
    }
}

internal sealed class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public TenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid TenantId
    {
        get
        {
            var claim = _accessor.HttpContext?.User?.FindFirst("tenantId")?.Value;
            if (Guid.TryParse(claim, out var id)) return id;
            return BackgroundTenantScope.TenantId ?? Guid.Empty;
        }
    }

    public bool IsSuperAdmin
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated == true)
                // Stamped into the JWT at login/refresh from the SuperAdminUsers table — the sole
                // authoritative source. A plain claim read: no per-request DB call.
                return user.FindFirst("is_super_admin")?.Value == "true";

            // A background job impersonating one tenant is scoped to it, not bypassing every tenant's filter.
            if (BackgroundTenantScope.TenantId is not null) return false;

            // No authenticated principal and no ambient tenant (anonymous endpoint, or startup seeding):
            // no tenant can be known in advance, so bypass rather than scope to nothing.
            return true;
        }
    }
}
