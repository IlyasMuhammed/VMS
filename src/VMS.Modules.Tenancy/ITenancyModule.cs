using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VMS.Modules.Tenancy.Data;
using VMS.Modules.Tenancy.Services;
using VMS.Shared.Common;
using VMS.Shared.Tenancy;

namespace VMS.Modules.Tenancy;

public interface ITenancyModule { }

public static class TenancyModuleExtensions
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connString = configuration["Data:mainOrg"]
            ?? throw new InvalidOperationException("Connection string 'Data:mainOrg' is missing.");

        services.AddDbContext<TenancyDbContext>(options =>
            options.UseSqlServer(connString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", TenancyDbContext.Schema);
                sql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(500), null);
            }));

        services.AddMemoryCache();

        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<ITenantStatusService, TenantStatusService>();
        services.AddScoped<ISuperAdminService, SuperAdminService>();
        services.AddScoped<ITenantSnapshotProvider, TenantSnapshotProvider>();
        services.AddScoped<ITenantDirectory, TenantDirectory>();
        services.AddScoped<ITenantProfileDirectory, TenantDirectory>();
        services.AddScoped<TenancyDataSeeder>();

        return services;
    }

    /// <summary>Applies pending migrations and seeds the platform tenant. Runs before the Auth module.</summary>
    public static IApplicationBuilder UseTenancyModule(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenancyDbContext>().Database.Migrate();
        scope.ServiceProvider.GetRequiredService<TenancyDataSeeder>().SeedAsync().GetAwaiter().GetResult();
        return app;
    }
}
