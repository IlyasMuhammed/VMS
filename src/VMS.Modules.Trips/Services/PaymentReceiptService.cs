using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;

namespace VMS.Modules.Trips.Services;

/// <summary>§37: payment receipt entry — one <see cref="CustomerReceipt"/> (the money received) allocated to one
/// or more Submitted invoices through <see cref="InvoicePayment"/> rows, each posting an L4 ledger credit
/// (§40A.1). Reversal (BR-P5) is CC-32's own job — every row this service writes starts, and stays, Posted.</summary>
public interface IPaymentReceiptService
{
    Task<CustomerReceiptModel> CreateAsync(CreatePaymentReceiptRequest request, int userId, CancellationToken ct = default);
}

internal sealed class PaymentReceiptService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages,
    IReceiptNumberAllocator numbers, ICustomerLedgerPostingService ledger, IInvoiceSettlementService settlements) : IPaymentReceiptService
{
    public async Task<CustomerReceiptModel> CreateAsync(CreatePaymentReceiptRequest request, int userId, CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (!PaymentMethods.All.Contains(request.PaymentMethod))
            // AC-63: "the method list shows only Direct to Account and Bank Cheque."
            Add("paymentMethod", Msg.OneOf, ("Field", "Payment method"), ("Allowed", string.Join(", ", PaymentMethods.All)));
        if (string.IsNullOrWhiteSpace(request.InstrumentNo)) Add("instrumentNo", Msg.Required, ("Field", "Instrument number"));
        if (request.PaymentMethod == PaymentMethods.BankCheque)
        {
            if (request.InstrumentDate is null) Add("instrumentDate", Msg.Required, ("Field", "Cheque date"));
            if (string.IsNullOrWhiteSpace(request.DrawnOnBank)) Add("drawnOnBank", Msg.Required, ("Field", "Drawn on bank"));
        }
        if (request.ReceiptAmount <= 0) Add("receiptAmount", Msg.Min, ("Field", "Receipt amount"), ("Min", "0.01"));
        if (request.Allocations.Count == 0)
            Add("allocations", Msg.Required, ("Field", "At least one invoice allocation"));
        foreach (var allocation in request.Allocations)
            if (allocation.Amount <= 0) Add("allocations", Msg.Min, ("Field", "Allocation amount"), ("Min", "0.01"));
        // BR-P3: "Sum of allocations = ReceiptAmount."
        if (errors.Count == 0 && request.Allocations.Sum(a => a.Amount) != request.ReceiptAmount)
            Add("allocations", Msg.Invalid, ("Field", "Allocations (must sum to the receipt amount)"));
        // §37.5: "Settle remaining balance" only exists on Record Payment's own single-invoice screen — there is
        // no single "the invoice" to settle once a receipt spans more than one.
        if (request.SettleRemaining is not null && request.Allocations.Count != 1)
            Add("settleRemaining", Msg.Invalid, ("Field", "Settle remaining balance (only valid with exactly one allocation)"));
        if (errors.Count > 0) throw new ValidationException(errors);

        var bankAccount = await db.BankCashAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.BankCashAccountId == request.BankCashAccountId, ct)
            ?? throw new ValidationException(messages.Error("bankCashAccountId", Msg.Invalid, ("Field", "Bank account")));
        if (!bankAccount.IsActive) throw new ValidationException(messages.Error("bankCashAccountId", Msg.Invalid, ("Field", "Bank account (not Active)")));

        // BR-P4: same customer + InstrumentNo + Amount + method already exists → a confirmable warning, the
        // same "soft block, explicit confirm to proceed" shape as CC-06's TAX_RULE_REPLACEMENT_CONFIRMATION and
        // CC-25's OVERLAP_DETECTED — §47.3 calls this one 409, but every other confirmable soft block in this
        // module is already a 422 BusinessRuleException; kept consistent with that, not the literal status code.
        if (!request.ConfirmDuplicate)
        {
            var duplicate = await db.CustomerReceipts.AnyAsync(r => r.TenantId == tenant.TenantId && r.CustomerId == request.CustomerId
                && r.InstrumentNo == request.InstrumentNo && r.ReceiptAmount == request.ReceiptAmount && r.PaymentMethod == request.PaymentMethod
                && r.Status == CustomerReceiptStatuses.Posted, ct);
            if (duplicate)
                throw new BusinessRuleException("DUPLICATE_INSTRUMENT", $"A receipt with the same instrument number already exists ({request.InstrumentNo}).", []);
        }

        return await db.InTransactionAsync(async ct2 =>
        {
            var invoiceIds = request.Allocations.Select(a => a.InvoiceId).Distinct().ToList();
            var invoices = await db.Invoices.Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, ct2);
            if (invoices.Count != invoiceIds.Count)
                throw new ValidationException(messages.Error("allocations", Msg.Invalid, ("Field", "One or more invoices were not found")));

            foreach (var allocation in request.Allocations)
            {
                var invoice = invoices[allocation.InvoiceId];
                if (invoice.CustomerId != request.CustomerId)
                    throw new ValidationException(messages.Error("allocations", Msg.Invalid, ("Field", "One or more invoices belong to a different customer")));

                // §36/§47.3: an Inactive invoice (superseded by regeneration — CC-35's own job) points at its
                // replacement; checked ahead of the generic status check since it is the more specific case.
                if (invoice.Status == InvoiceStatuses.Inactive)
                {
                    var replacement = invoice.ReplacedByInvoiceId is { } replacedId
                        ? await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.InvoiceId == replacedId).Select(i => i.InvoiceNumber).FirstOrDefaultAsync(ct2)
                        : null;
                    throw new BusinessRuleException("INVOICE_INACTIVE",
                        replacement is null ? $"This invoice has been replaced. Record the payment against the active invoice." : $"This invoice has been replaced by {replacement}. Record the payment against the active invoice.", []);
                }
                // BR-P1: "Invoice payments are allowed only on Active, Submitted invoices."
                if (invoice.Status != InvoiceStatuses.Submitted)
                    throw new BusinessRuleException("INVOICE_NOT_SUBMITTED", $"Invoice {invoice.InvoiceNumber} is not Submitted; payments cannot be recorded against it yet.", []);

                // BR-P2: overpayment is allowed (Confirmed), but only after this confirmation.
                if (allocation.Amount > invoice.BalanceAmount && !request.ConfirmOverpayment)
                    throw new BusinessRuleException("OVERPAYMENT_CONFIRMATION_REQUIRED",
                        $"Payment exceeds the outstanding balance by {allocation.Amount - invoice.BalanceAmount:0.00}. The invoice will show a credit.", []);
            }

            var receipt = new CustomerReceipt
            {
                ReceiptNumber = await numbers.NextAsync(ct2), CustomerId = request.CustomerId, ReceiptType = ReceiptTypes.InvoicePayment,
                ReceiptDate = request.ReceiptDate, ReceiptAmount = request.ReceiptAmount, CurrencyCode = bankAccount.CurrencyCode,
                PaymentMethod = request.PaymentMethod, BankCashAccountId = request.BankCashAccountId, InstrumentNo = request.InstrumentNo.Trim(),
                InstrumentDate = request.InstrumentDate, DrawnOnBank = request.DrawnOnBank?.Trim(), PaymentReference = request.PaymentReference?.Trim(),
                AttachmentDocumentId = request.AttachmentDocumentId, Status = CustomerReceiptStatuses.Posted,
                Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(), CreatedBy = userId, CreatedOn = DateTime.UtcNow
            };
            db.CustomerReceipts.Add(receipt);
            await db.SaveChangesAsync(ct2);   // the id is needed for every allocation row

            var allocationModels = new List<InvoicePaymentAllocationModel>();
            foreach (var allocation in request.Allocations)
            {
                var invoice = invoices[allocation.InvoiceId];
                var payment = new InvoicePayment
                {
                    CustomerReceiptId = receipt.CustomerReceiptId, InvoiceId = invoice.InvoiceId, PaymentDate = request.ReceiptDate, Amount = allocation.Amount,
                    PaymentMethod = request.PaymentMethod, PaymentReference = request.PaymentReference?.Trim(), BankCashAccountId = request.BankCashAccountId,
                    Status = InvoicePaymentRowStatuses.Posted, CreatedBy = userId, CreatedOn = DateTime.UtcNow
                };
                db.InvoicePayments.Add(payment);
                await db.SaveChangesAsync(ct2);   // the id is needed by the ledger posting below

                // §40A LR-2: "Each payment allocation posts exactly one credit against its invoice."
                var ledgerEntryId = await ledger.PostPaymentAsync(payment.InvoicePaymentId, request.ReceiptDate, userId, ct2);
                payment.LedgerEntryId = ledgerEntryId;

                // §36: recomputed in the same transaction as the payment (BR-P6). No client-supplied rowVersion
                // exists in §47.3's own request shape for this action — the invoice's own tracked RowVersion
                // (captured when it was read a moment ago, above) is EF's implicit optimistic-concurrency check
                // instead, the same effective "recompute under UPDLOCK" guarantee via a different mechanism.
                invoice.PaidAmount += allocation.Amount;
                InvoiceBalanceRecalculation.Apply(invoice);

                allocationModels.Add(new InvoicePaymentAllocationModel
                {
                    InvoicePaymentId = payment.InvoicePaymentId, InvoiceId = invoice.InvoiceId, InvoiceNumber = invoice.InvoiceNumber, Amount = payment.Amount,
                    LedgerEntryId = ledgerEntryId, InvoiceBalance = invoice.BalanceAmount, InvoicePaymentStatus = invoice.PaymentStatus
                });
            }

            try { await db.SaveChangesAsync(ct2); }
            catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException("Another payment against one of these invoices was just recorded. Please retry."); }

            // §37.5: "pre-filled with the remaining amount (editable)" — Amount defaults to whatever the
            // invoice's own balance is now, after the payment just applied above. Calling the settlement
            // service's own public CreateAsync here joins THIS transaction (InTransactionAsync's own "if
            // already inside one" rule) rather than opening a second one.
            InvoiceSettlementModel? settlementModel = null;
            if (request.SettleRemaining is { } settleRemaining)
            {
                var invoiceId = request.Allocations[0].InvoiceId;
                settlementModel = await settlements.CreateAsync(invoiceId, new CreateSettlementRequest
                {
                    SettlementType = settleRemaining.SettlementType, Amount = settleRemaining.Amount ?? invoices[invoiceId].BalanceAmount,
                    SettlementDate = settleRemaining.SettlementDate ?? request.ReceiptDate, Reason = settleRemaining.Reason
                }, userId, ct2);
                allocationModels[0].InvoiceBalance = settlementModel.InvoiceBalance;
                allocationModels[0].InvoicePaymentStatus = settlementModel.InvoicePaymentStatus;
            }

            return new CustomerReceiptModel
            {
                CustomerReceiptId = receipt.CustomerReceiptId, ReceiptNumber = receipt.ReceiptNumber, CustomerId = receipt.CustomerId, ReceiptDate = receipt.ReceiptDate,
                ReceiptAmount = receipt.ReceiptAmount, CurrencyCode = receipt.CurrencyCode, PaymentMethod = receipt.PaymentMethod, BankCashAccountId = receipt.BankCashAccountId,
                InstrumentNo = receipt.InstrumentNo, Status = receipt.Status, Remarks = receipt.Remarks, RowVersion = Convert.ToBase64String(receipt.RowVersion),
                Allocations = allocationModels, Settlement = settlementModel
            };
        }, ct);
    }
}
