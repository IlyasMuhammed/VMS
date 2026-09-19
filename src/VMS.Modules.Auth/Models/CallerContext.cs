using System.Security.Claims;
using VMS.Shared.Authorization;

namespace VMS.Modules.Auth.Models;

/// <summary>Who is making the request, read once from the JWT so services can make authorization decisions.</summary>
public sealed record CallerContext(int UserId, Guid TenantId, bool IsSuperAdmin, IReadOnlySet<string> Permissions)
{
    public bool Has(string permission) => IsSuperAdmin || Permissions.Contains(permission);
}

public static class CallerContextExtensions
{
    public static CallerContext ToCaller(this ClaimsPrincipal user) => new(
        user.GetUserId(),
        user.GetTenantId(),
        user.IsSuperAdmin(),
        user.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());
}
