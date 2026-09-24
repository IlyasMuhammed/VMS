using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;

namespace VMS.Modules.Vehicles.Services;

public interface IInstallmentService
{
    /// <summary>The installments of the vehicle's agreements, in due order, settled ones included so the payment history stays. Empty while the only agreement is a Draft: the schedule is generated at activation.</summary>
    Task<List<InstallmentModel>> ListAsync(int vehicleId);
    Task<InstallmentPaymentModel> PayAsync(int vehicleId, int installmentId, PayInstallmentRequest request, VehicleCaller caller);
}

/// <summary>
/// Recording an installment payment: the schedule is updated and one ledger entry is posted, together (source §8, §19). Paid to date
/// is the sum of the ledger, never a stored figure, so what the schedule says and what the ledger says must always agree.
/// </summary>
internal sealed class InstallmentService(VehicleContext ctx) : IInstallmentService
{
    public async Task<List<InstallmentModel>> ListAsync(int vehicleId)
    {
        await ctx.LoadReadOnlyAsync(vehicleId);
        var rows = await ctx.Db.Installments.AsNoTracking()
            .Where(i => i.TenantId == ctx.Tenant && i.VehicleId == vehicleId
                && ctx.Db.Agreements.Any(a => a.VehicleFinanceAgreementId == i.VehicleFinanceAgreementId && a.Status != AgreementStatuses.Draft))
            .OrderBy(i => i.DueDate).ThenBy(i => i.InstallmentNo).ToListAsync();
        return rows.Select(ToModel).ToList();
    }

    private static InstallmentModel ToModel(VehicleInstallment i) => new()
    {
        Id = i.VehicleInstallmentId, AgreementId = i.VehicleFinanceAgreementId, InstallmentNo = i.InstallmentNo, DueDate = i.DueDate, ExpectedAmount = i.ExpectedAmount,
        PaidAmount = i.PaidAmount, RemainingAmount = i.ExpectedAmount - i.PaidAmount, PaidOn = i.PaidOn, IsResidual = i.IsResidual, Status = i.Status,
        RowVersion = Convert.ToBase64String(i.RowVersion)
    };

    public async Task<InstallmentPaymentModel> PayAsync(int vehicleId, int installmentId, PayInstallmentRequest request, VehicleCaller caller)
    {
        // What is posted is a cost, and the schedule is finance: the caller needs to be able to see both.
        if (!caller.Has(PermissionCodes.VEH_FIELD_FINANCE_VIEW) || !caller.Has(PermissionCodes.VEH_FIELD_COST_VIEW))
            throw new ForbiddenException("You do not have permission to record vehicle payments.");

        var vehicle = await ctx.LoadAsync(vehicleId);
        var installment = await ctx.Db.Installments.FirstOrDefaultAsync(i => i.TenantId == ctx.Tenant && i.VehicleId == vehicleId && i.VehicleInstallmentId == installmentId)
            ?? throw new NotFoundException("Installment not found.");
        var agreement = await ctx.Db.Agreements.FirstAsync(a => a.TenantId == ctx.Tenant && a.VehicleFinanceAgreementId == installment.VehicleFinanceAgreementId);

        // A settled or closed agreement takes no more payments, and a vehicle that is gone from the books takes none either.
        if (agreement.Status != AgreementStatuses.Active)
            throw new ValidationException(ctx.Messages.Error("status", Msg.VhWrongStatus, ("Status", agreement.Status), ("Action", "given a payment")));

        var today = await ctx.TodayAsync();
        var paidOn = request.PaidOn ?? today;
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(ctx.Messages.Error(field, code, values));

        var remaining = installment.ExpectedAmount - installment.PaidAmount;
        if (request.Amount is null) Add("amount", Msg.Required, ("Field", "Amount"));
        else if (request.Amount <= 0) Add("amount", Msg.Min, ("Field", "Amount"), ("Min", "0.01"));
        else if (request.Amount > remaining) Add("amount", Msg.Max, ("Field", "Amount"), ("Max", remaining.ToString("N2")));

        if (paidOn > today) Add("paidOn", Msg.NotFuture, ("Field", "Payment date"));
        else if (vehicle.AcquisitionDate is { } acquired && paidOn < acquired) Add("paidOn", Msg.Min, ("Field", "Payment date"), ("Min", acquired.ToString("yyyy-MM-dd")));   // no entry predates the acquisition

        var mode = string.IsNullOrWhiteSpace(request.PaymentMode) ? null : request.PaymentMode.Trim();
        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
        if (mode is not null && !PaymentModes.All.Contains(mode)) Add("paymentMode", Msg.OneOf, ("Field", "Payment mode"), ("Allowed", string.Join(", ", PaymentModes.All)));
        if (reference is { Length: > 60 }) Add("reference", Msg.MaxLength, ("Field", "Reference"), ("Max", 60));
        if (string.IsNullOrWhiteSpace(request.RowVersion)) Add("rowVersion", Msg.Required, ("Field", "Row version"));
        if (errors.Count > 0) throw new ValidationException(errors);

        try { ctx.Db.Entry(installment).Property(i => i.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(ctx.Messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        var amount = request.Amount!.Value;
        var entry = new VehicleTransaction
        {
            VehicleId = vehicleId, Type = TransactionTypes.Installment, SubType = installment.IsResidual ? "Residual" : null, Amount = amount, TransactionDate = paidOn,
            PartnerId = agreement.BankId, Reference = Describe(installment, agreement, mode, reference), Source = TransactionSources.Manual, IsSystemGenerated = false, InstallmentId = installment.VehicleInstallmentId,
            CreatedBy = caller.UserId, CreatedOn = DateTime.UtcNow
        };

        bool settled;
        try
        {
            settled = await ctx.Db.InTransactionAsync(async ct =>
            {
                installment.PaidAmount += amount;
                installment.PaidOn = paidOn;
                installment.Status = installment.PaidAmount >= installment.ExpectedAmount ? InstallmentStatuses.Paid : InstallmentStatuses.PartiallyPaid;
                ctx.Db.Transactions.Add(entry);
                await ctx.Db.SaveChangesAsync(ct);

                // The last thing due has been paid: the agreement is settled (its category lock, BR-VH-007, ends with it).
                var stillDue = await ctx.Db.Installments.AnyAsync(i => i.TenantId == ctx.Tenant && i.VehicleFinanceAgreementId == agreement.VehicleFinanceAgreementId
                    && i.Status != InstallmentStatuses.Paid, ct);
                if (stillDue) return false;
                agreement.Status = AgreementStatuses.Settled;
                agreement.ModifiedBy = caller.UserId;
                agreement.ModifiedOn = DateTime.UtcNow;
                await ctx.Db.SaveChangesAsync(ct);
                return true;
            });
        }
        catch (DbUpdateConcurrencyException) { throw await ctx.StaleAsync(vehicleId); }

        var fresh = await ctx.Db.Installments.AsNoTracking().FirstAsync(i => i.VehicleInstallmentId == installmentId);
        return new InstallmentPaymentModel { Installment = ToModel(fresh), TransactionId = entry.VehicleTransactionId, AgreementSettled = settled };
    }

    /// <summary>What the ledger shows for the payment. The column holds 120 characters; an agreement number and a reference can be 60 each.</summary>
    private static string Describe(VehicleInstallment i, VehicleFinanceAgreement a, string? mode, string? reference)
    {
        var text = string.Join(" ", new[] { i.IsResidual ? "Residual payment" : $"Installment {i.InstallmentNo}", "—", a.AgreementNo, mode, reference }.Where(s => !string.IsNullOrEmpty(s)));
        return text.Length <= 120 ? text : text[..120];
    }
}
