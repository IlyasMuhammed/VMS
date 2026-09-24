using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Partners;

namespace VMS.Modules.Vehicles.Services;

public interface IFinanceService
{
    /// <summary>The vehicle's open agreement (Draft or Active), or null when it has none.</summary>
    Task<AgreementModel?> GetAsync(int vehicleId);
    Task<AgreementModel> SaveAsync(int vehicleId, SaveFinanceRequest request, VehicleCaller caller);
    /// <summary>Removes the finance block of a Draft, for a vehicle that turned out to be paid for in full.</summary>
    Task RemoveAsync(int vehicleId, VehicleCaller caller);
}

/// <summary>
/// The finance block of a Draft vehicle (FSD §18.2): the bank's agreement and its terms. It is held here until the vehicle is
/// activated, when the installment schedule is generated from it and the down payment is posted with the acquisition (§19).
/// </summary>
internal sealed class FinanceService(VehicleContext ctx, ILookupReader lookups) : IFinanceService
{
    private const int AgreementGraceDays = 90;
    private const decimal MaxAmount = AcquisitionService.MaxAmount;

    private IQueryable<VehicleFinanceAgreement> Open(int vehicleId) =>
        ctx.Db.Agreements.Where(a => a.TenantId == ctx.Tenant && a.VehicleId == vehicleId && (a.Status == AgreementStatuses.Draft || a.Status == AgreementStatuses.Active));

    // ── Reading ─────────────────────────────────────────────────────────────────────

    public async Task<AgreementModel?> GetAsync(int vehicleId)
    {
        await ctx.LoadReadOnlyAsync(vehicleId);
        var agreement = await Open(vehicleId).AsNoTracking().FirstOrDefaultAsync();
        return agreement is null ? null : await ToModelAsync(agreement);
    }

    private async Task<AgreementModel> ToModelAsync(VehicleFinanceAgreement a)
    {
        var type = await lookups.FindAsync(PlatformLookups.FinanceType, a.FinanceTypeId);
        var refs = await ctx.RefsAsync([a.BankId]);
        return new AgreementModel
        {
            Id = a.VehicleFinanceAgreementId, VehicleId = a.VehicleId, FinanceTypeId = a.FinanceTypeId, FinanceType = type?.Description, BankId = a.BankId,
            Bank = VehicleContext.Ref(refs, a.BankId), AgreementNo = a.AgreementNo, AgreementDate = a.AgreementDate, FinanceAmount = a.FinanceAmount,
            DownPayment = a.DownPayment, InstallmentAmount = a.InstallmentAmount, Frequency = a.Frequency, Tenure = a.Tenure, FirstDueDate = a.FirstDueDate,
            MarkupRate = a.MarkupRate, ResidualAmount = a.ResidualAmount, SecurityDeposit = a.SecurityDeposit, TotalPayable = a.InstallmentAmount * a.Tenure,
            Status = a.Status, RowVersion = Convert.ToBase64String(a.RowVersion)
        };
    }

    // ── Saving ──────────────────────────────────────────────────────────────────────

    public async Task<AgreementModel> SaveAsync(int vehicleId, SaveFinanceRequest request, VehicleCaller caller)
    {
        // The down payment is checked against what was paid, and the amounts are the caller's to enter only if they can see them.
        if (!caller.Has(PermissionCodes.VEH_FIELD_FINANCE_VIEW) || !caller.Has(PermissionCodes.VEH_FIELD_COST_VIEW))
            throw new ForbiddenException("You do not have permission to enter vehicle finance.");

        var vehicle = await ctx.LoadAsync(vehicleId);
        EnsureDraft(vehicle);

        request.AgreementNo = request.AgreementNo?.Trim() ?? string.Empty;
        request.Frequency = request.Frequency?.Trim() ?? string.Empty;

        var existing = await Open(vehicleId).FirstOrDefaultAsync();
        var acquisition = await ctx.Db.Acquisitions.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == ctx.Tenant && a.VehicleId == vehicleId);

        var errors = Validate(request, vehicle.AcquisitionDate);
        if (existing is not null && string.IsNullOrWhiteSpace(request.RowVersion)) errors.Add(ctx.Messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));
        await CheckReferencesAsync(request, existing, errors);
        if (errors.Count > 0) throw new ValidationException(errors);

        // The bank's own number is never reused, not even for a settled agreement.
        if (request.AgreementNo.Length > 0 && await AgreementHolderAsync(request.BankId, request.AgreementNo, existing?.VehicleFinanceAgreementId ?? 0) is { } holder)
            errors.Add(ctx.Messages.Error("agreementNo", Msg.VhChassisOrEngineDuplicate, ("field", "agreement number"), ("VehicleCode", holder)));

        // BR-VH-009 blocks; BR-VH-010 only asks.
        errors.AddRange(CrossCheck(ctx, request.DownPayment!.Value, acquisition?.AmountPaid));
        if (errors.Count > 0) throw new ValidationException(errors);
        if (!request.ConfirmMismatch && Reconciliation(request.FinanceAmount!.Value, request.DownPayment!.Value, acquisition?.PurchasePrice) is { } mismatch)
            throw new ValidationException(ctx.Messages.Error("financeAmount", Msg.VhFinanceNotReconciled, ("x", mismatch.Sum), ("y", mismatch.Price)));

        try
        {
            if (existing is not null) ctx.Db.Entry(existing).Property(a => a.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion!);
        }
        catch (FormatException) { throw new ValidationException(ctx.Messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        try
        {
            await ctx.Db.InTransactionAsync(async ct =>
            {
                var now = DateTime.UtcNow;
                var agreement = existing;
                if (agreement is null)
                    ctx.Db.Agreements.Add(agreement = new VehicleFinanceAgreement { VehicleId = vehicleId, Status = AgreementStatuses.Draft, CreatedBy = caller.UserId, CreatedOn = now });
                agreement.FinanceTypeId = request.FinanceTypeId;
                agreement.BankId = request.BankId;
                agreement.AgreementNo = request.AgreementNo;
                agreement.AgreementDate = request.AgreementDate!.Value;
                agreement.FinanceAmount = request.FinanceAmount!.Value;
                agreement.DownPayment = request.DownPayment!.Value;
                agreement.InstallmentAmount = request.InstallmentAmount!.Value;
                agreement.Frequency = request.Frequency;
                agreement.Tenure = request.Tenure!.Value;
                agreement.FirstDueDate = request.FirstDueDate!.Value;
                agreement.MarkupRate = request.MarkupRate;
                agreement.ResidualAmount = request.ResidualAmount;
                agreement.SecurityDeposit = request.SecurityDeposit;
                agreement.ModifiedBy = caller.UserId;
                agreement.ModifiedOn = now;
                await ctx.Db.SaveChangesAsync(ct);
            });
        }
        catch (DbUpdateConcurrencyException) { throw await ctx.StaleAsync(vehicleId); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Two people saving at once: the database allows one open agreement per vehicle and one bank number per bank.
            var taken = await AgreementHolderAsync(request.BankId, request.AgreementNo, 0);
            throw taken is not null
                ? new ValidationException(ctx.Messages.Error("agreementNo", Msg.VhChassisOrEngineDuplicate, ("field", "agreement number"), ("VehicleCode", taken)))
                : await ctx.StaleAsync(vehicleId);
        }

        return (await GetAsync(vehicleId))!;
    }

    public async Task RemoveAsync(int vehicleId, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadAsync(vehicleId);
        EnsureDraft(vehicle);
        var agreement = await Open(vehicleId).FirstOrDefaultAsync() ?? throw new NotFoundException("This vehicle has no finance agreement.");
        ctx.Db.Agreements.Remove(agreement);
        await ctx.Db.SaveChangesAsync();
    }

    private void EnsureDraft(Vehicle vehicle)
    {
        if (vehicle.Status != VehicleStatuses.Draft)
            throw new ValidationException(ctx.Messages.Error("status", Msg.VhWrongStatus, ("Status", vehicle.Status), ("Action", "given a different finance agreement")));
    }

    /// <summary>The vehicle that already holds the bank's agreement number, or null.</summary>
    private async Task<string?> AgreementHolderAsync(int bankId, string agreementNo, int exceptAgreementId)
    {
        var held = await ctx.Db.Agreements.AsNoTracking()
            .Where(a => a.TenantId == ctx.Tenant && a.BankId == bankId && a.AgreementNo == agreementNo && a.VehicleFinanceAgreementId != exceptAgreementId)
            .Select(a => a.VehicleId).FirstOrDefaultAsync();
        if (held == 0) return null;
        return await ctx.Db.Vehicles.AsNoTracking().Where(v => v.TenantId == ctx.Tenant && v.VehicleId == held).Select(v => v.VehicleCode).FirstOrDefaultAsync();
    }

    // ── Rules ───────────────────────────────────────────────────────────────────────

    /// <summary>Every check that needs nothing but what was sent and the acquisition date (FSD §18.2).</summary>
    internal List<ValidationError> Validate(SaveFinanceRequest r, DateOnly? acquisitionDate)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(ctx.Messages.Error(field, code, values));

        if (r.FinanceTypeId <= 0) Add("financeTypeId", Msg.Required, ("Field", "Finance type"));
        if (r.BankId <= 0) Add("bankId", Msg.Required, ("Field", "Bank"));

        if (r.AgreementNo.Length == 0) Add("agreementNo", Msg.Required, ("Field", "Agreement number"));
        else if (r.AgreementNo.Length > 60) Add("agreementNo", Msg.MaxLength, ("Field", "Agreement number"), ("Max", 60));

        if (r.AgreementDate is null) Add("agreementDate", Msg.Required, ("Field", "Agreement date"));
        else if (acquisitionDate is null) Add("acquisitionDate", Msg.Required, ("Field", "Acquisition date"));   // saved with the acquisition block first
        else if (r.AgreementDate > acquisitionDate.Value.AddDays(AgreementGraceDays))
            Add("agreementDate", Msg.Max, ("Field", "Agreement date"), ("Max", acquisitionDate.Value.AddDays(AgreementGraceDays).ToString("yyyy-MM-dd")));

        Money("financeAmount", "Finance amount", r.FinanceAmount, positive: true);
        Money("downPayment", "Down payment", r.DownPayment, positive: false);
        if (r.InstallmentAmount is null) Add("installmentAmount", Msg.VhInstallmentRequired);
        else Money("installmentAmount", "Installment amount", r.InstallmentAmount, positive: true);

        if (!FinanceFrequencies.All.Contains(r.Frequency)) Add("frequency", Msg.OneOf, ("Field", "Frequency"), ("Allowed", string.Join(", ", FinanceFrequencies.All)));
        if (r.Tenure is null) Add("tenure", Msg.VhInstallmentRequired);
        else if (r.Tenure < 1) Add("tenure", Msg.Min, ("Field", "Tenure"), ("Min", 1));
        else if (r.Tenure > 120) Add("tenure", Msg.Max, ("Field", "Tenure"), ("Max", 120));

        if (r.FirstDueDate is null) Add("firstDueDate", Msg.Required, ("Field", "First installment due date"));
        else if (r.AgreementDate is { } agreed && r.FirstDueDate < agreed) Add("firstDueDate", Msg.Min, ("Field", "First installment due date"), ("Min", agreed.ToString("yyyy-MM-dd")));

        if (r.MarkupRate is < 0 or > 100) Add("markupRate", Msg.Invalid, ("Field", "Markup rate"));
        if (r.ResidualAmount is { } residual && (residual < 0 || residual >= MaxAmount)) Add("residualAmount", Msg.Invalid, ("Field", "Residual amount"));
        if (r.SecurityDeposit is { } deposit && (deposit < 0 || deposit >= MaxAmount)) Add("securityDeposit", Msg.Invalid, ("Field", "Security deposit"));
        return errors;

        void Money(string field, string label, decimal? value, bool positive)
        {
            if (value is null) Add(field, Msg.Required, ("Field", label));
            else if (value >= MaxAmount || (positive ? value <= 0 : value < 0)) Add(field, Msg.Invalid, ("Field", label));
        }
    }

    private async Task CheckReferencesAsync(SaveFinanceRequest r, VehicleFinanceAgreement? existing, List<ValidationError> errors)
    {
        if (r.FinanceTypeId > 0)
        {
            var type = await lookups.FindAsync(PlatformLookups.FinanceType, r.FinanceTypeId);
            // Fully Paid means there is no agreement, and a value retired from the list stays valid on the agreement that already holds it.
            if (type is null || type.Code == FinanceTypeCodes.FullyPaid || (!type.IsActive && existing?.FinanceTypeId != r.FinanceTypeId))
                errors.Add(ctx.Messages.Error("financeTypeId", Msg.Invalid, ("Field", "Finance type")));
        }
        if (r.BankId > 0)
        {
            var bank = await ctx.Partners.FindAsync(r.BankId);
            if (bank is null || (!bank.IsAvailable && existing?.BankId != r.BankId)) errors.Add(ctx.Messages.Error("bankId", Msg.Invalid, ("Field", "Bank")));
            else if (existing?.BankId != r.BankId && !bank.HasRole(PartnerRoleCodes.Bank))
                errors.Add(ctx.Messages.Error("bankId", Msg.VhCounterpartyLacksRole, ("PartnerName", bank.DisplayName), ("Role", "Bank")));
        }
    }

    /// <summary>BR-VH-009: the down payment is the amount paid at creation, so it is not counted twice. Used here and by activation.</summary>
    internal static List<ValidationError> CrossCheck(VehicleContext ctx, decimal downPayment, decimal? amountPaid)
    {
        var paid = amountPaid ?? 0m;
        return downPayment == paid
            ? []
            : [ctx.Messages.Error("downPayment", Msg.VhDownPaymentMismatch, ("a", downPayment.ToString("N2")), ("b", paid.ToString("N2")))];
    }

    /// <summary>BR-VH-010: finance amount plus down payment should be the purchase price. Returns what it came to when it is not, or null.</summary>
    internal static (string Sum, string Price)? Reconciliation(decimal financeAmount, decimal downPayment, decimal? purchasePrice) =>
        purchasePrice is { } price && financeAmount + downPayment != price ? ((financeAmount + downPayment).ToString("N2"), price.ToString("N2")) : null;
}
