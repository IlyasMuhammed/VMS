using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VMS.Modules.Documents.Data;
using VMS.Modules.Documents.Services;
using VMS.Shared.Documents;
using VMS.Shared.Notifications;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Documents;

public interface IDocumentsModule { }

public static class DocumentsModuleExtensions
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connString = configuration["Data:mainOrg"]
            ?? throw new InvalidOperationException("Connection string 'Data:mainOrg' is missing.");

        services.AddDbContext<DocumentDbContext>(options =>
            options.UseSqlServer(connString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", DocumentDbContext.Schema);
                sql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(500), null);
            }));

        services.AddScoped<DocumentContext>();
        services.AddScoped<IDocumentTypeService, DocumentTypeService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IDocumentRegisterService, DocumentRegisterService>();
        services.AddScoped<IDocumentExpiryRecalculator, DocumentExpiryRecalculator>();
        services.AddScoped<IDocumentRetentionCleaner, DocumentRetentionCleaner>();
        services.AddScoped<IDocumentJobsRunner, DocumentJobsRunner>();
        services.AddHostedService<DocumentsHostedService>();
        services.AddScoped<IDocumentNotificationSource, DocumentNotificationSource>();
        services.TryAddScoped<INotificationTrigger, NoNotificationTrigger>();   // the Notifications module's own registration, added after this one, wins

        // The real answer to what other modules ask about a record's documents, taking priority over the vehicle
        // module's own no-op stand-in (NoDocumentCheck) — registered here, so AddDocumentsModule must run before
        // AddVehiclesModule in Program.cs for its TryAddScoped call to see this one already taken.
        services.AddScoped<DocumentCheckService>();
        services.AddScoped<IDocumentCheck>(sp => sp.GetRequiredService<DocumentCheckService>());
        services.AddScoped<IVehicleDocumentCheck>(sp => sp.GetRequiredService<DocumentCheckService>());
        return services;
    }

    /// <summary>Applies pending migrations. Runs after Core, Tenancy, BusinessPartners and before Vehicles (whose stand-in document check this module must pre-empt).</summary>
    public static IApplicationBuilder UseDocumentsModule(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<DocumentDbContext>().Database.Migrate();
        return app;
    }
}
