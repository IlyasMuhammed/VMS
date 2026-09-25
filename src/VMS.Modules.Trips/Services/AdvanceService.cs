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

/// <summary>§37.4, §40A L8-L10/L15, AC-59/AC-60: advance payments on Open trips only. Backed by a
/// <see cref="CustomerReceipt"/> with <see cref="ReceiptTypes.Advance"/>, the same method/account/instrument
/// shape a payment (CC-31) already uses — money is money, whichever screen it was entered from.</summary>
public interface IAdvanceService
{
    Task<CustomerAdvanceModel> CreateAsync(long tripId, CreateAdvanceRequest request, int userId, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerAdvanceModel>> ListAsync(int? customerId, CancellationToken ct = default);
    Task<CustomerAdvanceModel> MoveAsync(long customerAdvanceId, MoveAdvanceRequest request, int userId, CancellationToken ct = default);
    Task<CustomerAdvanceModel> RefundAsync(long customerAdvanceId, RefundAdvanceRequest request, int userId, CancellationToken ct = default);
    Task<CustomerAdvanceModel> ReverseAsync(long customerAdvanceId, ReverseAdvanceRequest request, int userId, CancellationToken ct = default);

    /// <summary>§37.4 step 3, called once per trip line by <see cref="InvoiceSubmissionService.SubmitAsync"/> —
    /// applies every still-Open advance of that trip to the invoice being submitted, uncapped. A no-op when the
    /// trip has no Open advances (the common case), which is also what makes a retried Submit safely idempotent
    /// here: a trip's advances are only ever Open once.</summary>
    Task ApplyForTripAsync(long tripId, long invoiceId, DateOnly entryDate, int userId, CancellationToken ct = default);
}

internal sealed class AdvanceService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IOperatingClock clock,
    IAdvanceNumberAllocator numbers, IReceiptNumberAllocator receiptNumbers, ICustomerLedgerPostingService ledger) : IAdvanceService
{
    public async Task<CustomerAdvanceModel> CreateAsync(long tripId, CreateAdvanceRequest request, int userId, CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (!PaymentMethods.All.Contains(request.PaymentMethod))
            Add("paymentMethod", Msg.OneOf, ("Field", "Payment method"), ("Allowed", string.Join(", ", PaymentMethods.All)));
        if (string.IsNullOrWhiteSpace(request.InstrumentNo)) Add("instrumentNo", Msg.Required, ("Field", "Instrument number"));
        if (request.PaymentMethod == PaymentMethods.BankCheque)
        {
            if (request.InstrumentDate is null) Add("instrumentDate", Msg.Required, ("Field", "Cheque date"));
            if (string.IsNullOrWhiteSpace(request.DrawnOnBank)) Add("drawnOnBank", Msg.Required, ("Field", "Drawn on bank"));
        }
        if (request.Amount <= 0) Add("amount", Msg.Min, ("Field", "Amount"), ("Min", "0.01"));
        if (errors.Count > 0) throw new ValidationException(errors);

        var bankAccount = await db.BankCashAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.BankCashAccountId == request.BankCashAccountId, ct)
            ?? throw new ValidationException(messages.Error("bankCashAccountId", Msg.Invalid, ("Field", "Bank account")));
        if (!bankAccount.IsActive) throw new ValidationException(messages.Error("bankCashAccountId", Msg.Invalid, ("Field", "Bank account (not Active)")));

        return await db.InTransactionAsync(async ct2 =>
        {
            var trip = await db.Trips.FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct2)
                ?? throw new NotFoundException($"Trip {tripId} was not found.");

            // AC-60: "Advances can only be recorded against Open trips" — the literal rejection message.
            if (trip.TripType != TripTypes.Open)
                throw new BusinessRuleException("ADVANCE_ONLY_ON_OPEN_TRIPS", "Advances can only be recorded against Open trips.", []);
            if (trip.Status == TripStatuses.Cancelled)
                throw new BusinessRuleException("TRIP_CANCELLED", "This trip is cancelled; advances cannot be recorded against it.", []);
            // §37.4's own field rule: "not yet on a Submitted invoice" — Draft/Generated is still fine (the
            // advance will simply be applied automatically once that invoice is eventually Submitted).
            if (trip.InvoiceId is { } linkedInvoiceId)
            {
                var linkedStatus = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.InvoiceId == linkedInvoiceId)
                    .Select(i => i.Status).FirstOrDefaultAsync(ct2);
                if (linkedStatus == InvoiceStatuses.Submitted)
                    throw new BusinessRuleException("TRIP_ALREADY_INVOICED", "This trip is already on a Submitted invoice.", []);
            }

            var receipt = new CustomerReceipt
            {
                ReceiptNumber = await receiptNumbers.NextAsync(ct2), ReceiptType = ReceiptTypes.Advance, CustomerId = trip.CustomerId,
                ReceiptDate = request.AdvanceDate, ReceiptAmount = request.Amount, CurrencyCode = bankAccount.CurrencyCode, PaymentMethod = request.PaymentMethod,
                BankCashAccountId = request.BankCashAccountId, InstrumentNo = request.InstrumentNo.Trim(), InstrumentDate = request.InstrumentDate,
                DrawnOnBank = request.DrawnOnBank?.Trim(), PaymentReference = request.PaymentReference?.Trim(), Status = CustomerReceiptStatuses.Posted,
                Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(), CreatedBy = userId, CreatedOn = DateTime.UtcNow
            };
            db.CustomerReceipts.Add(receipt);
            await db.SaveChangesAsync(ct2);

            var advance = new CustomerAdvance
            {
                AdvanceNumber = await numbers.NextAsync(ct2), CustomerReceiptId = receipt.CustomerReceiptId, CustomerId = trip.CustomerId, TripId = tripId,
                AdvanceDate = request.AdvanceDate, Amount = request.Amount, Status = CustomerAdvanceStatuses.Open, CreatedBy = userId, CreatedOn = DateTime.UtcNow
            };
            db.CustomerAdvances.Add(advance);
            await db.SaveChangesAsync(ct2);

            // §40A L8: one ADVANCE credit, linked to the customer and trip — no invoice yet.
            advance.LedgerEntryId = await ledger.PostAdvanceAsync(advance.CustomerAdvanceId, request.AdvanceDate, userId, ct2);
            await db.SaveChangesAsync(ct2);

            return ToModel(advance, trip.TripNumber);
        }, ct);
    }

    public async Task<IReadOnlyList<CustomerAdvanceModel>> ListAsync(int? customerId, CancellationToken ct = default)
    {
        var query = db.CustomerAdvances.AsNoTracking().Where(a => a.TenantId == tenant.TenantId);
        if (customerId is { } id) query = query.Where(a => a.CustomerId == id);
        var advances = await query.OrderByDescending(a => a.CreatedOn).ToListAsync(ct);
        var tripIds = advances.Select(a => a.TripId).Distinct().ToList();
        var tripNumbers = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && tripIds.Contains(t.TripId))
            .ToDictionaryAsync(t => t.TripId, t => t.TripNumber, ct);
        return advances.Select(a => ToModel(a, tripNumbers.GetValueOrDefault(a.TripId, string.Empty))).ToList();
    }

    public async Task<CustomerAdvanceModel> MoveAsync(long customerAdvanceId, MoveAdvanceRequest request, int userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Move reason")));

        return await db.InTransactionAsync(async ct2 =>
        {
            var advance = await db.CustomerAdvances.FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.CustomerAdvanceId == customerAdvanceId, ct2)
                ?? throw new NotFoundException($"Customer advance {customerAdvanceId} was not found.");
            if (advance.Status != CustomerAdvanceStatuses.Open)
                throw new BusinessRuleException("ADVANCE_NOT_OPEN", $"Only an Open advance can be moved (current status: {advance.Status}).", []);

            var toTrip = await db.Trips.FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == request.ToTripId, ct2)
                ?? throw new NotFoundException($"Trip {request.ToTripId} was not found.");
            if (toTrip.TripType != TripTypes.Open)
                throw new BusinessRuleException("ADVANCE_ONLY_ON_OPEN_TRIPS", "Advances can only be recorded against Open trips.", []);
            if (toTrip.CustomerId != advance.CustomerId)
                throw new ValidationException(messages.Error("toTripId", Msg.Invalid, ("Field", "Trip (must belong to the same customer)")));
            if (toTrip.Status == TripStatuses.Cancelled)
                throw new BusinessRuleException("TRIP_CANCELLED", "This trip is cancelled; advances cannot be moved to it.", []);

            // §40A's own ledger rows are append-only — the ORIGINAL L8 entry keeps naming the trip it was
            // actually posted against; only this row's own current TripId changes, which is what every later
            // read (including the next Submit's own auto-apply) actually consults.
            advance.MovedFromTripId = advance.TripId;
            advance.TripId = request.ToTripId;
            advance.MoveReason = request.Reason.Trim();
            advance.MovedBy = userId;
            advance.MovedOn = DateTime.UtcNow;
            await db.SaveChangesAsync(ct2);

            return ToModel(advance, toTrip.TripNumber);
        }, ct);
    }

    public async Task<CustomerAdvanceModel> RefundAsync(long customerAdvanceId, RefundAdvanceRequest request, int userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Refund reason")));

        return await db.InTransactionAsync(async ct2 =>
        {
            var advance = await db.CustomerAdvances.FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.CustomerAdvanceId == customerAdvanceId, ct2)
                ?? throw new NotFoundException($"Customer advance {customerAdvanceId} was not found.");
            if (advance.Status != CustomerAdvanceStatuses.Open)
                throw new BusinessRuleException("ADVANCE_NOT_OPEN", $"Only an Open (un-applied) advance can be refunded (current status: {advance.Status}).", []);

            var refundDate = request.RefundDate ?? await clock.TodayAsync(tenant.TenantId);
            advance.Status = CustomerAdvanceStatuses.Refunded;
            advance.RefundedAmount = advance.Amount;
            advance.RefundReason = request.Reason.Trim();
            advance.RefundedBy = userId;
            advance.RefundedOn = DateTime.UtcNow;
            await db.SaveChangesAsync(ct2);

            // §40A L15: a REFUND debit against the advance.
            advance.RefundLedgerEntryId = await ledger.PostAdvanceRefundAsync(customerAdvanceId, refundDate, userId, ct2);
            await db.SaveChangesAsync(ct2);

            var trip = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && t.TripId == advance.TripId).Select(t => t.TripNumber).FirstOrDefaultAsync(ct2);
            return ToModel(advance, trip ?? string.Empty);
        }, ct);
    }

    public async Task<CustomerAdvanceModel> ReverseAsync(long customerAdvanceId, ReverseAdvanceRequest request, int userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Reversal reason")));

        return await db.InTransactionAsync(async ct2 =>
        {
            var advance = await db.CustomerAdvances.FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.CustomerAdvanceId == customerAdvanceId, ct2)
                ?? throw new NotFoundException($"Customer advance {customerAdvanceId} was not found.");
            // §37.4: "a bounced advance cheque is reversed ... like a payment" — the same "the row that was
            // never applied is the one that can still be undone outright" scope PaymentReversalService keeps.
            if (advance.Status != CustomerAdvanceStatuses.Open)
                throw new BusinessRuleException("ADVANCE_NOT_OPEN", $"Only an Open advance can be reversed (current status: {advance.Status}).", []);

            var reversalDate = request.ReversalDate ?? await clock.TodayAsync(tenant.TenantId);
            advance.Status = CustomerAdvanceStatuses.Reversed;
            advance.ReversalReason = request.Reason.Trim();
            advance.ReversedBy = userId;
            advance.ReversedOn = DateTime.UtcNow;
            await db.SaveChangesAsync(ct2);

            // §40A L10: an ADVANCE_REVERSAL debit — the original L8 credit stays exactly as posted.
            await ledger.PostAdvanceReversalAsync(customerAdvanceId, reversalDate, userId, ct2);

            var trip = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && t.TripId == advance.TripId).Select(t => t.TripNumber).FirstOrDefaultAsync(ct2);
            return ToModel(advance, trip ?? string.Empty);
        }, ct);
    }

    public async Task ApplyForTripAsync(long tripId, long invoiceId, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        var openAdvances = await db.CustomerAdvances.Where(a => a.TenantId == tenant.TenantId && a.TripId == tripId && a.Status == CustomerAdvanceStatuses.Open).ToListAsync(ct);
        if (openAdvances.Count == 0) return;

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == invoiceId, ct)
            ?? throw new NotFoundException($"Invoice {invoiceId} was not found.");

        foreach (var advance in openAdvances)
        {
            // §37.4: "The full advance is applied, not capped" — even past the invoice's own remaining balance.
            advance.Status = CustomerAdvanceStatuses.Applied;
            advance.AppliedAmount = advance.Amount;
            await ledger.PostAdvanceApplicationAsync(advance.CustomerAdvanceId, invoiceId, advance.Amount, entryDate, userId, ct);
            invoice.AdvanceAppliedAmount += advance.Amount;
        }
        InvoiceBalanceRecalculation.Apply(invoice);
        await db.SaveChangesAsync(ct);
    }

    private static CustomerAdvanceModel ToModel(CustomerAdvance a, string tripNumber) => new()
    {
        CustomerAdvanceId = a.CustomerAdvanceId, AdvanceNumber = a.AdvanceNumber, CustomerId = a.CustomerId, TripId = a.TripId, TripNumber = tripNumber,
        AdvanceDate = a.AdvanceDate, Amount = a.Amount, AppliedAmount = a.AppliedAmount, RefundedAmount = a.RefundedAmount, Status = a.Status, LedgerEntryId = a.LedgerEntryId
    };
}
