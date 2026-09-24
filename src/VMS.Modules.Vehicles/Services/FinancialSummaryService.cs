using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Vehicles.Services;

public interface IFinancialSummaryService
{
    Task<FinancialSummary> GetAsync(int vehicleId);
}

/// <summary>
/// The derived totals of a vehicle (FR-VH-006, BR-VH-003): sums over its ledger and its installment schedule. A correcting entry
/// (BR-VH-011) counts under the type of the entry it reverses, so a reversed payment is no longer paid.
/// </summary>
internal sealed class FinancialSummaryService(VehicleContext ctx) : IFinancialSummaryService
{
    public async Task<FinancialSummary> GetAsync(int vehicleId)
    {
        await ctx.LoadReadOnlyAsync(vehicleId);

        // One aggregate query: each entry under its own type, or under the type of the entry it corrects.
        var byType = await (
            from t in ctx.Db.Transactions.AsNoTracking()
            where t.TenantId == ctx.Tenant && t.VehicleId == vehicleId
            join o in ctx.Db.Transactions.AsNoTracking() on t.ReversesTransactionId equals (int?)o.VehicleTransactionId into originals
            from o in originals.DefaultIfEmpty()
            select new { Type = t.Type == TransactionTypes.Adjustment && o != null ? o.Type : t.Type, t.Amount })
            .GroupBy(x => x.Type).Select(g => new { Type = g.Key, Total = g.Sum(x => x.Amount) }).ToListAsync();
        decimal Of(string type) => byType.FirstOrDefault(x => x.Type == type)?.Total ?? 0m;

        var summary = new FinancialSummary
        {
            VehicleId = vehicleId, AcquisitionCost = Of(TransactionTypes.Acquisition), MajorExpenses = Of(TransactionTypes.MajorExpense),
            PaidToDate = Of(TransactionTypes.InitialPayment) + Of(TransactionTypes.Installment), Deposits = Of(TransactionTypes.Deposit)
        };
        summary.TotalCost = summary.AcquisitionCost + summary.MajorExpenses;
        summary.Agreement = await AgreementAsync(vehicleId);
        return summary;
    }

    private async Task<AgreementSummary?> AgreementAsync(int vehicleId)
    {
        // The one in force, or else the latest that was: a settled agreement stays visible with nothing outstanding. A Draft has no schedule yet.
        var agreement = await ctx.Db.Agreements.AsNoTracking().Where(a => a.TenantId == ctx.Tenant && a.VehicleId == vehicleId && a.Status != AgreementStatuses.Draft)
            .OrderBy(a => a.Status == AgreementStatuses.Active ? 0 : 1).ThenByDescending(a => a.VehicleFinanceAgreementId).FirstOrDefaultAsync();
        if (agreement is null) return null;

        var rows = await ctx.Db.Installments.AsNoTracking().Where(i => i.TenantId == ctx.Tenant && i.VehicleFinanceAgreementId == agreement.VehicleFinanceAgreementId)
            .OrderBy(i => i.DueDate).ThenBy(i => i.InstallmentNo).ToListAsync();
        var today = await ctx.TodayAsync();
        var open = rows.Where(i => i.PaidAmount < i.ExpectedAmount).ToList();
        var late = open.Where(i => i.DueDate < today).ToList();
        var refs = await ctx.RefsAsync([agreement.BankId]);
        return new AgreementSummary
        {
            AgreementId = agreement.VehicleFinanceAgreementId, Status = agreement.Status, Bank = VehicleContext.Ref(refs, agreement.BankId),
            TotalPayable = rows.Where(i => !i.IsResidual).Sum(i => i.ExpectedAmount), Residual = rows.Where(i => i.IsResidual).Sum(i => i.ExpectedAmount),
            Outstanding = rows.Sum(i => i.ExpectedAmount - i.PaidAmount), InstallmentsPaid = rows.Count(i => i.PaidAmount >= i.ExpectedAmount), InstallmentsTotal = rows.Count,
            NextDueDate = open.FirstOrDefault()?.DueDate, NextDueAmount = open.FirstOrDefault() is { } next ? next.ExpectedAmount - next.PaidAmount : null,
            OverdueCount = late.Count, OverdueAmount = late.Sum(i => i.ExpectedAmount - i.PaidAmount)
        };
    }
}

/// <summary>
/// Whether a vehicle's finance agreement still has a balance, for the category rule (BR-VH-007): any Category but Bank Leased is refused while
/// installments remain unpaid. This is the real answer the category service asks for; the vehicle module holds the schedule itself.
/// </summary>
internal sealed class VehicleFinanceGuard(VehicleContext ctx) : IVehicleFinanceGuard
{
    public Task<bool> HasOutstandingFinanceAsync(int vehicleId, CancellationToken cancellationToken = default) =>
        ctx.Db.Installments.AsNoTracking().AnyAsync(i => i.TenantId == ctx.Tenant && i.VehicleId == vehicleId && i.PaidAmount < i.ExpectedAmount
            && ctx.Db.Agreements.Any(a => a.VehicleFinanceAgreementId == i.VehicleFinanceAgreementId && a.Status == AgreementStatuses.Active), cancellationToken);
}
