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

/// <summary>§37 BR-P5, §40A L5, AC-40. Payments are never edited or deleted — a wrong one is reversed (reason
/// mandatory): the original row's own Status becomes Reversed but it "remains visible" (the ledger's own L4
/// credit is untouched; a brand-new L5 debit is posted alongside it, never in its place). A corrected payment is
/// then entered fresh through <see cref="IPaymentReceiptService"/>, not by editing this one.</summary>
public interface IPaymentReversalService
{
    Task<InvoicePaymentReversalModel> ReverseAsync(long invoicePaymentId, ReversePaymentRequest request, int userId, CancellationToken ct = default);
}

internal sealed class PaymentReversalService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IOperatingClock clock, ICustomerLedgerPostingService ledger) : IPaymentReversalService
{
    public async Task<InvoicePaymentReversalModel> ReverseAsync(long invoicePaymentId, ReversePaymentRequest request, int userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Reversal reason")));

        return await db.InTransactionAsync(async ct2 =>
        {
            var payment = await db.InvoicePayments.FirstOrDefaultAsync(p => p.TenantId == tenant.TenantId && p.InvoicePaymentId == invoicePaymentId, ct2)
                ?? throw new NotFoundException($"Invoice payment {invoicePaymentId} was not found.");

            if (payment.Status == InvoicePaymentRowStatuses.Reversed)
                throw new BusinessRuleException("PAYMENT_ALREADY_REVERSED", "This payment has already been reversed.", []);
            // §47.3: "PAYMENT_TRANSFERRED (reverse on the active invoice instead)" — unreachable today (no
            // caller ever sets Transferred; that is CC-36's own job) but checked now anyway, the same
            // "guard real, currently vacuous" pattern this register keeps using for a not-yet-built dependency.
            if (payment.Status == InvoicePaymentRowStatuses.Transferred)
                throw new BusinessRuleException("PAYMENT_TRANSFERRED", "This payment was transferred to a regenerated invoice. Reverse it on the active invoice instead.", []);

            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == payment.InvoiceId, ct2)
                ?? throw new NotFoundException($"Invoice {payment.InvoiceId} was not found.");

            var reversalDate = request.ReversalDate ?? await clock.TodayAsync(tenant.TenantId);
            payment.Status = InvoicePaymentRowStatuses.Reversed;
            payment.ReversalReason = request.Reason.Trim();
            payment.ReversedBy = userId;
            payment.ReversedOn = DateTime.UtcNow;

            // BR-P6-equivalent for reversal: recomputed in the same transaction, same shared formula as the
            // original payment (CC-31) so the two can never drift apart on what "balance" means.
            invoice.PaidAmount -= payment.Amount;
            InvoiceBalanceRecalculation.Apply(invoice);

            await db.SaveChangesAsync(ct2);

            // §40A L5: a NEW debit entry, dated the reversal date — "the original PAYMENT credit remains visible."
            var reversalLedgerEntryId = await ledger.PostPaymentReversalAsync(invoicePaymentId, reversalDate, userId, ct2);

            return new InvoicePaymentReversalModel
            {
                InvoicePaymentId = payment.InvoicePaymentId, InvoiceId = invoice.InvoiceId, InvoiceNumber = invoice.InvoiceNumber, Amount = payment.Amount,
                Status = payment.Status, ReversalLedgerEntryId = reversalLedgerEntryId, InvoiceBalance = invoice.BalanceAmount, InvoicePaymentStatus = invoice.PaymentStatus
            };
        }, ct);
    }
}
