using System.Text.Json;
using Microsoft.AspNetCore.Http;
using VMS.Shared.Common;
using VMS.Shared.Pagination;

namespace VMS.Shared.Middleware;

/// <summary>
/// Blocks an authenticated request with 401 if its tenant has been deactivated — checked on every
/// request, not just at login, so a tenant disabled mid-session is cut off within the snapshot
/// cache's TTL rather than when its long-lived refresh token finally expires. Registered after
/// UseAuthentication/UseAuthorization so HttpContext.User is populated. Anonymous and Super Admin
/// requests are bypassed: there is no tenant to resolve yet for login/refresh, and Super Admin
/// requests are meant to cross tenant boundaries.
/// </summary>
public sealed class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, ITenantSnapshotProvider snapshots)
    {
        if (context.User.Identity?.IsAuthenticated != true || tenantContext.IsSuperAdmin)
        {
            await _next(context);
            return;
        }

        var snapshot = await snapshots.GetSnapshotAsync(tenantContext.TenantId);
        if (snapshot is null || !snapshot.IsActive)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(
                ApiResponse.Fail("This tenant has been deactivated."),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return;
        }

        await _next(context);
    }
}
