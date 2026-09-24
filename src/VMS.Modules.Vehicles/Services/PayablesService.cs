using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;

namespace VMS.Modules.Vehicles.Services;

public interface IPayablesService
{
    /// <summary>One vehicle's Due and Overdue items — its recurring charge entries and its unpaid installments together (BR-VH-029), oldest due first.</summary>
    Task<List<PayableModel>> ForVehicleAsync(int vehicleId);

    /// <summary>Fleet-wide Due and Overdue items for the Payables Due workbench (FR-VH-016).</summary>
    Task<List<PayableModel>> ListAsync(PayablesQuery query);

    /// <summary>Confirms a batch from the workbench with one payment date and mode; each item still runs its own kind's checks, so one bad row does not stop the rest (§19A.5).</summary>
    Task<BulkConfirmResult> BulkConfirmAsync(BulkConfirmRequest request, VehicleCaller caller);

    /// <summary>The dashboard tile: due within 7 days and overdue, fleet-wide (§19A.5).</summary>
    Task<PayablesSummaryModel> SummaryAsync();
}

/// <summary>
/// "One schedule, two views" (BR-VH-029): a Bank Installment is never a <see cref="VehicleRecurringCharge"/>, so this is where its
/// due rows and a configured charge's due rows are brought together for the screens that need both (§19A.5).
/// </summary>
internal sealed class PayablesService(VehicleContext ctx, ILookupReader lookups, IRecurringChargeService charges, IInstallmentService installments) : IPayablesService
{
    private const string InstallmentKind = "Installment";
    private const string RecurringChargeKind = "RecurringCharge";

    public async Task<List<PayableModel>> ForVehicleAsync(int vehicleId)
    {
        await ctx.LoadReadOnlyAsync(vehicleId);
        var all = await CombinedAsync(v => v.VehicleId == vehicleId);
        return all.OrderBy(p => p.DueDate).ToList();
    }

    public async Task<List<PayableModel>> ListAsync(PayablesQuery query)
    {
        var all = await CombinedAsync(v => true);
        IEnumerable<PayableModel> filtered = all;
        if (query.ChargeTypeId is { } type) filtered = filtered.Where(p => p.ChargeTypeId == type);
        if (query.PayeeId is { } payee) filtered = filtered.Where(p => p.Payee?.Id == payee);
        if (query.BranchId is { } branch) filtered = filtered.Where(p => p.BranchId == branch);
        if (query.DueFrom is { } from) filtered = filtered.Where(p => p.DueDate >= from);
        if (query.DueTo is { } to) filtered = filtered.Where(p => p.DueDate <= to);
        if (query.OverdueOnly) filtered = filtered.Where(p => p.Status == ChargeEntryStatuses.Overdue);
        return filtered.OrderBy(p => p.DueDate).ToList();
    }

    /// <summary>How far ahead an installment surfaces as payable, matching the platform default lead time a recurring charge starts with (§19A.1).</summary>
    private const int InstallmentLeadDays = 7;

    /// <summary>Every Due or Overdue item, of both kinds, across the vehicles <paramref name="scope"/> lets through.</summary>
    private async Task<List<PayableModel>> CombinedAsync(Func<Vehicle, bool> scope)
    {
        var today = await ctx.TodayAsync();
        var entries = await (from e in ctx.Db.RecurringChargeEntries
                              join c in ctx.Db.RecurringCharges on e.VehicleRecurringChargeId equals c.VehicleRecurringChargeId
                              join v in ctx.Db.Vehicles on e.VehicleId equals v.VehicleId
                              where e.TenantId == ctx.Tenant && (e.Status == ChargeEntryStatuses.Due || e.Status == ChargeEntryStatuses.Overdue)
                              select new { e, c, v }).AsNoTracking().ToListAsync();
        var live = entries.Where(x => scope(x.v)).ToList();

        // BR-VH-029: an installment is not "generated" the way a charge entry is, so it has no lead days of its own — it surfaces
        // on the same default window a charge starts with, once it is due or about to be, and stops once it is paid.
        var leadWindow = today.AddDays(InstallmentLeadDays);
        var dueInstallments = await (from i in ctx.Db.Installments
                                      join a in ctx.Db.Agreements on i.VehicleFinanceAgreementId equals a.VehicleFinanceAgreementId
                                      join v in ctx.Db.Vehicles on i.VehicleId equals v.VehicleId
                                      where i.TenantId == ctx.Tenant && a.Status == AgreementStatuses.Active && i.Status != InstallmentStatuses.Paid && i.DueDate <= leadWindow
                                      select new { i, a, v }).AsNoTracking().ToListAsync();
        var liveInstallments = dueInstallments.Where(x => scope(x.v)).ToList();

        var types = await lookups.FindManyAsync(PlatformLookups.RecurringChargeType, live.Select(x => x.c.ChargeTypeId));
        var bankType = await lookups.GetActiveAsync(PlatformLookups.RecurringChargeType);
        var bankTypeId = bankType.FirstOrDefault(t => t.Code == ChargeTypeCodes.BankInstallment)?.Id;
        var refs = await ctx.RefsAsync(live.Select(x => (int?)x.c.PayeeId).Concat(liveInstallments.Select(x => (int?)x.a.BankId)));
        var branches = await ctx.Branches.FindManyAsync(live.Select(x => x.v.BranchId).Concat(liveInstallments.Select(x => x.v.BranchId)).Where(b => b is not null).Select(b => b!.Value));

        var result = live.Select(x => new PayableModel
        {
            Id = x.e.VehicleRecurringChargeEntryId, Kind = RecurringChargeKind, VehicleId = x.v.VehicleId, VehicleRegistrationNo = x.v.RegistrationNo,
            BranchId = x.v.BranchId, Branch = x.v.BranchId is { } b ? branches.GetValueOrDefault(b)?.Name : null, ChargeId = x.c.VehicleRecurringChargeId,
            ChargeTypeId = x.c.ChargeTypeId, ChargeType = types.GetValueOrDefault(x.c.ChargeTypeId)?.Description, Payee = VehicleContext.Ref(refs, x.c.PayeeId),
            DueDate = x.e.DueDate, ExpectedAmount = x.e.ExpectedAmount, Status = x.e.DueDate < today ? ChargeEntryStatuses.Overdue : x.e.Status
        }).ToList();

        result.AddRange(liveInstallments.Select(x => new PayableModel
        {
            Id = x.i.VehicleInstallmentId, Kind = InstallmentKind, VehicleId = x.v.VehicleId, VehicleRegistrationNo = x.v.RegistrationNo,
            BranchId = x.v.BranchId, Branch = x.v.BranchId is { } b ? branches.GetValueOrDefault(b)?.Name : null, ChargeId = null,
            ChargeTypeId = bankTypeId, ChargeType = bankType.FirstOrDefault(t => t.Code == ChargeTypeCodes.BankInstallment)?.Description ?? "Bank Installment",
            Payee = VehicleContext.Ref(refs, x.a.BankId), DueDate = x.i.DueDate, ExpectedAmount = x.i.ExpectedAmount - x.i.PaidAmount,
            Status = x.i.DueDate < today ? ChargeEntryStatuses.Overdue : ChargeEntryStatuses.Due
        }));
        return result;
    }

    public async Task<BulkConfirmResult> BulkConfirmAsync(BulkConfirmRequest request, VehicleCaller caller)
    {
        // The workbench already shows each row's expected amount; an item with no override pays exactly that, the same figure the user saw and accepted.
        var expected = (await CombinedAsync(v => true)).ToDictionary(p => (p.Kind, p.VehicleId, p.Id), p => p.ExpectedAmount);

        var result = new BulkConfirmResult();
        foreach (var item in request.Items)
        {
            try
            {
                var amount = item.Amount ?? expected.GetValueOrDefault((item.Kind, item.VehicleId, item.Id));
                if (item.Kind == InstallmentKind)
                {
                    await installments.PayAsync(item.VehicleId, item.Id, new PayInstallmentRequest
                    {
                        Amount = amount, PaidOn = request.PaidOn, PaymentMode = request.PaymentMode, RowVersion = item.RowVersion ?? string.Empty
                    }, caller);
                }
                else
                {
                    await charges.ConfirmAsync(item.VehicleId, item.Id, new ConfirmChargeEntryRequest
                    {
                        Amount = amount, PaidOn = request.PaidOn, PaymentMode = request.PaymentMode
                    }, caller);
                }
                result.Succeeded++;
            }
            catch (Exception ex) when (ex is ValidationException or NotFoundException or ConflictException or ForbiddenException)
            {
                result.Failed.Add(new BulkConfirmFailure { Kind = item.Kind, VehicleId = item.VehicleId, Id = item.Id, Message = ex.Message });
            }
        }
        return result;
    }

    public async Task<PayablesSummaryModel> SummaryAsync()
    {
        var all = await CombinedAsync(v => true);
        var today = await ctx.TodayAsync();
        var soon = today.AddDays(7);
        var dueSoon = all.Where(p => p.Status != ChargeEntryStatuses.Overdue && p.DueDate <= soon).ToList();
        var overdue = all.Where(p => p.Status == ChargeEntryStatuses.Overdue).ToList();
        return new PayablesSummaryModel
        {
            DueWithin7Days = dueSoon.Count, DueWithin7DaysAmount = dueSoon.Sum(p => p.ExpectedAmount ?? 0),
            OverdueCount = overdue.Count, OverdueAmount = overdue.Sum(p => p.ExpectedAmount ?? 0)
        };
    }
}
