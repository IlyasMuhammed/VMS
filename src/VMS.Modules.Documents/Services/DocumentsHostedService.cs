using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Tenancy;

namespace VMS.Modules.Documents.Services;

/// <summary>What one run produced, for the admin trigger's answer and the log line.</summary>
public sealed record DocumentJobsResult(ExpiryRecalculationResult Expiry, RetentionResult Retention);

public interface IDocumentJobsRunner
{
    /// <summary>Runs the expiry recalculation (BR-DOC-003) and the retention cleanup (BR-DOC-002) together, for the signed-in tenant.</summary>
    Task<DocumentJobsResult> RunAsync(CancellationToken cancellationToken = default);
}

internal sealed class DocumentJobsRunner(IDocumentExpiryRecalculator expiry, IDocumentRetentionCleaner retention) : IDocumentJobsRunner
{
    public async Task<DocumentJobsResult> RunAsync(CancellationToken ct = default) =>
        new(await expiry.RunAsync(ct), await retention.RunAsync(ct));
}

/// <summary>
/// The nightly documents job (S5-DOC-06, S5-DOC-09), built on the same pattern S4-REC-05 introduced: a tenant-scoped,
/// directly testable runner, and an hourly <see cref="BackgroundService"/> looping every active tenant through it via
/// <see cref="BackgroundTenantScope"/>. Both halves are naturally idempotent (recalculating a status that has not changed
/// is a no-op; deleting an already-deleted row finds nothing), so polling hourly instead of once a night is harmless.
/// </summary>
internal sealed class DocumentsHostedService(IServiceScopeFactory scopeFactory, ILogger<DocumentsHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(35);   // a little after the recurring-charge job's own delay, so they do not contend for the same tenants' connections at once

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Document jobs failed."); }

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
                using var acting = scope.ServiceProvider.GetRequiredService<IAuditContext>().ActAsSystem("Document jobs");
                var result = await scope.ServiceProvider.GetRequiredService<IDocumentJobsRunner>().RunAsync(ct);
                if (result.Expiry.Recalculated > 0 || result.Retention.Removed > 0)
                    logger.LogInformation("Tenant {TenantId}: {Recalculated} document statuses recalculated ({ExpiringSoon} now expiring soon, {Expired} now expired), {Removed} superseded or rejected versions retired.",
                        tenantId, result.Expiry.Recalculated, result.Expiry.BecameExpiringSoon, result.Expiry.BecameExpired, result.Retention.Removed);
            });
        }
    }
}
