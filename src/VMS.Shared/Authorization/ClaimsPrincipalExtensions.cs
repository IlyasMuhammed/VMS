using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace VMS.Shared.Authorization;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated user's id (JWT <c>sub</c>), or 0 when absent or invalid.</summary>
    public static int GetUserId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return int.TryParse(raw, out var id) ? id : 0;
    }

    public static Guid GetTenantId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("tenantId")?.Value, out var id) ? id : Guid.Empty;

    public static bool IsSuperAdmin(this ClaimsPrincipal user) =>
        user.FindFirst("is_super_admin")?.Value == "true";

    public static bool HasPermission(this ClaimsPrincipal user, string code) =>
        user.IsSuperAdmin() || user.Claims.Any(c => c.Type == "permission" && c.Value == code);
}
