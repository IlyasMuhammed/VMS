using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VMS.Shared.Common;
using VMS.Shared.Tenancy;

namespace VMS.Modules.Notifications.Services;

public interface INotificationJobsRunner
{
    /// <summary>Evaluates every notification candidate for the signed-in tenant and writes what is newly due (S7-NOT-03).</summary>
    Task<NotificationEvaluationResult> RunAsync(CancellationToken cancellationToken = default);
}

internal sealed class NotificationJobsRunner(INotificationEvaluator evaluator) : INotificationJobsRunner
{
    public Task<NotificationEvaluationResult> RunAsync(CancellationToken ct = default) => evaluator.RunAsync(ct);
}

/// <summary>
/// The nightly notification evaluation (S7-NOT-03), built on the same no-scheduler-in-repo pattern S4-REC-05 and
/// S5-DOC-06/09 introduced: a tenant-scoped, directly testable runner, and an hourly <see cref="BackgroundService"/>
/// looping every active tenant through it via <see cref="BackgroundTenantScope"/>. Naturally idempotent (a candidate
/// already notified for is simply skipped by the dedup check), so polling hourly instead of once a night is harmless —
/// and the same evaluator this loop calls is also called synchronously by save-time hooks (S7-NOT-02), so a lead time
/// this loop would have found in an hour is instead already there the moment the record was saved.
/// S7-NOT-04 (operating time zone) needs no code of its own: the evaluator already asks <c>IOperatingClock.TodayAsync</c>
/// for "today", the same NFR-DT-06 pattern the Documents and Vehicles jobs use, so a lead time means the same day for
/// every user regardless of where they log in.
/// </summary>
internal sealed class NotificationsHostedService(IServiceScopeFactory scopeFactory, ILogger<NotificationsHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(40);   // a little after the documents job's own delay, so they do not contend for the same tenants' connections at once

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Notification job failed."); }

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
                var result = await scope.ServiceProvider.GetRequiredService<INotificationJobsRunner>().RunAsync(ct);
                if (result.Created > 0)
                    logger.LogInformation("Tenant {TenantId}: {Created} notifications created.", tenantId, result.Created);
            });
        }
    }
}
