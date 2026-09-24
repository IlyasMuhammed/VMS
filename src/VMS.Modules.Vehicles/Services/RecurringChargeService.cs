using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;
using VMS.Shared.Messages;
using VMS.Shared.Notifications;
using VMS.Shared.Numbering;
using VMS.Shared.Partners;

namespace VMS.Modules.Vehicles.Services;

public interface IRecurringChargeService
{
    /// <summary>Every version of the vehicle's charges: the current one of each series, and history once amended or ended (BR-VH-033).</summary>
    Task<List<RecurringChargeModel>> ListAsync(int vehicleId);
    Task<RecurringChargeModel> CreateAsync(int vehicleId, SaveRecurringChargeRequest request, VehicleCaller caller);
    /// <summary>An amendment: the current row is end-dated and a new one takes its place from the effective date (BR-VH-033).</summary>
    Task<RecurringChargeModel> AmendAsync(int vehicleId, int chargeId, SaveRecurringChargeRequest request, VehicleCaller caller);
    Task<RecurringChargeModel> EndAsync(int vehicleId, int chargeId, EndRecurringChargeRequest request, VehicleCaller caller);

    Task<ChargeEntryPaymentModel> ConfirmAsync(int vehicleId, int entryId, ConfirmChargeEntryRequest request, VehicleCaller caller);
    Task WaiveAsync(int vehicleId, int entryId, WaiveChargeEntryRequest request, VehicleCaller caller);
    Task CancelAsync(int vehicleId, int entryId, WaiveChargeEntryRequest request, VehicleCaller caller);

    /// <summary>BR-VH-034: called by <c>LifecycleService.DisposeAsync</c>, inside its own transaction, before it saves.</summary>
    Task EndDateForDisposalAsync(int vehicleId, DateOnly disposalDate, CancellationToken ct);
    /// <summary>BR-VH-035: called by <c>CategoryService.ChangeAsync</c>, inside its own transaction, before it saves.</summary>
    Task EndDateForClosedCategoryAsync(int vehicleId, string closedCategory, DateOnly closeDate, CancellationToken ct);
}

/// <summary>
/// Recurring charges (FSD §19A): insurance, tracker fees, rent and the rest, configured once and generated nightly (see
/// <see cref="IRecurringChargeGenerator"/>). A Bank Installment is never configured here — it is the finance agreement's own
/// schedule, surfaced instead of duplicated (BR-VH-029, <see cref="IPayablesService"/>).
/// </summary>
internal sealed class RecurringChargeService(VehicleContext ctx, ILookupReader lookups, INotificationTrigger notifications) : IRecurringChargeService
{
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ── Reading ─────────────────────────────────────────────────────────────────────

    public async Task<List<RecurringChargeModel>> ListAsync(int vehicleId)
    {
        await ctx.LoadReadOnlyAsync(vehicleId);
        var rows = await ctx.Db.RecurringCharges.AsNoTracking().Where(c => c.TenantId == ctx.Tenant && c.VehicleId == vehicleId)
            .OrderBy(c => c.SeriesId).ThenByDescending(c => c.StartDate).ToListAsync();
        return await ToModelsAsync(rows);
    }

    private async Task<List<RecurringChargeModel>> ToModelsAsync(IReadOnlyCollection<VehicleRecurringCharge> rows)
    {
        var types = await lookups.FindManyAsync(PlatformLookups.RecurringChargeType, rows.Select(c => c.ChargeTypeId));
        var expenseTypes = await lookups.FindManyAsync(PlatformLookups.ExpenseType, rows.Select(c => c.ExpenseTypeId));
        var refs = await ctx.RefsAsync(rows.Select(c => (int?)c.PayeeId));
        return rows.Select(c => new RecurringChargeModel
        {
            Id = c.VehicleRecurringChargeId, SeriesId = c.SeriesId, VehicleId = c.VehicleId, ChargeTypeId = c.ChargeTypeId, ChargeType = types.GetValueOrDefault(c.ChargeTypeId)?.Description,
            Payee = VehicleContext.Ref(refs, c.PayeeId), ExpenseTypeId = c.ExpenseTypeId, ExpenseType = expenseTypes.GetValueOrDefault(c.ExpenseTypeId)?.Description,
            Amount = c.Amount, AmountBasis = c.AmountBasis, Frequency = c.Frequency, DueDay = c.DueDay, DueMonth = c.DueMonth, CustomIntervalDays = c.CustomIntervalDays,
            StartDate = c.StartDate, EndDate = c.EndDate, OccurrenceCount = c.OccurrenceCount, GeneratedCount = c.GeneratedCount, PostingMode = c.PostingMode,
            GenerateLeadDays = c.GenerateLeadDays, TaxWithholdingPercent = c.TaxWithholdingPercent, NextDueDate = c.NextDueDate, IsActive = c.EffectiveTo is null,
            EffectiveTo = c.EffectiveTo, EndReason = c.EndReason, RowVersion = Convert.ToBase64String(c.RowVersion)
        }).ToList();
    }

    // ── Configuring ─────────────────────────────────────────────────────────────────

    /// <summary>Checks the fields of FSD §19A.1 and builds an unsaved row. Shared by create and amend, which differ only in the row it replaces.</summary>
    private async Task<(VehicleRecurringCharge? Charge, string? TypeCode, List<ValidationError> Errors)> PrepareAsync(int vehicleId, SaveRecurringChargeRequest r, DateOnly? acquired, DateOnly today, VehicleCaller caller)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(ctx.Messages.Error(field, code, values));

        var chargeType = r.ChargeTypeId is > 0 ? await lookups.FindAsync(PlatformLookups.RecurringChargeType, r.ChargeTypeId.Value) : null;
        if (chargeType is not { IsActive: true }) Add("chargeTypeId", Msg.Invalid, ("Field", "Charge type"));

        var expenseType = r.ExpenseTypeId is > 0 ? await lookups.FindAsync(PlatformLookups.ExpenseType, r.ExpenseTypeId.Value) : null;
        if (expenseType is not { IsActive: true }) Add("expenseTypeId", Msg.Invalid, ("Field", "Expense type"));

        if (chargeType is not null && chargeType.Code == ChargeTypeCodes.BankInstallment)
        {
            // BR-VH-029: the finance agreement's own schedule is the Bank Installment charge. A manual one only clashes once there is an agreement to duplicate.
            var financed = await ctx.Db.Agreements.AnyAsync(a => a.TenantId == ctx.Tenant && a.VehicleId == vehicleId && a.Status == AgreementStatuses.Active);
            if (financed) Add("chargeTypeId", Msg.VhBankInstallmentAlreadyFinanced);
        }

        if (r.PayeeId is not > 0) Add("payeeId", Msg.Required, ("Field", "Payee"));
        else
        {
            var partner = await ctx.Partners.FindAsync(r.PayeeId.Value);
            if (partner is null || !partner.IsAvailable) Add("payeeId", Msg.Invalid, ("Field", "Payee"));
            else if (chargeType is not null && ChargeTypeCodes.PayeeRole.TryGetValue(chargeType.Code, out var role) && !partner.HasRole(role))
                Add("payeeId", Msg.VhCounterpartyLacksRole, ("PartnerName", partner.DisplayName), ("Role", role == PartnerRoleCodes.TrackerCompany ? "Tracker Company" : role));
        }

        var basis = r.AmountBasis ?? string.Empty;
        if (!ChargeAmountBases.All.Contains(basis)) Add("amountBasis", Msg.OneOf, ("Field", "Amount basis"), ("Allowed", string.Join(", ", ChargeAmountBases.All)));
        else if (basis != ChargeAmountBases.Variable && r.Amount is not > 0) Add("amount", Msg.Required, ("Field", "Amount"));
        if (r.Amount is < 0) Add("amount", Msg.Min, ("Field", "Amount"), ("Min", "0"));

        var frequency = r.Frequency ?? string.Empty;
        if (!ChargeFrequencies.All.Contains(frequency)) Add("frequency", Msg.OneOf, ("Field", "Frequency"), ("Allowed", string.Join(", ", ChargeFrequencies.All)));
        else if (frequency == ChargeFrequencies.CustomDays && r.CustomIntervalDays is not (>= 1 and <= 365)) Add("customIntervalDays", Msg.Required, ("Field", "Interval (1 to 365 days)"));
        else if (frequency is ChargeFrequencies.Monthly or ChargeFrequencies.Quarterly or ChargeFrequencies.HalfYearly or ChargeFrequencies.Yearly && r.DueDay is not (>= 1 and <= 31))
            Add("dueDay", Msg.Required, ("Field", "Due day (1 to 31)"));

        var postingMode = r.PostingMode ?? ChargePostingModes.GenerateAsDue;
        if (!ChargePostingModes.All.Contains(postingMode)) Add("postingMode", Msg.OneOf, ("Field", "Posting mode"), ("Allowed", string.Join(", ", ChargePostingModes.All)));
        else if (postingMode == ChargePostingModes.AutoPost)
        {
            if (basis != ChargeAmountBases.Fixed) Add("postingMode", Msg.VhAutoPostNeedsFixedAmount);   // BR-VH-027
            if (!caller.Has(PermissionCodes.FIN_RECURRING_AUTOPOST)) throw new ForbiddenException("Only an Admin may set a recurring charge to Auto-post.");   // BR-VH-028
        }

        var start = r.StartDate ?? today;
        if (acquired is { } day && start < day) Add("startDate", Msg.Min, ("Field", "Start date"), ("Min", day.ToString("yyyy-MM-dd")));
        if (r.EndDate is { } end && end <= start) Add("endDate", Msg.VhEndBeforeStart);
        if (r.EndDate is not null && r.OccurrenceCount is not null) Add("endDate", Msg.VhEndDateOrOccurrences);   // mutually exclusive
        if (r.OccurrenceCount is < 1) Add("occurrenceCount", Msg.Min, ("Field", "Number of occurrences"), ("Min", "1"));

        var leadDays = r.GenerateLeadDays ?? 7;
        if (leadDays < 0 || leadDays > 90) Add("generateLeadDays", Msg.Invalid, ("Field", "Generate lead days"));

        if (r.TaxWithholdingPercent is < 0 or > 100) Add("taxWithholdingPercent", Msg.Invalid, ("Field", "Tax / withholding %"));

        if (errors.Count > 0) return (null, chargeType?.Code, errors);

        var charge = new VehicleRecurringCharge
        {
            VehicleId = vehicleId, ChargeTypeId = r.ChargeTypeId!.Value, PayeeId = r.PayeeId!.Value, ExpenseTypeId = r.ExpenseTypeId!.Value,
            Amount = basis == ChargeAmountBases.Variable ? r.Amount : r.Amount, AmountBasis = basis, Frequency = frequency,
            DueDay = frequency == ChargeFrequencies.CustomDays ? null : r.DueDay, DueMonth = frequency == ChargeFrequencies.Yearly ? r.DueMonth : null,
            CustomIntervalDays = frequency == ChargeFrequencies.CustomDays ? r.CustomIntervalDays : null,
            StartDate = start, EndDate = r.EndDate, OccurrenceCount = r.OccurrenceCount, PostingMode = postingMode, GenerateLeadDays = leadDays,
            TaxWithholdingPercent = r.TaxWithholdingPercent, CreatedBy = caller.UserId, CreatedOn = DateTime.UtcNow
        };
        charge.NextDueDate = ChargeSchedule.FirstDue(start, frequency, charge.DueDay, charge.DueMonth);
        return (charge, chargeType!.Code, errors);
    }

    public async Task<RecurringChargeModel> CreateAsync(int vehicleId, SaveRecurringChargeRequest request, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadReadOnlyAsync(vehicleId);
        var today = await ctx.TodayAsync();
        var (charge, _, errors) = await PrepareAsync(vehicleId, request, vehicle.AcquisitionDate, today, caller);
        if (charge is null) throw new ValidationException(errors);

        charge.SeriesId = Guid.NewGuid();
        ctx.Db.RecurringCharges.Add(charge);
        await ctx.Db.SaveChangesAsync();
        // FR-VH-008's "immediately" (ChargeEnding, when an end date was set at configure time): picked up now, not at the next hourly run.
        if (charge.EndDate is not null) await notifications.NotifyAsync(nameof(VehicleRecurringCharge), charge.VehicleRecurringChargeId.ToString());
        return (await ToModelsAsync([charge]))[0];
    }

    public async Task<RecurringChargeModel> AmendAsync(int vehicleId, int chargeId, SaveRecurringChargeRequest request, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadReadOnlyAsync(vehicleId);
        var current = await ctx.Db.RecurringCharges.FirstOrDefaultAsync(c => c.TenantId == ctx.Tenant && c.VehicleId == vehicleId && c.VehicleRecurringChargeId == chargeId)
            ?? throw new NotFoundException("Recurring charge not found.");
        if (current.EffectiveTo is not null) throw new ConflictException("This charge has already been superseded or ended. Amend its current version instead.");
        if (string.IsNullOrWhiteSpace(request.RowVersion)) throw new ValidationException(ctx.Messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));

        var today = await ctx.TodayAsync();
        var (replacement, _, errors) = await PrepareAsync(vehicleId, request, vehicle.AcquisitionDate, today, caller);
        var effective = request.StartDate ?? today;
        if (effective < today) errors.Add(ctx.Messages.Error("startDate", Msg.NotFuture, ("Field", "Effective date")));   // an amendment starts today or later; it does not rewrite the past
        if (effective <= current.StartDate) errors.Add(ctx.Messages.Error("startDate", Msg.Min, ("Field", "Effective date"), ("Min", current.StartDate.AddDays(1).ToString("yyyy-MM-dd"))));
        if (errors.Count > 0) throw new ValidationException(errors);

        try { ctx.Db.Entry(current).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion!); }
        catch (FormatException) { throw new ValidationException(ctx.Messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        replacement!.SeriesId = current.SeriesId;
        try
        {
            await ctx.Db.InTransactionAsync(async ct =>
            {
                current.EffectiveTo = effective.AddDays(-1);
                current.ModifiedBy = caller.UserId;
                current.ModifiedOn = DateTime.UtcNow;
                await ctx.Db.SaveChangesAsync(ct);
                ctx.Db.RecurringCharges.Add(replacement);
                await ctx.Db.SaveChangesAsync(ct);
            });
        }
        catch (DbUpdateConcurrencyException) { throw await ctx.StaleAsync(vehicleId); }
        if (replacement.EndDate is not null) await notifications.NotifyAsync(nameof(VehicleRecurringCharge), replacement.VehicleRecurringChargeId.ToString());
        return (await ToModelsAsync([replacement]))[0];
    }

    public async Task<RecurringChargeModel> EndAsync(int vehicleId, int chargeId, EndRecurringChargeRequest request, VehicleCaller caller)
    {
        await ctx.LoadReadOnlyAsync(vehicleId);
        var current = await ctx.Db.RecurringCharges.FirstOrDefaultAsync(c => c.TenantId == ctx.Tenant && c.VehicleId == vehicleId && c.VehicleRecurringChargeId == chargeId)
            ?? throw new NotFoundException("Recurring charge not found.");
        if (current.EffectiveTo is not null) throw new ConflictException("This charge has already ended.");

        var today = await ctx.TodayAsync();
        var end = request.EndDate ?? today;
        var reason = Blank(request.Reason);
        var errors = new List<ValidationError>();
        if (end < current.StartDate) errors.Add(ctx.Messages.Error("endDate", Msg.Min, ("Field", "End date"), ("Min", current.StartDate.ToString("yyyy-MM-dd"))));
        if (reason is null) errors.Add(ctx.Messages.Error("reason", Msg.Required, ("Field", "Reason")));
        else if (reason.Length > 500) errors.Add(ctx.Messages.Error("reason", Msg.MaxLength, ("Field", "Reason"), ("Max", 500)));
        if (errors.Count > 0) throw new ValidationException(errors);

        current.EffectiveTo = end;
        current.EndReason = reason;
        current.NextDueDate = null;
        current.ModifiedBy = caller.UserId;
        current.ModifiedOn = DateTime.UtcNow;
        await ctx.Db.SaveChangesAsync();
        return (await ToModelsAsync([current]))[0];
    }

    // ── Confirming, waiving, cancelling an entry (FSD §19A.4) ──────────────────────────

    private async Task<(VehicleRecurringChargeEntry Entry, VehicleRecurringCharge Charge)> LoadEntryAsync(int vehicleId, int entryId)
    {
        var entry = await ctx.Db.RecurringChargeEntries.FirstOrDefaultAsync(e => e.TenantId == ctx.Tenant && e.VehicleId == vehicleId && e.VehicleRecurringChargeEntryId == entryId)
            ?? throw new NotFoundException("Entry not found.");
        var charge = await ctx.Db.RecurringCharges.FirstAsync(c => c.VehicleRecurringChargeId == entry.VehicleRecurringChargeId);
        return (entry, charge);
    }

    public async Task<ChargeEntryPaymentModel> ConfirmAsync(int vehicleId, int entryId, ConfirmChargeEntryRequest request, VehicleCaller caller)
    {
        if (!caller.Has(PermissionCodes.FIN_DUE_CONFIRM) || !caller.Has(PermissionCodes.VEH_FIELD_COST_VIEW))
            throw new ForbiddenException("You do not have permission to confirm due payments.");

        var vehicle = await ctx.LoadAsync(vehicleId);
        var (entry, charge) = await LoadEntryAsync(vehicleId, entryId);
        if (entry.Status is not (ChargeEntryStatuses.Due or ChargeEntryStatuses.Overdue))
            throw new ValidationException(ctx.Messages.Error("status", Msg.VhEntryWrongStatus, ("Status", entry.Status), ("Action", "confirmed")));

        var today = await ctx.TodayAsync();
        var paidOn = request.PaidOn ?? today;
        var amount = request.Amount ?? entry.ExpectedAmount;
        var mode = Blank(request.PaymentMode);
        var reference = Blank(request.Reference);
        var remarks = Blank(request.Remarks);

        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(ctx.Messages.Error(field, code, values));
        if (amount <= 0) Add("amount", Msg.Min, ("Field", "Amount"), ("Min", "0.01"));
        if (paidOn > today) Add("paidOn", Msg.NotFuture, ("Field", "Payment date"));
        else if (vehicle.AcquisitionDate is { } acquired && paidOn < acquired) Add("paidOn", Msg.Min, ("Field", "Payment date"), ("Min", acquired.ToString("yyyy-MM-dd")));
        if (mode is not null && !PaymentModes.All.Contains(mode)) Add("paymentMode", Msg.OneOf, ("Field", "Payment mode"), ("Allowed", string.Join(", ", PaymentModes.All)));
        if (reference is { Length: > 120 }) Add("reference", Msg.MaxLength, ("Field", "Reference"), ("Max", 120));
        if (remarks is { Length: > 500 }) Add("remarks", Msg.MaxLength, ("Field", "Remarks"), ("Max", 500));
        if (errors.Count > 0) throw new ValidationException(errors);

        var typeCode = (await lookups.FindAsync(PlatformLookups.RecurringChargeType, charge.ChargeTypeId))?.Code ?? "OTHER";
        await ctx.Db.InTransactionAsync(ct => RecurringChargePosting.PostAsync(ctx, entry, charge, typeCode, amount, paidOn, mode, reference, remarks, caller.UserId, isAutoPosted: false, ct));
        return new ChargeEntryPaymentModel { TransactionId = entry.TransactionId!.Value };
    }

    public async Task WaiveAsync(int vehicleId, int entryId, WaiveChargeEntryRequest request, VehicleCaller caller)
    {
        if (!caller.Has(PermissionCodes.FIN_DUE_WAIVE)) throw new ForbiddenException("You do not have permission to waive due entries.");
        await ctx.LoadReadOnlyAsync(vehicleId);
        var (entry, _) = await LoadEntryAsync(vehicleId, entryId);
        await SetAsideAsync(entry, request.Reason, ChargeEntryStatuses.Waived, "waived", caller);
    }

    public async Task CancelAsync(int vehicleId, int entryId, WaiveChargeEntryRequest request, VehicleCaller caller)
    {
        if (!caller.Has(PermissionCodes.FIN_RECURRING_MANAGE)) throw new ForbiddenException("You do not have permission to cancel a due entry.");
        await ctx.LoadReadOnlyAsync(vehicleId);
        var (entry, _) = await LoadEntryAsync(vehicleId, entryId);
        await SetAsideAsync(entry, request.Reason, ChargeEntryStatuses.Cancelled, "cancelled", caller);
    }

    private async Task SetAsideAsync(VehicleRecurringChargeEntry entry, string? rawReason, string status, string action, VehicleCaller caller)
    {
        if (entry.Status is not (ChargeEntryStatuses.Due or ChargeEntryStatuses.Overdue))
            throw new ValidationException(ctx.Messages.Error("status", Msg.VhEntryWrongStatus, ("Status", entry.Status), ("Action", action)));   // BR-VH-032: a Paid entry cannot be waived or cancelled

        var reason = Blank(rawReason);
        var errors = new List<ValidationError>();
        if (reason is null) errors.Add(ctx.Messages.Error("reason", Msg.Required, ("Field", "Reason")));
        else if (reason.Length > 500) errors.Add(ctx.Messages.Error("reason", Msg.MaxLength, ("Field", "Reason"), ("Max", 500)));
        if (errors.Count > 0) throw new ValidationException(errors);

        entry.Status = status;
        entry.WaiveReason = reason;
        entry.ConfirmedBy = caller.UserId;
        entry.ConfirmedOn = DateTime.UtcNow;
        await ctx.Db.SaveChangesAsync();
    }

    // ── Cascades (called from other vehicle services, in their own transaction) ────────

    /// <summary>BR-VH-034: disposal end-dates every active charge, and cancels Due or Overdue entries not yet due — what is already owed stays payable.</summary>
    public async Task EndDateForDisposalAsync(int vehicleId, DateOnly disposalDate, CancellationToken ct)
    {
        var active = await ctx.Db.RecurringCharges.Where(c => c.TenantId == ctx.Tenant && c.VehicleId == vehicleId && c.EffectiveTo == null).ToListAsync(ct);
        foreach (var charge in active)
        {
            charge.EffectiveTo = disposalDate;
            charge.EndReason = "Vehicle disposed";
            charge.NextDueDate = null;
        }
        if (active.Count == 0) return;

        var ids = active.Select(c => c.VehicleRecurringChargeId).ToList();
        var future = await ctx.Db.RecurringChargeEntries
            .Where(e => e.TenantId == ctx.Tenant && ids.Contains(e.VehicleRecurringChargeId) && (e.Status == ChargeEntryStatuses.Due || e.Status == ChargeEntryStatuses.Overdue) && e.DueDate > disposalDate)
            .ToListAsync(ct);
        foreach (var entry in future) entry.Status = ChargeEntryStatuses.Cancelled;
    }

    /// <summary>BR-VH-035: the charge tied to a closed relation — rent on a Rented vehicle, payout on a Shared one — is end-dated with it.</summary>
    public async Task EndDateForClosedCategoryAsync(int vehicleId, string closedCategory, DateOnly closeDate, CancellationToken ct)
    {
        var tiedTypeCode = closedCategory switch
        {
            OwnershipCategories.Rented => ChargeTypeCodes.VehicleRentPayable,
            OwnershipCategories.Shared => ChargeTypeCodes.SharedPartnerPayout,
            _ => (string?)null
        };
        if (tiedTypeCode is null) return;

        var active = await ctx.Db.RecurringCharges.Where(c => c.TenantId == ctx.Tenant && c.VehicleId == vehicleId && c.EffectiveTo == null).ToListAsync(ct);
        if (active.Count == 0) return;
        var types = await lookups.FindManyAsync(PlatformLookups.RecurringChargeType, active.Select(c => c.ChargeTypeId));
        var tied = active.Where(c => types.GetValueOrDefault(c.ChargeTypeId)?.Code == tiedTypeCode).ToList();
        foreach (var charge in tied)
        {
            charge.EffectiveTo = closeDate;
            charge.EndReason = "Ownership category changed";
            charge.NextDueDate = null;
        }
    }
}
