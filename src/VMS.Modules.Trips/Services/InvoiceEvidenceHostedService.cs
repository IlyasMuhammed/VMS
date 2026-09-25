using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Tenancy;

namespace VMS.Modules.Trips.Services;

/// <summary>§41: "rendered by a background job (keeps generation fast for large invoices)" — the same
/// tenant-scoped-runner-plus-loop pattern <see cref="FuelCardHostedService"/> already established. Naturally
/// idempotent: a row that already left Queued (Generated or Failed) is simply excluded by
/// <see cref="IInvoiceEvidenceGenerationService.ProcessQueuedAsync"/>'s own filter next run.</summary>
internal sealed class InvoiceEvidenceHostedService(IServiceScopeFactory scopeFactory, ILogger<InvoiceEvidenceHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Invoice evidence generation job failed."); }

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
                using var acting = scope.ServiceProvider.GetRequiredService<IAuditContext>().ActAsSystem("Invoice evidence generation job");
                var processed = await scope.ServiceProvider.GetRequiredService<IInvoiceEvidenceGenerationService>().ProcessQueuedAsync(ct);
                if (processed > 0) logger.LogInformation("Tenant {TenantId}: {Processed} invoice evidence document(s) generated.", tenantId, processed);
            });
        }
    }
}
