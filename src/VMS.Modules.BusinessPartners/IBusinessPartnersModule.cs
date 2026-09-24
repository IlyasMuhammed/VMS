using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VMS.Modules.BusinessPartners.Data;
using VMS.Modules.BusinessPartners.Services;
using VMS.Shared.Partners;

namespace VMS.Modules.BusinessPartners;

public interface IBusinessPartnersModule { }

public static class BusinessPartnersModuleExtensions
{
    public static IServiceCollection AddBusinessPartnersModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connString = configuration["Data:mainOrg"]
            ?? throw new InvalidOperationException("Connection string 'Data:mainOrg' is missing.");

        services.AddDbContext<PartnerDbContext>(options =>
            options.UseSqlServer(connString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", PartnerDbContext.Schema);
                sql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(500), null);
            }));

        services.AddScoped<IPartnerDirectory, PartnerDirectory>();
        services.AddScoped<DuplicateService>();
        services.AddScoped<IPartnerService, PartnerService>();
        services.AddScoped<IPartnerQueryService, PartnerQueryService>();
        return services;
    }

    /// <summary>Applies pending migrations. Runs after the Core module (audit rows) and the Tenancy module.</summary>
    public static IApplicationBuilder UseBusinessPartnersModule(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<PartnerDbContext>().Database.Migrate();
        return app;
    }
}
