using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Tenancy;

namespace VMS.Modules.Trips.Services;

/// <summary>§42.9: "Large exports (&gt; 50,000 rows) run as background jobs" — the same tenant-scoped-runner-plus-
/// loop pattern <see cref="InvoiceEvidenceHostedService"/>/<see cref="LedgerReconciliationHostedService"/> already
/// established. Tests call <see cref="ILedgerReportService.ProcessQueuedExportsAsync"/> directly via DI, the same
/// shortcut CC-28 established.</summary>
internal sealed class ReportExportHostedService(IServiceScopeFactory scopeFactory, ILogger<ReportExportHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Report export job failed."); }

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
                using var acting = scope.ServiceProvider.GetRequiredService<IAuditContext>().ActAsSystem("Report export job");
                var processed = await scope.ServiceProvider.GetRequiredService<ILedgerReportService>().ProcessQueuedExportsAsync(ct);
                if (processed > 0) logger.LogInformation("Tenant {TenantId}: {Processed} report export(s) rendered.", tenantId, processed);
            });
        }
    }
}
