using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace VMS.Shared.Common;

public static class TenantContextExtensions
{
    /// <summary>Registered once, centrally, before any module — every tenant-scoped DbContext injects ITenantContext.</summary>
    public static IServiceCollection AddTenantContext(this IServiceCollection services)
    {
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<ITenantContext, TenantContext>();
        return services;
    }
}
