using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VMS.Modules.Core.Data;
using VMS.Modules.Core.Files;
using VMS.Modules.Core.Services;
using VMS.Shared.Branches;
using VMS.Shared.Files;
using VMS.Shared.Lookups;
using VMS.Shared.Numbering;

namespace VMS.Modules.Core;

public interface ICoreModule { }

public static class CoreModuleExtensions
{
    public static IServiceCollection AddCoreModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connString = configuration["Data:mainOrg"]
            ?? throw new InvalidOperationException("Connection string 'Data:mainOrg' is missing.");

        services.AddDbContext<CoreDbContext>(options =>
            options.UseSqlServer(connString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", CoreDbContext.Schema);
                sql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(500), null);
            }));

        services.AddScoped<INumberSeries, NumberSeriesAllocator>();
        services.AddScoped<INumberSeriesAdminService, NumberSeriesAdminService>();

        // Master lists (Vehicle Type, Make, City, …): the platform's own, and any a module adds with AddLookupType.
        foreach (var definition in PlatformLookups.All) services.AddLookupType(definition);
        services.AddSingleton<ILookupCatalog, LookupCatalog>();
        services.AddScoped<LookupService>();
        services.AddScoped<ILookupReader>(sp => sp.GetRequiredService<LookupService>());
        services.AddScoped<ILookupAdminService>(sp => sp.GetRequiredService<LookupService>());

        // The tenant's own locations (FSD §6 field 15): one to start with, "Head Office" (OQ-10).
        services.AddScoped<BranchService>();
        services.AddScoped<IBranchService>(sp => sp.GetRequiredService<BranchService>());
        services.AddScoped<IBranchDirectory>(sp => sp.GetRequiredService<BranchService>());

        // File storage: encrypted files on disk, scanned on the way in, served only through short-lived links.
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.Section));
        services.AddDataProtection().SetApplicationName("VMS");
        services.AddScoped<IFileStore, LocalFileStore>();
        services.AddSingleton<FileDownloadLinks>();
        services.AddSingleton<IFileDownloadLinks>(sp => sp.GetRequiredService<FileDownloadLinks>());
        services.TryAddSingleton<IVirusScanner, BuiltInVirusScanner>();   // replace with a real engine before go-live
        services.TryAddScoped<IFileScanAlert, LoggingFileScanAlert>();     // the notification module replaces this

        return services;
    }

    /// <summary>Applies pending migrations. Runs before the other modules, whose saves write audit rows into the core schema.</summary>
    public static IApplicationBuilder UseCoreModule(this IApplicationBuilder app)
    {
        // Refuse to start with file storage that would lose or expose files, rather than fail on the first upload.
        var environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
        app.ApplicationServices.GetRequiredService<IOptions<FileStorageOptions>>().Value.Validate(environment.IsDevelopment());
        _ = app.ApplicationServices.GetRequiredService<ILookupCatalog>();   // builds it, which checks every lookup definition

        using var scope = app.ApplicationServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<CoreDbContext>().Database.Migrate();
        return app;
    }
}
