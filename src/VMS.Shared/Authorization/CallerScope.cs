using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace VMS.Shared.Authorization;

/// <summary>
/// The caller's data scope (§23B.4), read from the JWT the same way <c>ITenantContext</c> reads the tenant — a
/// plain claim, no per-request DB call. Mirrors the widest scope across every role the caller holds (a user with
/// several roles sees the union of what each one reaches, same as their permissions). A Super Admin, or any request
/// with no scope claim (a background job, an anonymous endpoint), is unscoped: <see cref="ScopeType"/> is All branches.
/// </summary>
public interface ICallerScope
{
    string ScopeType { get; }
    /// <summary>Only meaningful for Own branch.</summary>
    Guid? BranchId { get; }
    /// <summary>§23B.1: the Business Partner this caller is the same person as, when they are also a driver — what Own vehicles filters by.</summary>
    int? LinkedPartnerId { get; }
    int UserId { get; }
}

internal sealed class CallerScope(IHttpContextAccessor accessor) : ICallerScope
{
    private System.Security.Claims.ClaimsPrincipal? User => accessor.HttpContext?.User;

    public string ScopeType
    {
        get
        {
            if (User?.Identity?.IsAuthenticated != true || User.IsSuperAdmin()) return ScopeTypes.AllBranches;
            var claim = User.FindFirst("scope")?.Value;
            return string.IsNullOrEmpty(claim) ? ScopeTypes.AllBranches : claim;
        }
    }

    public Guid? BranchId => Guid.TryParse(User?.FindFirst("scopeBranchId")?.Value, out var id) ? id : null;

    public int? LinkedPartnerId => int.TryParse(User?.FindFirst("linkedPartnerId")?.Value, out var id) ? id : null;

    public int UserId => User?.GetUserId() ?? 0;
}

public static class CallerScopeExtensions
{
    /// <summary>Registered once, centrally, before any module — alongside <c>AddTenantContext</c>.</summary>
    public static IServiceCollection AddCallerScope(this IServiceCollection services)
    {
        services.AddScoped<ICallerScope, CallerScope>();
        return services;
    }
}
