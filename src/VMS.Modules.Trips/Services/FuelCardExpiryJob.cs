using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Tenancy;
using VMS.Shared.Time;

namespace VMS.Modules.Trips.Services;

public sealed record FuelCardExpiryResult(int Expired);

public interface IFuelCardExpiryJob
{
    /// <summary>§28: "Status = Expired set by nightly job when ExpiryDate &lt; today." Only Active cards are
    /// touched — an already-Inactive or Blocked card keeps its own status even past its expiry date, since that
    /// status was a deliberate back-office decision this job should not silently overwrite.</summary>
    Task<FuelCardExpiryResult> RunAsync(CancellationToken cancellationToken = default);
}

internal sealed class FuelCardExpiryJob(TripsDbContext db, ITenantContext tenant, IOperatingClock clock) : IFuelCardExpiryJob
{
    public async Task<FuelCardExpiryResult> RunAsync(CancellationToken ct = default)
    {
        var today = await clock.TodayAsync(tenant.TenantId);
        var due = await db.FuelCards.Where(c => c.TenantId == tenant.TenantId && c.Status == FuelCardStatuses.Active && c.ExpiryDate < today).ToListAsync(ct);
        foreach (var card in due) card.Status = FuelCardStatuses.Expired;
        if (due.Count > 0) await db.SaveChangesAsync(ct);
        return new FuelCardExpiryResult(due.Count);
    }
}

/// <summary>The nightly fuel card job, on the same tenant-scoped-runner-plus-hourly-loop pattern S4-REC-05
/// introduced and S5/S7 both reused (<see cref="BackgroundTenantScope"/>/<see cref="ITenantDirectory"/>).
/// Naturally idempotent (an already-Expired card is simply excluded by the Active filter next run).</summary>
internal sealed class FuelCardHostedService(IServiceScopeFactory scopeFactory, ILogger<FuelCardHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(40);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Fuel card expiry job failed."); }

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
                using var acting = scope.ServiceProvider.GetRequiredService<IAuditContext>().ActAsSystem("Fuel card expiry job");
                var result = await scope.ServiceProvider.GetRequiredService<IFuelCardExpiryJob>().RunAsync(ct);
                if (result.Expired > 0) logger.LogInformation("Tenant {TenantId}: {Expired} fuel card(s) marked Expired.", tenantId, result.Expired);
            });
        }
    }
}
