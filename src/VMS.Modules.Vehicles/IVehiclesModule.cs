using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VMS.Modules.Vehicles.Data;
using VMS.Modules.Vehicles.Services;
using VMS.Shared.Notifications;
using VMS.Shared.Partners;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Vehicles;

public interface IVehiclesModule { }

/// <summary>Until the finance module supplies the real answer, no vehicle has finance outstanding.</summary>
internal sealed class NoFinanceGuard : IVehicleFinanceGuard
{
    public Task<bool> HasOutstandingFinanceAsync(int vehicleId, CancellationToken cancellationToken = default) => Task.FromResult(false);
}

public static class VehiclesModuleExtensions
{
    public static IServiceCollection AddVehiclesModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connString = configuration["Data:mainOrg"]
            ?? throw new InvalidOperationException("Connection string 'Data:mainOrg' is missing.");

        services.AddDbContext<VehicleDbContext>(options =>
            options.UseSqlServer(connString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", VehicleDbContext.Schema);
                sql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(500), null);
            }));

        services.AddScoped<VehicleContext>();
        services.AddScoped<IVehicleService, VehicleService>();
        services.AddScoped<IVehicleQueryService, VehicleQueryService>();
        services.AddScoped<ILifecycleService, LifecycleService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ItemService>();
        services.AddScoped<IItemService>(sp => sp.GetRequiredService<ItemService>());   // the activation service uses the same item rules
        services.AddScoped<IOdometerService, OdometerService>();
        services.AddScoped<IAssignmentService, AssignmentService>();
        services.AddScoped<IAcquisitionService, AcquisitionService>();
        services.AddScoped<IFinanceService, FinanceService>();
        services.AddScoped<IInstallmentService, InstallmentService>();
        services.AddScoped<IActivationService, ActivationService>();
        services.TryAddScoped<IVehicleDocumentCheck, NoDocumentCheck>();
        services.AddScoped<ILinkedVehiclesService, LinkedVehiclesService>();
        services.AddScoped<IFinancialSummaryService, FinancialSummaryService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IVehicleFinanceGuard, VehicleFinanceGuard>();   // the schedule is this module's: BR-VH-007 is answered here
        services.AddScoped<IVehicleDirectory, VehicleDirectory>();
        services.AddScoped<IRecurringChargeService, RecurringChargeService>();
        services.AddScoped<IRecurringChargeGenerator, RecurringChargeGenerator>();
        services.AddScoped<IPayablesService, PayablesService>();
        services.AddHostedService<RecurringChargeHostedService>();
        services.AddScoped<IVehicleNotificationSource, VehicleNotificationSource>();
        services.TryAddScoped<INotificationTrigger, NoNotificationTrigger>();   // the Notifications module's own registration, added after this one, wins
        // The partner module asks every module that points at a partner before it lets a role go or the partner be deactivated.
        services.AddScoped<IPartnerUsageCheck, VehiclePartnerUsageCheck>();
        return services;
    }

    /// <summary>Applies pending migrations. Runs after the Core module (audit rows) and the Tenancy module.</summary>
    public static IApplicationBuilder UseVehiclesModule(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<VehicleDbContext>().Database.Migrate();
        return app;
    }
}
