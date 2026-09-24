using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Tenancy;

namespace VMS.Modules.Vehicles.Services;

/// <summary>
/// The "nightly" of the nightly generation job (BR-VH-030). It is idempotent, so polling more often than once a day is harmless —
/// a charge only produces something new once its lead-day window opens, which in practice is once a day — and it means a missed
/// restart is never more than an hour late rather than a lost day. Runs every active tenant, one at a time, each impersonated
/// through <see cref="BackgroundTenantScope"/> so the same scoped services and tenant query filters a request would use apply here too.
/// </summary>
internal sealed class RecurringChargeHostedService(IServiceScopeFactory scopeFactory, ILogger<RecurringChargeHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);   // let migrations and seeding finish first

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Recurring charge generation failed."); }

            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> tenants;
        using (var scope = scopeFactory.CreateScope())
            tenants = (await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().ActiveTenantIdsAsync(ct)).ToList();

        foreach (var tenantId in tenants)
        {
            await BackgroundTenantScope.RunAsAsync(tenantId, async () =>
            {
                using var scope = scopeFactory.CreateScope();
                using var acting = scope.ServiceProvider.GetRequiredService<IAuditContext>().ActAsSystem("Recurring charge generator");
                var result = await scope.ServiceProvider.GetRequiredService<IRecurringChargeGenerator>().RunAsync(ct);
                if (result.EntriesGenerated > 0 || result.EntriesMarkedOverdue > 0)
                    logger.LogInformation("Tenant {TenantId}: {Generated} entries generated ({AutoPosted} auto-posted), {Overdue} moved to overdue.",
                        tenantId, result.EntriesGenerated, result.EntriesAutoPosted, result.EntriesMarkedOverdue);
            });
        }
    }
}
