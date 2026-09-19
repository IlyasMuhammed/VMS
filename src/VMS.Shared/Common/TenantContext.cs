using Microsoft.AspNetCore.Http;

namespace VMS.Shared.Common;

internal sealed class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public TenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid TenantId
    {
        get
        {
            var claim = _accessor.HttpContext?.User?.FindFirst("tenantId")?.Value;
            return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
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

            // No authenticated principal (anonymous endpoint, or no HttpContext at all — startup
            // seeding): no tenant can be known in advance, so bypass rather than scope to nothing.
            return true;
        }
    }
}
