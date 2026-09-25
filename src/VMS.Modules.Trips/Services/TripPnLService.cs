using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;

namespace VMS.Modules.Trips.Services;

/// <summary>Trip operational P&amp;L (§31, AC-22).</summary>
public interface ITripPnLService
{
    Task<TripPnLModel> GetAsync(long tripId, CancellationToken ct = default);
    Task<TripPnLSummaryModel> SummaryAsync(TripPnLQuery query, CancellationToken ct = default);
}

internal sealed class TripPnLService(TripsDbContext db, ITenantContext tenant) : ITripPnLService
{
    public async Task<TripPnLModel> GetAsync(long tripId, CancellationToken ct = default)
    {
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct)
            ?? throw new NotFoundException($"Trip {tripId} was not found.");
        return await ComputeAsync(trip, ct);
    }

    public async Task<TripPnLSummaryModel> SummaryAsync(TripPnLQuery query, CancellationToken ct = default)
    {
        var trips = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId)
            .Where(t => query.CustomerId == null || t.CustomerId == query.CustomerId)
            .Where(t => query.VehicleId == null || t.VehicleId == query.VehicleId)
            .Where(t => query.FromDate == null || t.TripDate >= query.FromDate)
            .Where(t => query.ToDate == null || t.TripDate <= query.ToDate)
            .ToListAsync(ct);

        var lines = new List<TripPnLModel>();
        foreach (var trip in trips) lines.Add(await ComputeAsync(trip, ct));

        var priced = lines.Where(l => l.IsPriced).ToList();
        return new TripPnLSummaryModel
        {
            Trips = lines,
            TotalRevenue = priced.Sum(l => l.Revenue ?? 0),
            TotalIncome = priced.Sum(l => l.ApprovedIncome),
            TotalFuel = priced.Sum(l => l.Fuel),
            TotalExpenses = priced.Sum(l => l.ApprovedExpenses),
            TotalPnL = priced.Sum(l => l.OperationalPnL ?? 0),
            PricedTripCount = priced.Count,
            // §31: "Trips with RateMissing show revenue as 'Not priced' and are excluded from totals with a count shown."
            UnpricedTripCount = lines.Count - priced.Count
        };
    }

    private async Task<TripPnLModel> ComputeAsync(Trip trip, CancellationToken ct)
    {
        var fuel = await db.TripFuels.AsNoTracking().Where(f => f.TenantId == tenant.TenantId && f.TripId == trip.TripId && !f.IsVoided).SumAsync(f => (decimal?)f.Amount, ct) ?? 0;
        var expenses = await db.TripExpenses.AsNoTracking()
            .Where(e => e.TenantId == tenant.TenantId && e.TripId == trip.TripId && !e.IsVoided && e.ApprovalStatus == TripExpenseApprovalStatuses.Approved)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0;
        // Income has no approval workflow of its own (§30) — every non-voided row is the closest honest reading
        // of the formula's "Approved Trip Income."
        var income = await db.TripIncomes.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.TripId == trip.TripId && !i.IsVoided).SumAsync(i => (decimal?)i.Amount, ct) ?? 0;

        var isPriced = !trip.RateMissing && trip.TripAmount is not null;
        return new TripPnLModel
        {
            TripId = trip.TripId, TripNumber = trip.TripNumber, IsPriced = isPriced, Revenue = isPriced ? trip.TripAmount : null,
            ApprovedIncome = income, Fuel = fuel, ApprovedExpenses = expenses,
            OperationalPnL = isPriced ? trip.TripAmount!.Value + income - fuel - expenses : null,
            CurrencyCode = trip.CurrencyCode ?? string.Empty
        };
    }
}
