using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Time;

namespace VMS.Modules.Trips.Services;

/// <summary>§40's own "How Finance resolves a negative balance": Carry Forward (L14) or Refund (L15's own
/// credit-invoice half — CC-34's <see cref="IAdvanceService.RefundAsync"/> already covers the un-applied-advance
/// half). Both need a reason and are capped at the source invoice's own available credit, never at the target's
/// own balance (uncapped there, same "the full amount moves, not just what's needed" principle as CC-36's own
/// payment transfer).</summary>
public interface ICustomerCreditService
{
    Task<CarryForwardModel> CarryForwardAsync(long sourceInvoiceId, CarryForwardRequest request, int userId, CancellationToken ct = default);
    Task<CustomerRefundModel> RefundAsync(long invoiceId, RefundCreditRequest request, int userId, CancellationToken ct = default);
}

internal sealed class CustomerCreditService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IOperatingClock clock, ICustomerLedgerPostingService ledger) : ICustomerCreditService
{
    public async Task<CarryForwardModel> CarryForwardAsync(long sourceInvoiceId, CarryForwardRequest request, int userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Reason")));
        if (request.Amount <= 0)
            throw new ValidationException(messages.Error("amount", Msg.Min, ("Field", "Amount"), ("Min", "0.01")));

        return await db.InTransactionAsync(async ct2 =>
        {
            var source = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == sourceInvoiceId, ct2)
                ?? throw new NotFoundException($"Invoice {sourceInvoiceId} was not found.");
            if (source.Status != InvoiceStatuses.Submitted)
                throw new BusinessRuleException("INVOICE_NOT_SUBMITTED", $"Invoice {source.InvoiceNumber} is not Submitted; it has no credit to carry forward.", []);

            var availableCredit = source.BalanceAmount < 0 ? -source.BalanceAmount : 0;
            if (request.Amount > availableCredit)
                throw new BusinessRuleException("AMOUNT_EXCEEDS_CREDIT", "Amount cannot exceed the available credit.", []);

            if (request.TargetInvoiceId == sourceInvoiceId)
                throw new ValidationException(messages.Error("targetInvoiceId", Msg.Invalid, ("Field", "Target invoice (must be a different invoice)")));
            var target = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == request.TargetInvoiceId, ct2)
                ?? throw new NotFoundException($"Invoice {request.TargetInvoiceId} was not found.");
            // §40: "another open Submitted invoice of the same customer."
            if (target.CustomerId != source.CustomerId)
                throw new ValidationException(messages.Error("targetInvoiceId", Msg.Invalid, ("Field", "Target invoice (must belong to the same customer)")));
            if (target.Status != InvoiceStatuses.Submitted || !target.IsActive)
                throw new BusinessRuleException("INVOICE_NOT_SUBMITTED", $"Invoice {target.InvoiceNumber} is not an open Submitted invoice; it cannot receive a carry forward.", []);

            var carryForwardDate = await clock.TodayAsync(tenant.TenantId);
            var carryForward = new InvoiceCreditCarryForward
            {
                SourceInvoiceId = sourceInvoiceId, TargetInvoiceId = request.TargetInvoiceId, Amount = request.Amount,
                CarryForwardDate = carryForwardDate, Reason = request.Reason.Trim(), CreatedBy = userId, CreatedOn = DateTime.UtcNow
            };
            db.InvoiceCreditCarryForwards.Add(carryForward);
            await db.SaveChangesAsync(ct2);

            // §40A: "net zero for the customer" — the source's credit shrinks (balance moves up toward 0), the
            // target's own outstanding balance falls by the same amount.
            source.CarryForwardOutAmount += request.Amount;
            target.CarryForwardInAmount += request.Amount;
            InvoiceBalanceRecalculation.Apply(source);
            InvoiceBalanceRecalculation.Apply(target);

            var (outId, inId) = await ledger.PostCarryForwardAsync(carryForward.InvoiceCreditCarryForwardId, carryForwardDate, userId, ct2);
            carryForward.LedgerEntryOutId = outId;
            carryForward.LedgerEntryInId = inId;
            await db.SaveChangesAsync(ct2);

            return new CarryForwardModel
            {
                InvoiceCreditCarryForwardId = carryForward.InvoiceCreditCarryForwardId, SourceInvoiceId = source.InvoiceId, SourceInvoiceNumber = source.InvoiceNumber,
                SourceInvoiceBalance = source.BalanceAmount, TargetInvoiceId = target.InvoiceId, TargetInvoiceNumber = target.InvoiceNumber,
                TargetInvoiceBalance = target.BalanceAmount, Amount = carryForward.Amount, Reason = carryForward.Reason
            };
        }, ct);
    }

    public async Task<CustomerRefundModel> RefundAsync(long invoiceId, RefundCreditRequest request, int userId, CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (!PaymentMethods.All.Contains(request.PaymentMethod))
            Add("paymentMethod", Msg.OneOf, ("Field", "Payment method"), ("Allowed", string.Join(", ", PaymentMethods.All)));
        if (request.Amount <= 0) Add("amount", Msg.Min, ("Field", "Amount"), ("Min", "0.01"));
        if (string.IsNullOrWhiteSpace(request.Reason)) Add("reason", Msg.Required, ("Field", "Reason"));
        if (errors.Count > 0) throw new ValidationException(errors);

        var bankAccount = await db.BankCashAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.BankCashAccountId == request.BankCashAccountId, ct)
            ?? throw new ValidationException(messages.Error("bankCashAccountId", Msg.Invalid, ("Field", "Bank account")));
        if (!bankAccount.IsActive) throw new ValidationException(messages.Error("bankCashAccountId", Msg.Invalid, ("Field", "Bank account (not Active)")));

        return await db.InTransactionAsync(async ct2 =>
        {
            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == invoiceId, ct2)
                ?? throw new NotFoundException($"Invoice {invoiceId} was not found.");
            if (invoice.Status != InvoiceStatuses.Submitted)
                throw new BusinessRuleException("INVOICE_NOT_SUBMITTED", $"Invoice {invoice.InvoiceNumber} is not Submitted; it has no credit to refund.", []);

            var availableCredit = invoice.BalanceAmount < 0 ? -invoice.BalanceAmount : 0;
            if (request.Amount > availableCredit)
                throw new BusinessRuleException("AMOUNT_EXCEEDS_CREDIT", "Amount cannot exceed the available credit.", []);

            var refundDate = request.RefundDate ?? await clock.TodayAsync(tenant.TenantId);
            var refund = new CustomerRefund
            {
                InvoiceId = invoiceId, Amount = request.Amount, RefundDate = refundDate, PaymentMethod = request.PaymentMethod,
                BankCashAccountId = request.BankCashAccountId, PaymentReference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
                Reason = request.Reason.Trim(), CreatedBy = userId, CreatedOn = DateTime.UtcNow
            };
            db.CustomerRefunds.Add(refund);
            await db.SaveChangesAsync(ct2);

            invoice.RefundedAmount += request.Amount;
            InvoiceBalanceRecalculation.Apply(invoice);

            refund.LedgerEntryId = await ledger.PostInvoiceRefundAsync(refund.CustomerRefundId, refundDate, userId, ct2);
            await db.SaveChangesAsync(ct2);

            return new CustomerRefundModel
            {
                CustomerRefundId = refund.CustomerRefundId, InvoiceId = invoice.InvoiceId, InvoiceNumber = invoice.InvoiceNumber, Amount = refund.Amount,
                RefundDate = refund.RefundDate, PaymentMethod = refund.PaymentMethod, Reference = refund.PaymentReference,
                InvoiceBalance = invoice.BalanceAmount, Reason = refund.Reason
            };
        }, ct);
    }
}
