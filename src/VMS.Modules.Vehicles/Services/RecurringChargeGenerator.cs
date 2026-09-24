using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Shared.Lookups;

namespace VMS.Modules.Vehicles.Services;

/// <summary>What one run produced, for the admin trigger's answer and the hosted service's log line.</summary>
public sealed record RecurringChargeGenerationResult(int EntriesGenerated, int EntriesAutoPosted, int EntriesMarkedOverdue);

public interface IRecurringChargeGenerator
{
    /// <summary>
    /// Generates Due entries for every charge whose lead-day window has opened, auto-posts the Fixed ones set to it, and moves
    /// entries whose due date has passed to Overdue — all for the signed-in tenant, as of today (FSD §19A.4). Idempotent on
    /// (charge, period), BR-VH-030: safe to call more than once, from more than one place, at the same moment.
    /// </summary>
    Task<RecurringChargeGenerationResult> RunAsync(CancellationToken cancellationToken = default);
}

internal sealed class RecurringChargeGenerator(VehicleContext ctx, ILookupReader lookups) : IRecurringChargeGenerator
{
    public async Task<RecurringChargeGenerationResult> RunAsync(CancellationToken ct = default)
    {
        var today = await ctx.TodayAsync();
        var generated = 0;
        var autoPosted = 0;

        // Only a vehicle in the fleet generates entries: a Draft has not been activated, and a disposed vehicle's charges are
        // already end-dated (BR-VH-034), so its NextDueDate is already null.
        var due = await (from c in ctx.Db.RecurringCharges
                          join v in ctx.Db.Vehicles on c.VehicleId equals v.VehicleId
                          where c.TenantId == ctx.Tenant && c.EffectiveTo == null && c.NextDueDate != null && c.PostingMode != ChargePostingModes.ReminderOnly
                                && VehicleStatuses.InFleet.Contains(v.Status)
                          select c).ToListAsync(ct);

        foreach (var charge in due)
        {
            // A while, not an if: a job that missed a run catches up one occurrence at a time, each idempotently, rather than skipping what it missed.
            while (charge.NextDueDate is { } occurrence && occurrence.AddDays(-charge.GenerateLeadDays) <= today)
            {
                var posted = await GenerateOneAsync(charge, occurrence, ct);
                if (posted.Entry is not null) { generated++; if (posted.AutoPosted) autoPosted++; }
                Advance(charge, occurrence);
            }
        }
        if (generated > 0) await ctx.Db.SaveChangesAsync(ct);

        var overdue = await ctx.Db.RecurringChargeEntries
            .Where(e => e.TenantId == ctx.Tenant && e.Status == ChargeEntryStatuses.Due && e.DueDate < today).ToListAsync(ct);
        foreach (var entry in overdue) entry.Status = ChargeEntryStatuses.Overdue;
        if (overdue.Count > 0) await ctx.Db.SaveChangesAsync(ct);

        return new RecurringChargeGenerationResult(generated, autoPosted, overdue.Count);
    }

    private async Task<(VehicleRecurringChargeEntry? Entry, bool AutoPosted)> GenerateOneAsync(VehicleRecurringCharge charge, DateOnly occurrence, CancellationToken ct)
    {
        var periodKey = ChargeSchedule.PeriodKeyOf(occurrence, charge.Frequency);
        if (await ctx.Db.RecurringChargeEntries.AnyAsync(e => e.VehicleRecurringChargeId == charge.VehicleRecurringChargeId && e.PeriodKey == periodKey, ct))
            return (null, false);   // already generated — a previous run, or one racing this one

        var amount = charge.AmountBasis == ChargeAmountBases.Fixed ? charge.Amount ?? 0m : await LastPaidAmountAsync(charge, ct) ?? charge.Amount ?? 0m;
        var entry = new VehicleRecurringChargeEntry
        {
            VehicleId = charge.VehicleId, VehicleRecurringChargeId = charge.VehicleRecurringChargeId, PeriodKey = periodKey, DueDate = occurrence,
            ExpectedAmount = amount, Status = ChargeEntryStatuses.Due, CreatedOn = DateTime.UtcNow
        };
        ctx.Db.RecurringChargeEntries.Add(entry);
        try { await ctx.Db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            ctx.Db.Entry(entry).State = EntityState.Detached;   // another run generated this same period a moment ago — nothing more to do
            return (null, false);
        }

        var autoPost = charge.PostingMode == ChargePostingModes.AutoPost && charge.AmountBasis == ChargeAmountBases.Fixed;   // BR-VH-027, re-checked here in case the charge changed since it was set
        if (autoPost)
        {
            var typeCode = (await lookups.FindAsync(PlatformLookups.RecurringChargeType, charge.ChargeTypeId))?.Code ?? "OTHER";
            await RecurringChargePosting.PostAsync(ctx, entry, charge, typeCode, amount, occurrence, mode: null, reference: null, remarks: "Auto-posted", confirmedByUserId: null, isAutoPosted: true, ct);
        }
        return (entry, autoPost);
    }

    /// <summary>The last confirmed amount of this charge's series, pre-filling a Variable or Percentage-of-income entry (§19A.1).</summary>
    private async Task<decimal?> LastPaidAmountAsync(VehicleRecurringCharge charge, CancellationToken ct) =>
        await (from e in ctx.Db.RecurringChargeEntries
               join c in ctx.Db.RecurringCharges on e.VehicleRecurringChargeId equals c.VehicleRecurringChargeId
               where c.TenantId == ctx.Tenant && c.SeriesId == charge.SeriesId && e.Status == ChargeEntryStatuses.Paid
               orderby e.PaidOn descending
               select e.PaidAmount).FirstOrDefaultAsync(ct);

    private static void Advance(VehicleRecurringCharge charge, DateOnly occurrence)
    {
        charge.GeneratedCount++;
        var reachedEnd = (charge.EndDate is { } end && occurrence >= end) || (charge.OccurrenceCount is { } max && charge.GeneratedCount >= max);
        charge.NextDueDate = reachedEnd ? null : ChargeSchedule.Next(occurrence, charge.Frequency, charge.DueDay, charge.CustomIntervalDays);
    }
}
