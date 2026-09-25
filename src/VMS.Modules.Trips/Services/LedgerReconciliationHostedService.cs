using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Tenancy;

namespace VMS.Modules.Trips.Services;

/// <summary>§40A.5 LR-4: "a nightly reconciliation job" — the same tenant-scoped-runner-plus-loop pattern
/// <see cref="InvoiceEvidenceHostedService"/> already established (itself following
/// <c>FuelCardHostedService</c>'s own precedent). Tests call <see cref="ILedgerReconciliationJob.RunAsync"/>
/// directly via DI rather than waiting on this job's own interval, the same established shortcut CC-28's own
/// evidence tests already use.</summary>
internal sealed class LedgerReconciliationHostedService(IServiceScopeFactory scopeFactory, ILogger<LedgerReconciliationHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Ledger reconciliation job failed."); }

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
                using var acting = scope.ServiceProvider.GetRequiredService<IAuditContext>().ActAsSystem("Ledger reconciliation job");
                var run = await scope.ServiceProvider.GetRequiredService<ILedgerReconciliationJob>().RunAsync(ct);
                if (run.MismatchCount > 0) logger.LogWarning("Tenant {TenantId}: ledger reconciliation found {Count} mismatch(es).", tenantId, run.MismatchCount);
            });
        }
    }
}
