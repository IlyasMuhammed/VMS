using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Partners;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Vehicles.Services;

public interface ICategoryService
{
    /// <summary>Checks the fields of an ownership category (FSD §17) without saving. Used by the change action and, later, by activation.</summary>
    Task<List<ValidationError>> ValidateAsync(string category, CategoryDetails details, DateOnly today, DateOnly? acquisitionDate, bool requireAmounts = true);
    Task<VehicleModel> ChangeAsync(int vehicleId, ChangeCategoryRequest request, VehicleCaller caller);
}

/// <summary>
/// Ownership category rules (FSD §17) and the Change Category action (BR-VH-005 to BR-VH-008): a change never edits the old
/// relation; it closes it the day before and opens a new one, so what was true then stays true in the history.
/// </summary>
internal sealed class CategoryService(VehicleContext ctx, IVehicleService vehicles, IVehicleFinanceGuard financeGuard, IRecurringChargeService recurringCharges) : ICategoryService
{
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ── Rules ───────────────────────────────────────────────────────────────────────

    public async Task<List<ValidationError>> ValidateAsync(string category, CategoryDetails d, DateOnly today, DateOnly? acquisitionDate, bool requireAmounts = true)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(ctx.Messages.Error(field, code, values));
        string F(string name) => "details." + name;

        if (!OwnershipCategories.All.Contains(category))
        {
            Add("category", Msg.OneOf, ("Field", "Category"), ("Allowed", string.Join(", ", OwnershipCategories.All)));
            return errors;
        }
        if (category == OwnershipCategories.SelfOwned) return errors;

        // The counterparty: who it is called, and which role it must hold.
        var (label, roles) = category switch
        {
            OwnershipCategories.Shared => ("Sharing partner", Array.Empty<string>()),
            OwnershipCategories.Rented => ("Lessor", Array.Empty<string>()),
            OwnershipCategories.CustomerArrangement => ("Customer", new[] { PartnerRoleCodes.Customer, PartnerRoleCodes.RunningCustomer }),
            _ => ("Bank", new[] { PartnerRoleCodes.Bank })
        };
        if (d.CounterpartyId is not > 0) Add(F("counterpartyId"), Msg.VhCounterpartyRequired, ("Counterparty", label));
        else
        {
            var partner = await ctx.Partners.FindAsync(d.CounterpartyId.Value);
            if (partner is null || !partner.IsAvailable) Add(F("counterpartyId"), Msg.Invalid, ("Field", label));   // BR-VH-020
            else if (roles.Length > 0 && !roles.Any(partner.HasRole))
                Add(F("counterpartyId"), Msg.VhCounterpartyLacksRole, ("PartnerName", partner.DisplayName), ("Role", string.Join(" or ", roles.Select(r => r == PartnerRoleCodes.RunningCustomer ? "Running Customer" : r))));
        }

        if (d.AgreementReference is { Length: > 60 }) Add(F("agreementReference"), Msg.MaxLength, ("Field", "Agreement reference"), ("Max", 60));

        // Bank Leased has no fields of its own here: its finance block belongs to the finance module.
        if (category == OwnershipCategories.BankLeased) return errors;

        if (d.StartDate is null) Add(F("startDate"), Msg.Required, ("Field", "Start date"));
        else if (d.StartDate > today) Add(F("startDate"), Msg.NotFuture, ("Field", "Start date"));
        else if (acquisitionDate is { } acquired && d.StartDate < acquired) Add(F("startDate"), Msg.VhItemBeforeAcquisition);   // BR-VH-006
        if (d.EndDate is { } end && d.StartDate is { } start && end <= start) Add(F("endDate"), Msg.VhEndBeforeStart);

        switch (category)
        {
            case OwnershipCategories.Shared:
                if (d.SharePercent is not { } share || share < 0.01m || share > 100m) Add(F("sharePercent"), Msg.VhShareRange);
                if (d.SharingBasis is null || !ArrangementValues.SharingBases.Contains(d.SharingBasis))
                    Add(F("sharingBasis"), Msg.OneOf, ("Field", "Sharing basis"), ("Allowed", string.Join(", ", ArrangementValues.SharingBases)));
                else if (requireAmounts && d.SharingBasis == ArrangementValues.FixedMonthly && d.FixedMonthlyAmount is not > 0)
                    Add(F("fixedMonthlyAmount"), Msg.Required, ("Field", "Fixed monthly amount"));
                if (d.ExpenseSharingRule is null || !ArrangementValues.ExpenseSharingRules.Contains(d.ExpenseSharingRule))
                    Add(F("expenseSharingRule"), Msg.OneOf, ("Field", "Expense sharing rule"), ("Allowed", string.Join(", ", ArrangementValues.ExpenseSharingRules)));
                break;

            case OwnershipCategories.Rented:
                if (requireAmounts && d.RentAmount is not > 0) Add(F("rentAmount"), Msg.Min, ("Field", "Rent amount"), ("Min", "0.01"));
                if (d.RentFrequency is null || !ArrangementValues.RentFrequencies.Contains(d.RentFrequency))
                    Add(F("rentFrequency"), Msg.OneOf, ("Field", "Rent frequency"), ("Allowed", string.Join(", ", ArrangementValues.RentFrequencies)));
                else if (d.RentFrequency == ArrangementValues.Monthly && d.RentDueDay is not (>= 1 and <= 31))
                    Add(F("rentDueDay"), Msg.Required, ("Field", "Rent due day (1 to 31)"));
                if (d.SecurityDeposit < 0) Add(F("securityDeposit"), Msg.Min, ("Field", "Security deposit"), ("Min", 0));
                break;

            case OwnershipCategories.CustomerArrangement:
                if (d.ArrangementType is null || !ArrangementValues.ArrangementTypes.Contains(d.ArrangementType))
                    Add(F("arrangementType"), Msg.OneOf, ("Field", "Arrangement type"), ("Allowed", string.Join(", ", ArrangementValues.ArrangementTypes)));
                else if (requireAmounts && d.ArrangementType == ArrangementValues.DedicatedMonthly && d.AgreedAmount is not > 0)
                    Add(F("agreedAmount"), Msg.Required, ("Field", "Agreed amount"));
                else if (d.ArrangementType == ArrangementValues.RevenueShare && d.RevenueSharePercent is not (>= 0 and <= 100))
                    Add(F("revenueSharePercent"), Msg.Required, ("Field", "Revenue share % (0 to 100)"));
                break;
        }
        return errors;
    }

    /// <summary>A relation row for a validated category and its details, opening on <paramref name="from"/>.</summary>
    internal static VehicleRelation NewRelation(int vehicleId, string category, CategoryDetails d, DateOnly from, bool mayEnterFinance) => new()
    {
        VehicleId = vehicleId, Category = category, CounterpartyId = d.CounterpartyId, EffectiveFrom = from, AgreementEndDate = d.EndDate,
        AgreementReference = Blank(d.AgreementReference),
        SharePercent = category == OwnershipCategories.Shared ? d.SharePercent : null,
        SharingBasis = category == OwnershipCategories.Shared ? d.SharingBasis : null,
        FixedMonthlyAmount = category == OwnershipCategories.Shared && d.SharingBasis == ArrangementValues.FixedMonthly && mayEnterFinance ? d.FixedMonthlyAmount : null,
        ExpenseSharingRule = category == OwnershipCategories.Shared ? d.ExpenseSharingRule : null,
        RentAmount = category == OwnershipCategories.Rented && mayEnterFinance ? d.RentAmount : null,
        RentFrequency = category == OwnershipCategories.Rented ? d.RentFrequency : null,
        RentDueDay = category == OwnershipCategories.Rented && d.RentFrequency == ArrangementValues.Monthly ? d.RentDueDay : null,
        SecurityDeposit = category == OwnershipCategories.Rented && mayEnterFinance ? d.SecurityDeposit : null,
        ArrangementType = category == OwnershipCategories.CustomerArrangement ? d.ArrangementType : null,
        AgreedAmount = category == OwnershipCategories.CustomerArrangement && d.ArrangementType == ArrangementValues.DedicatedMonthly && mayEnterFinance ? d.AgreedAmount : null,
        RevenueSharePercent = category == OwnershipCategories.CustomerArrangement && d.ArrangementType == ArrangementValues.RevenueShare ? d.RevenueSharePercent : null
    };

    // ── The change ──────────────────────────────────────────────────────────────────

    public async Task<VehicleModel> ChangeAsync(int vehicleId, ChangeCategoryRequest request, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadAsync(vehicleId);
        if (ctx.NotInFleet(vehicle, "given another category") is { } wrong) throw wrong;

        var today = await ctx.TodayAsync();
        var effective = request.EffectiveDate ?? today;
        request.Details.StartDate = effective;   // the arrangement starts the day the category changes
        // An amount is asked for only of someone who may enter it; anyone else's change carries over what the arrangement already has.
        var mayEnterFinance = caller.Has(PermissionCodes.VEH_FIELD_FINANCE_VIEW);
        var errors = await ValidateAsync(request.Category, request.Details, today, vehicle.AcquisitionDate, requireAmounts: mayEnterFinance);
        if (effective > today) errors.Add(ctx.Messages.Error("effectiveDate", Msg.NotFuture, ("Field", "Effective date")));
        if (request.Reason is { Length: > 500 }) errors.Add(ctx.Messages.Error("reason", Msg.MaxLength, ("Field", "Reason"), ("Max", 500)));
        if (vehicle.AcquisitionDate is { } acquired && effective < acquired) errors.Add(ctx.Messages.Error("effectiveDate", Msg.VhItemBeforeAcquisition));   // BR-VH-006

        var open = await ctx.Db.Relations.FirstOrDefaultAsync(r => r.TenantId == ctx.Tenant && r.VehicleId == vehicleId && r.EffectiveTo == null);
        // BR-VH-006: the new relation must not overlap the one it replaces.
        if (open is not null && effective <= open.EffectiveFrom) errors.Add(ctx.Messages.Error("effectiveDate", Msg.Min, ("Field", "Effective date"), ("Min", open.EffectiveFrom.AddDays(1).ToString("yyyy-MM-dd"))));
        if (errors.Count > 0) throw new ValidationException(errors);

        var from = vehicle.CurrentCategory ?? OwnershipCategories.SelfOwned;
        var next = request.Category == OwnershipCategories.SelfOwned ? null : NewRelation(vehicleId, request.Category, request.Details, effective, mayEnterFinance);

        // Amounts the caller may not see are carried over from the arrangement they are changing, never wiped or invented.
        if (next is not null && !mayEnterFinance && open is not null && open.Category == next.Category)
            (next.FixedMonthlyAmount, next.RentAmount, next.SecurityDeposit, next.AgreedAmount) = (open.FixedMonthlyAmount, open.RentAmount, open.SecurityDeposit, open.AgreedAmount);

        // Something must actually be different: another category, another partner or other terms (BR-VH-008 makes a change of terms a new relation too).
        if (request.Category == from && SameTerms(open, next))
            throw new ConflictException("Nothing about the arrangement is different, so there is nothing to change.");

        // BR-VH-007: only Bank Leased may follow an agreement that still has a balance.
        if (request.Category != OwnershipCategories.BankLeased && await financeGuard.HasOutstandingFinanceAsync(vehicleId))
            throw new ValidationException(ctx.Messages.Error("category", Msg.VhCategoryLockedByFinance));

        await ctx.Db.InTransactionAsync(async ct =>
        {
            if (open is not null)
            {
                open.EffectiveTo = effective.AddDays(-1);   // BR-VH-005: closed the day before the change
                await recurringCharges.EndDateForClosedCategoryAsync(vehicleId, open.Category, open.EffectiveTo.Value, ct);   // BR-VH-035
            }
            await ctx.Db.SaveChangesAsync(ct);   // the database allows one open relation, so the old one is closed first
            if (next is not null) ctx.Db.Relations.Add(next);

            vehicle.CurrentCategory = request.Category;
            vehicle.CurrentCounterpartyId = next?.CounterpartyId;
            vehicle.ModifiedBy = caller.UserId;
            vehicle.ModifiedOn = DateTime.UtcNow;
            ctx.LogEvent(vehicle, LifecycleEvents.CategoryChange, effective, caller, fromCategory: from, toCategory: request.Category, reason: request.Reason, counterpartyId: next?.CounterpartyId);
            await ctx.Db.SaveChangesAsync(ct);
        });

        return await vehicles.GetAsync(vehicleId);
    }

    private static bool SameTerms(VehicleRelation? a, VehicleRelation? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return (a.CounterpartyId, a.AgreementEndDate, a.AgreementReference, a.SharePercent, a.SharingBasis, a.FixedMonthlyAmount, a.ExpenseSharingRule, a.RentAmount, a.RentFrequency, a.RentDueDay, a.SecurityDeposit, a.ArrangementType, a.AgreedAmount, a.RevenueSharePercent)
            == (b.CounterpartyId, b.AgreementEndDate, b.AgreementReference, b.SharePercent, b.SharingBasis, b.FixedMonthlyAmount, b.ExpenseSharingRule, b.RentAmount, b.RentFrequency, b.RentDueDay, b.SecurityDeposit, b.ArrangementType, b.AgreedAmount, b.RevenueSharePercent);
    }
}
