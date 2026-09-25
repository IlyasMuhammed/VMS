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

/// <summary>§37.5, §40A L6-L7, AC-61: settling part of a Submitted invoice as a Write-off or a Discount, either
/// standalone (Invoice Detail) or as "Settle remaining balance" on Record Payment — <see cref="PaymentReceiptService"/>
/// calls <see cref="CreateAsync"/> directly for that case, from inside its own already-open transaction (which
/// <c>InTransactionAsync</c> simply joins, per its own "if already inside one, joins it" rule), never a second one.</summary>
public interface IInvoiceSettlementService
{
    Task<InvoiceSettlementModel> CreateAsync(long invoiceId, CreateSettlementRequest request, int userId, CancellationToken ct = default);
    Task<InvoiceSettlementModel> ReverseAsync(long invoiceSettlementId, ReverseSettlementRequest request, int userId, CancellationToken ct = default);
}

internal sealed class InvoiceSettlementService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IOperatingClock clock,
    ISettlementNumberAllocator numbers, ICustomerLedgerPostingService ledger) : IInvoiceSettlementService
{
    public async Task<InvoiceSettlementModel> CreateAsync(long invoiceId, CreateSettlementRequest request, int userId, CancellationToken ct = default)
    {
        if (!SettlementTypes.All.Contains(request.SettlementType))
            throw new ValidationException(messages.Error("settlementType", Msg.OneOf, ("Field", "Settlement type"), ("Allowed", string.Join(", ", SettlementTypes.All))));
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Reason")));
        if (request.Amount <= 0)
            throw new ValidationException(messages.Error("amount", Msg.Min, ("Field", "Amount"), ("Min", "0.01")));

        return await db.InTransactionAsync(async ct2 =>
        {
            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == invoiceId, ct2)
                ?? throw new NotFoundException($"Invoice {invoiceId} was not found.");
            // §37.5: "InvoiceId ... Active, Submitted."
            if (invoice.Status != InvoiceStatuses.Submitted)
                throw new BusinessRuleException("INVOICE_NOT_SUBMITTED", $"Invoice {invoice.InvoiceNumber} is not Submitted; it cannot be settled.", []);
            // §37.5: "Amount > 0 and ≤ invoice balance at that moment" — screen 24's own error text.
            if (request.Amount > invoice.BalanceAmount)
                throw new BusinessRuleException("AMOUNT_EXCEEDS_BALANCE", "Amount cannot exceed the invoice balance.", []);

            var settlementDate = request.SettlementDate ?? await clock.TodayAsync(tenant.TenantId);
            var settlement = new InvoiceSettlement
            {
                InvoiceId = invoiceId, SettlementNumber = await numbers.NextAsync(ct2), SettlementType = request.SettlementType, Amount = request.Amount,
                SettlementDate = settlementDate, Reason = request.Reason.Trim(), Status = InvoiceSettlementStatuses.Posted, CreatedBy = userId, CreatedOn = DateTime.UtcNow
            };
            db.InvoiceSettlements.Add(settlement);
            await db.SaveChangesAsync(ct2);   // the id is needed by the ledger posting below

            if (settlement.SettlementType == SettlementTypes.WriteOff) invoice.WriteOffAmount += settlement.Amount;
            else invoice.DiscountAmount += settlement.Amount;
            InvoiceBalanceRecalculation.Apply(invoice);

            // §40A L6: one WRITE_OFF or DISCOUNT credit — "the invoice becomes Paid when the balance reaches zero."
            settlement.LedgerEntryId = await ledger.PostSettlementAsync(settlement.InvoiceSettlementId, settlementDate, userId, ct2);
            await db.SaveChangesAsync(ct2);

            return ToModel(settlement, invoice);
        }, ct);
    }

    public async Task<InvoiceSettlementModel> ReverseAsync(long invoiceSettlementId, ReverseSettlementRequest request, int userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Reversal reason")));

        return await db.InTransactionAsync(async ct2 =>
        {
            var settlement = await db.InvoiceSettlements.FirstOrDefaultAsync(s => s.TenantId == tenant.TenantId && s.InvoiceSettlementId == invoiceSettlementId, ct2)
                ?? throw new NotFoundException($"Invoice settlement {invoiceSettlementId} was not found.");
            if (settlement.Status == InvoiceSettlementStatuses.Reversed)
                throw new BusinessRuleException("SETTLEMENT_ALREADY_REVERSED", "This settlement has already been reversed.", []);

            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == settlement.InvoiceId, ct2)
                ?? throw new NotFoundException($"Invoice {settlement.InvoiceId} was not found.");

            var reversalDate = request.ReversalDate ?? await clock.TodayAsync(tenant.TenantId);
            settlement.Status = InvoiceSettlementStatuses.Reversed;
            settlement.ReversalReason = request.Reason.Trim();
            settlement.ReversedBy = userId;
            settlement.ReversedOn = DateTime.UtcNow;

            if (settlement.SettlementType == SettlementTypes.WriteOff) invoice.WriteOffAmount -= settlement.Amount;
            else invoice.DiscountAmount -= settlement.Amount;
            InvoiceBalanceRecalculation.Apply(invoice);

            await db.SaveChangesAsync(ct2);

            // §40A L7: a new SETTLEMENT_REVERSAL debit — the original L6 credit stays exactly as posted.
            var ledgerEntryId = await ledger.PostSettlementReversalAsync(invoiceSettlementId, reversalDate, userId, ct2);

            return ToModel(settlement, invoice, ledgerEntryId);
        }, ct);
    }

    private static InvoiceSettlementModel ToModel(InvoiceSettlement s, Invoice invoice, long? reversalLedgerEntryId = null) => new()
    {
        InvoiceSettlementId = s.InvoiceSettlementId, InvoiceId = s.InvoiceId, InvoiceNumber = invoice.InvoiceNumber, SettlementNumber = s.SettlementNumber,
        SettlementType = s.SettlementType, Amount = s.Amount, SettlementDate = s.SettlementDate, Reason = s.Reason, Status = s.Status,
        LedgerEntryId = reversalLedgerEntryId ?? s.LedgerEntryId, InvoiceBalance = invoice.BalanceAmount, InvoicePaymentStatus = invoice.PaymentStatus
    };
}
