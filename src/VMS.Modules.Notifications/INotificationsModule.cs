using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VMS.Modules.Notifications.Data;
using VMS.Modules.Notifications.Services;
using VMS.Shared.Notifications;

namespace VMS.Modules.Notifications;

public interface INotificationsModule { }

public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connString = configuration["Data:mainOrg"]
            ?? throw new InvalidOperationException("Connection string 'Data:mainOrg' is missing.");

        services.AddDbContext<NotificationDbContext>(options =>
            options.UseSqlServer(connString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationDbContext.Schema);
                sql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(500), null);
            }));

        services.AddScoped<INotificationRuleService, NotificationRuleService>();
        services.AddScoped<INotificationEvaluator, NotificationEvaluator>();
        services.AddScoped<INotificationJobsRunner, NotificationJobsRunner>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationTrigger, NotificationTrigger>();   // registered after Documents/Vehicles's fallback TryAddScoped calls, so this real one wins on resolve
        services.AddHostedService<NotificationsHostedService>();

        return services;
    }

    /// <summary>Applies pending migrations. Runs after the Documents and Vehicles modules, whose <c>IDocumentNotificationSource</c> and <c>IVehicleNotificationSource</c> this module's evaluator consumes by DI (no direct project reference either way — the cross-module pattern this whole platform uses).</summary>
    public static IApplicationBuilder UseNotificationsModule(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.Migrate();
        return app;
    }
}
