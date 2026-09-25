using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;

namespace VMS.Modules.Trips.Services;

/// <summary>§40A.1's L1-L3 (submission) and L11 (cancellation mirror) — the two posting rules CC-29's own
/// Submit/Cancel actually trigger. Every other L-code (payments, advances, regeneration, carry-forward/refund,
/// opening balance) posts through this same table but is CC-31..40's own job, not this one's.</summary>
public interface ICustomerLedgerPostingService
{
    /// <summary>L1 (invoice debit, incl. billable income), L2 (one entry per adjustment, Dr if positive, Cr if
    /// negative) and L3 (one credit per applied deduction), all dated <paramref name="entryDate"/> (the
    /// submission date). Idempotent — "invoice submitted twice due to a double click ⇒ only one set of entries
    /// exists" (§40A.5 acceptance): a no-op if L1 already exists for this invoice.</summary>
    Task PostSubmissionAsync(long invoiceId, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>L11: a mirror of every L1-L3 entry, posted when a Submitted invoice is Cancelled. A no-op if the
    /// invoice was never Submitted (nothing to mirror) — safe to call unconditionally from Cancel regardless of
    /// the invoice's prior status.</summary>
    Task PostCancellationMirrorAsync(long invoiceId, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>L12 (CC-35): the same mirror shape as L11, posted against the OLD invoice when it is superseded
    /// by regeneration — a no-op if it was never Submitted.</summary>
    Task PostRegenerationMirrorAsync(long invoiceId, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>L4 (CC-31): one credit against the invoice paid, dated the receipt date. Idempotent, same shape
    /// as the others — returns the (new or already-existing) entry's own id, so the caller can stamp
    /// <c>InvoicePayment.LedgerEntryId</c> either way.</summary>
    Task<long> PostPaymentAsync(long invoicePaymentId, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>L5 (CC-32): a debit reversal of the original L4 credit, dated the reversal date. "The original
    /// PAYMENT credit remains visible" — this posts a NEW, opposite entry, it never touches L4's own row.</summary>
    Task<long> PostPaymentReversalAsync(long invoicePaymentId, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>L6 (CC-33): one `WRITE_OFF` or `DISCOUNT` credit against the settled invoice.</summary>
    Task<long> PostSettlementAsync(long invoiceSettlementId, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>L7 (CC-33): a `SETTLEMENT_REVERSAL` debit, the same "new opposite entry, original stays visible"
    /// shape as L5's own reversal of L4.</summary>
    Task<long> PostSettlementReversalAsync(long invoiceSettlementId, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>L8 (CC-34): one `ADVANCE` credit linked to the customer and trip — "no invoice yet."</summary>
    Task<long> PostAdvanceAsync(long customerAdvanceId, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>L9 (CC-34): the `ADVANCE_APPLY_OUT` (debit, trip)/`ADVANCE_APPLY_IN` (credit, invoice) pair posted
    /// when the invoice holding the advance's own trip is Submitted — "net zero for the customer," §37.4's own
    /// "uncapped" rule (never more than the advance's own remaining amount, but never trimmed to the invoice's
    /// balance either).</summary>
    Task PostAdvanceApplicationAsync(long customerAdvanceId, long invoiceId, decimal amount, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>L10 (CC-34): an `ADVANCE_REVERSAL` debit — "a bounced advance cheque is reversed ... like a
    /// payment" (§37.4).</summary>
    Task<long> PostAdvanceReversalAsync(long customerAdvanceId, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>L15 (CC-34's own slice of it): a `REFUND` debit against an un-applied advance.</summary>
    Task<long> PostAdvanceRefundAsync(long customerAdvanceId, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>L16 (CC-40): "Opening balance may be debit or credit, posted once per customer and currency."
    /// The one L-code with no dedicated source row of its own — the FSD's own field table links it only to the
    /// pseudo-invoice, not to some new "OpeningBalance" table — so the caller (<see cref="IOpeningBalanceService"/>)
    /// passes the already-resolved amount/date/narration directly, rather than this method re-deriving them from
    /// a row that does not exist. Idempotent per (pseudo-invoice, currency): a customer with several currencies'
    /// opening balances shares one pseudo-invoice, differentiated by <paramref name="currencyCode"/>.</summary>
    Task<long> PostOpeningBalanceAsync(int customerId, string currencyCode, long pseudoInvoiceId, string documentNo,
        decimal debit, decimal credit, DateOnly entryDate, string narration, int userId, CancellationToken ct = default);

    /// <summary>L13 (CC-36): the `TRANSFER_OUT` (debit, old invoice)/`TRANSFER_IN` (credit, new invoice) pair for
    /// one already-saved <c>InvoicePaymentTransfer</c> row — "net zero for the customer," the same shape as L9's
    /// own advance-application pair. Idempotent (checking the OUT half is enough, same convention as L9).</summary>
    Task<(long OutEntryId, long InEntryId)> PostTransferAsync(long invoicePaymentTransferId, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>L14 (CC-37): the `CARRY_FORWARD_OUT` (debit, source/credit invoice)/`CARRY_FORWARD_IN` (credit,
    /// target invoice) pair for one already-saved <c>InvoiceCreditCarryForward</c> row — "net zero for the
    /// customer," the same shape as L9/L13's own pairs.</summary>
    Task<(long OutEntryId, long InEntryId)> PostCarryForwardAsync(long invoiceCreditCarryForwardId, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>L15 (CC-37's own half of it — the credit-invoice refund; the un-applied-advance refund is
    /// CC-34's own <see cref="PostAdvanceRefundAsync"/>): a `REFUND` debit against the credit invoice, for one
    /// already-saved <c>CustomerRefund</c> row.</summary>
    Task<long> PostInvoiceRefundAsync(long customerRefundId, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>Every entry posted against one invoice, oldest first — the query CC-38's own future "Invoice
    /// Ledger tab" (§40A.4) will need; built now since CC-30 already owns the table and this task's own tests
    /// need it to verify what actually posted.</summary>
    Task<IReadOnlyList<LedgerEntryModel>> ListForInvoiceAsync(long invoiceId, CancellationToken ct = default);

    /// <summary>Every entry carrying one trip's own TripId, oldest first — L8/L9-out/L10 (CC-34) all have no
    /// invoice yet (or never will, for L8/L10), so <see cref="ListForInvoiceAsync"/> alone can't find them.
    /// Reflects each entry's own TripId at posting time, not an advance's current (possibly since-moved) one.</summary>
    Task<IReadOnlyList<LedgerEntryModel>> ListForTripAsync(long tripId, CancellationToken ct = default);
}

internal sealed class CustomerLedgerPostingService(
    TripsDbContext db, ITenantContext tenant, ILedgerNumberAllocator numbers, ICustomerLedgerSequenceAllocator sequence) : ICustomerLedgerPostingService
{
    public async Task PostSubmissionAsync(long invoiceId, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == invoiceId, ct)
            ?? throw new NotFoundException($"Invoice {invoiceId} was not found.");

        // Idempotency's real guard is the unique (SourceType, SourceId, EntryType) index below — this check is
        // the cheap, common-case short-circuit, the same two-layer shape InvoiceCreationService's own app-lock
        // (cheap) + filtered unique index (authoritative) already established.
        var alreadyPosted = await db.CustomerLedgerEntries.AnyAsync(
            e => e.TenantId == tenant.TenantId && e.SourceType == LedgerSourceTypes.Invoice && e.SourceId == invoiceId && e.EntryType == LedgerEntryTypes.Invoice, ct);
        if (alreadyPosted) return;

        var adjustments = await db.InvoiceAdjustments.Where(a => a.TenantId == tenant.TenantId && a.InvoiceId == invoiceId).OrderBy(a => a.Sequence).ToListAsync(ct);
        var taxLines = await db.InvoiceTaxLines.Where(t => t.TenantId == tenant.TenantId && t.InvoiceId == invoiceId && t.Amount > 0).OrderBy(t => t.Sequence).ToListAsync(ct);

        var narrationBase = $"Invoice {invoice.InvoiceNumber} dated {invoice.InvoiceDate:dd-MMM-yyyy}, period {invoice.PeriodFrom:dd-MMM-yyyy} to {invoice.PeriodTo:dd-MMM-yyyy}";
        var entries = new List<CustomerLedgerEntry>
        {
            // L1: total trip amount already includes billable income lines (§32/§35 — InvoiceCreationService's
            // own "TotalTripAmount is this module's own name for [trip + income line amounts]").
            await NewEntryAsync(ForInvoice(invoice), entryDate, userId, LedgerEntryTypes.Invoice, LedgerSourceTypes.Invoice, invoice.InvoiceId,
                debit: invoice.TotalTripAmount, credit: 0, narration: narrationBase, ct)
        };

        // L2: one entry per adjustment, Debit if positive, Credit if negative (§40A.1).
        foreach (var adjustment in adjustments)
        {
            var narration = $"Adjustment {adjustment.AdjustmentMonth} — {adjustment.AdjustmentNote} ({invoice.InvoiceNumber})";
            entries.Add(await NewEntryAsync(ForInvoice(invoice), entryDate, userId, LedgerEntryTypes.Adjustment, LedgerSourceTypes.InvoiceAdjustment, adjustment.InvoiceAdjustmentId,
                debit: adjustment.AdjustmentAmount > 0 ? adjustment.AdjustmentAmount : 0, credit: adjustment.AdjustmentAmount < 0 ? -adjustment.AdjustmentAmount : 0, narration, ct));
        }

        // L3: one credit per applied deduction (§40A.1: "Posting deductions as their own credit entries is
        // Recommended Design, so the entries of an invoice always add up to its Net amount").
        foreach (var tax in taxLines)
        {
            var narration = $"{tax.TaxName} ({tax.TaxCode}) — {invoice.InvoiceNumber}";
            entries.Add(await NewEntryAsync(ForInvoice(invoice), entryDate, userId, LedgerEntryTypes.Deduction, LedgerSourceTypes.InvoiceTaxLine, tax.InvoiceTaxLineId,
                debit: 0, credit: tax.Amount, narration, ct));
        }

        await SaveEntriesAsync(invoice.CustomerId, invoice.CurrencyCode, entries, ct);
    }

    public Task PostCancellationMirrorAsync(long invoiceId, DateOnly entryDate, int userId, CancellationToken ct = default) =>
        PostMirrorAsync(invoiceId, entryDate, userId, LedgerEntryTypes.InvoiceCancel, "Cancellation", ct);

    public Task PostRegenerationMirrorAsync(long invoiceId, DateOnly entryDate, int userId, CancellationToken ct = default) =>
        PostMirrorAsync(invoiceId, entryDate, userId, LedgerEntryTypes.InvoiceSuperseded, "Superseded by regeneration", ct);

    /// <summary>L11 (Cancel) and L12 (Regenerate) are the same shape — mirror every still-standing L1-L3 entry,
    /// opposite Dr/Cr, dated today; a no-op if the invoice was never Submitted (nothing was ever posted).</summary>
    private async Task PostMirrorAsync(long invoiceId, DateOnly entryDate, int userId, string mirrorEntryType, string narrationVerb, CancellationToken ct)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == invoiceId, ct)
            ?? throw new NotFoundException($"Invoice {invoiceId} was not found.");

        var mirrorableTypes = new[] { LedgerEntryTypes.Invoice, LedgerEntryTypes.Adjustment, LedgerEntryTypes.Deduction };
        var toMirror = await db.CustomerLedgerEntries.Where(e => e.TenantId == tenant.TenantId && e.InvoiceId == invoiceId
            && mirrorableTypes.Contains(e.EntryType)).ToListAsync(ct);
        if (toMirror.Count == 0) return;   // never Submitted — nothing was posted, nothing to mirror.

        var alreadyMirrored = await db.CustomerLedgerEntries.AnyAsync(
            e => e.TenantId == tenant.TenantId && e.SourceType == LedgerSourceTypes.Invoice && e.SourceId == invoiceId && e.EntryType == mirrorEntryType, ct);
        if (alreadyMirrored) return;

        var entries = new List<CustomerLedgerEntry>();
        foreach (var original in toMirror)
        {
            entries.Add(await NewEntryAsync(ForInvoice(invoice), entryDate, userId, mirrorEntryType, original.SourceType, original.SourceId,
                debit: original.CreditAmount, credit: original.DebitAmount, narration: $"{narrationVerb} of {original.EntryType} ({invoice.InvoiceNumber})", ct, reversesEntryId: original.CustomerLedgerEntryId));
        }

        await SaveEntriesAsync(invoice.CustomerId, invoice.CurrencyCode, entries, ct);
    }

    public async Task<long> PostPaymentAsync(long invoicePaymentId, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        var payment = await db.InvoicePayments.FirstOrDefaultAsync(p => p.TenantId == tenant.TenantId && p.InvoicePaymentId == invoicePaymentId, ct)
            ?? throw new NotFoundException($"Invoice payment {invoicePaymentId} was not found.");

        var existing = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.InvoicePayment && e.SourceId == invoicePaymentId && e.EntryType == LedgerEntryTypes.Payment, ct);
        if (existing is not null) return existing.CustomerLedgerEntryId;

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == payment.InvoiceId, ct)
            ?? throw new NotFoundException($"Invoice {payment.InvoiceId} was not found.");
        var receipt = await db.CustomerReceipts.FirstOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.CustomerReceiptId == payment.CustomerReceiptId, ct)
            ?? throw new NotFoundException($"Customer receipt {payment.CustomerReceiptId} was not found.");

        // §40A.2's own literal narration example: "Payment RCPT-2026-00031 cheque 004512 against INV-2026-00125".
        var methodText = receipt.PaymentMethod == PaymentMethods.BankCheque ? $"cheque {receipt.InstrumentNo}" : $"transfer/deposit ref {receipt.InstrumentNo}";
        var narration = $"Payment {receipt.ReceiptNumber} {methodText} against {invoice.InvoiceNumber}";
        var entry = await NewEntryAsync(ForInvoice(invoice), entryDate, userId, LedgerEntryTypes.Payment, LedgerSourceTypes.InvoicePayment, invoicePaymentId,
            debit: 0, credit: payment.Amount, narration, ct);

        await SaveEntriesAsync(invoice.CustomerId, invoice.CurrencyCode, [entry], ct);
        // SaveEntriesAsync either inserted `entry` (it now carries a real id) or lost a genuine race and left it
        // unsaved — either way, the winning row is findable by its own (SourceType, SourceId, EntryType) triple.
        if (entry.CustomerLedgerEntryId != 0) return entry.CustomerLedgerEntryId;
        return (await db.CustomerLedgerEntries.FirstAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.InvoicePayment && e.SourceId == invoicePaymentId && e.EntryType == LedgerEntryTypes.Payment, ct)).CustomerLedgerEntryId;
    }

    public async Task<long> PostPaymentReversalAsync(long invoicePaymentId, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        var payment = await db.InvoicePayments.FirstOrDefaultAsync(p => p.TenantId == tenant.TenantId && p.InvoicePaymentId == invoicePaymentId, ct)
            ?? throw new NotFoundException($"Invoice payment {invoicePaymentId} was not found.");

        var existing = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.InvoicePayment && e.SourceId == invoicePaymentId && e.EntryType == LedgerEntryTypes.PaymentReversal, ct);
        if (existing is not null) return existing.CustomerLedgerEntryId;

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == payment.InvoiceId, ct)
            ?? throw new NotFoundException($"Invoice {payment.InvoiceId} was not found.");
        var receipt = await db.CustomerReceipts.FirstOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.CustomerReceiptId == payment.CustomerReceiptId, ct)
            ?? throw new NotFoundException($"Customer receipt {payment.CustomerReceiptId} was not found.");

        var narration = $"Reversal of payment {receipt.ReceiptNumber} against {invoice.InvoiceNumber}";
        var entry = await NewEntryAsync(ForInvoice(invoice), entryDate, userId, LedgerEntryTypes.PaymentReversal, LedgerSourceTypes.InvoicePayment, invoicePaymentId,
            debit: payment.Amount, credit: 0, narration, ct, reversesEntryId: payment.LedgerEntryId);

        await SaveEntriesAsync(invoice.CustomerId, invoice.CurrencyCode, [entry], ct);
        if (entry.CustomerLedgerEntryId != 0) return entry.CustomerLedgerEntryId;
        return (await db.CustomerLedgerEntries.FirstAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.InvoicePayment && e.SourceId == invoicePaymentId && e.EntryType == LedgerEntryTypes.PaymentReversal, ct)).CustomerLedgerEntryId;
    }

    public async Task<long> PostSettlementAsync(long invoiceSettlementId, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        var settlement = await db.InvoiceSettlements.FirstOrDefaultAsync(s => s.TenantId == tenant.TenantId && s.InvoiceSettlementId == invoiceSettlementId, ct)
            ?? throw new NotFoundException($"Invoice settlement {invoiceSettlementId} was not found.");
        var entryType = settlement.SettlementType == SettlementTypes.WriteOff ? LedgerEntryTypes.WriteOff : LedgerEntryTypes.Discount;

        var existing = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.InvoiceSettlement && e.SourceId == invoiceSettlementId && e.EntryType == entryType, ct);
        if (existing is not null) return existing.CustomerLedgerEntryId;

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == settlement.InvoiceId, ct)
            ?? throw new NotFoundException($"Invoice {settlement.InvoiceId} was not found.");

        var kind = settlement.SettlementType == SettlementTypes.WriteOff ? "Write-off" : "Discount";
        var narration = $"{kind} {settlement.SettlementNumber} — {settlement.Reason} ({invoice.InvoiceNumber})";
        var entry = await NewEntryAsync(ForInvoice(invoice), entryDate, userId, entryType, LedgerSourceTypes.InvoiceSettlement, invoiceSettlementId,
            debit: 0, credit: settlement.Amount, narration, ct);

        await SaveEntriesAsync(invoice.CustomerId, invoice.CurrencyCode, [entry], ct);
        if (entry.CustomerLedgerEntryId != 0) return entry.CustomerLedgerEntryId;
        return (await db.CustomerLedgerEntries.FirstAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.InvoiceSettlement && e.SourceId == invoiceSettlementId && e.EntryType == entryType, ct)).CustomerLedgerEntryId;
    }

    public async Task<long> PostSettlementReversalAsync(long invoiceSettlementId, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        var settlement = await db.InvoiceSettlements.FirstOrDefaultAsync(s => s.TenantId == tenant.TenantId && s.InvoiceSettlementId == invoiceSettlementId, ct)
            ?? throw new NotFoundException($"Invoice settlement {invoiceSettlementId} was not found.");

        var existing = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.InvoiceSettlement && e.SourceId == invoiceSettlementId && e.EntryType == LedgerEntryTypes.SettlementReversal, ct);
        if (existing is not null) return existing.CustomerLedgerEntryId;

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == settlement.InvoiceId, ct)
            ?? throw new NotFoundException($"Invoice {settlement.InvoiceId} was not found.");

        var narration = $"Reversal of {settlement.SettlementNumber} ({invoice.InvoiceNumber})";
        var entry = await NewEntryAsync(ForInvoice(invoice), entryDate, userId, LedgerEntryTypes.SettlementReversal, LedgerSourceTypes.InvoiceSettlement, invoiceSettlementId,
            debit: settlement.Amount, credit: 0, narration, ct, reversesEntryId: settlement.LedgerEntryId);

        await SaveEntriesAsync(invoice.CustomerId, invoice.CurrencyCode, [entry], ct);
        if (entry.CustomerLedgerEntryId != 0) return entry.CustomerLedgerEntryId;
        return (await db.CustomerLedgerEntries.FirstAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.InvoiceSettlement && e.SourceId == invoiceSettlementId && e.EntryType == LedgerEntryTypes.SettlementReversal, ct)).CustomerLedgerEntryId;
    }

    public async Task<long> PostAdvanceAsync(long customerAdvanceId, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        var advance = await db.CustomerAdvances.FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.CustomerAdvanceId == customerAdvanceId, ct)
            ?? throw new NotFoundException($"Customer advance {customerAdvanceId} was not found.");

        var existing = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.CustomerAdvance && e.SourceId == customerAdvanceId && e.EntryType == LedgerEntryTypes.Advance, ct);
        if (existing is not null) return existing.CustomerLedgerEntryId;

        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == advance.TripId, ct)
            ?? throw new NotFoundException($"Trip {advance.TripId} was not found.");
        var currencyCode = await CustomerCurrencyAsync(advance.CustomerId, ct);

        var context = new EntryContext(advance.CustomerId, currencyCode, advance.AdvanceNumber, null, advance.TripId);
        var entry = await NewEntryAsync(context, entryDate, userId, LedgerEntryTypes.Advance, LedgerSourceTypes.CustomerAdvance, customerAdvanceId,
            debit: 0, credit: advance.Amount, narration: $"Advance {advance.AdvanceNumber} for {trip.TripNumber}", ct);

        await SaveEntriesAsync(advance.CustomerId, currencyCode, [entry], ct);
        if (entry.CustomerLedgerEntryId != 0) return entry.CustomerLedgerEntryId;
        return (await db.CustomerLedgerEntries.FirstAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.CustomerAdvance && e.SourceId == customerAdvanceId && e.EntryType == LedgerEntryTypes.Advance, ct)).CustomerLedgerEntryId;
    }

    public async Task PostAdvanceApplicationAsync(long customerAdvanceId, long invoiceId, decimal amount, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        // Both entries always post together in the one SaveEntriesAsync call below — checking for the OUT half
        // is enough to know the whole pair already exists.
        var alreadyPosted = await db.CustomerLedgerEntries.AnyAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.CustomerAdvance && e.SourceId == customerAdvanceId && e.EntryType == LedgerEntryTypes.AdvanceApplyOut, ct);
        if (alreadyPosted) return;

        var advance = await db.CustomerAdvances.FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.CustomerAdvanceId == customerAdvanceId, ct)
            ?? throw new NotFoundException($"Customer advance {customerAdvanceId} was not found.");
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == invoiceId, ct)
            ?? throw new NotFoundException($"Invoice {invoiceId} was not found.");

        // §37.4: "ADVANCE_APPLY_OUT debit (trip advance) and ADVANCE_APPLY_IN credit (invoice), net zero for the
        // customer" — SaveEntriesAsync's own netDelta sum naturally comes out to zero for this pair, with no
        // special-casing needed, since amount − amount = 0.
        var outContext = new EntryContext(advance.CustomerId, invoice.CurrencyCode, advance.AdvanceNumber, null, advance.TripId);
        var outEntry = await NewEntryAsync(outContext, entryDate, userId, LedgerEntryTypes.AdvanceApplyOut, LedgerSourceTypes.CustomerAdvance, customerAdvanceId,
            debit: amount, credit: 0, narration: $"Advance {advance.AdvanceNumber} applied to {invoice.InvoiceNumber}", ct);
        var inEntry = await NewEntryAsync(ForInvoice(invoice), entryDate, userId, LedgerEntryTypes.AdvanceApplyIn, LedgerSourceTypes.CustomerAdvance, customerAdvanceId,
            debit: 0, credit: amount, narration: $"Advance {advance.AdvanceNumber} applied ({invoice.InvoiceNumber})", ct);

        await SaveEntriesAsync(advance.CustomerId, invoice.CurrencyCode, [outEntry, inEntry], ct);
    }

    public async Task<long> PostAdvanceReversalAsync(long customerAdvanceId, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        var advance = await db.CustomerAdvances.FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.CustomerAdvanceId == customerAdvanceId, ct)
            ?? throw new NotFoundException($"Customer advance {customerAdvanceId} was not found.");

        var existing = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.CustomerAdvance && e.SourceId == customerAdvanceId && e.EntryType == LedgerEntryTypes.AdvanceReversal, ct);
        if (existing is not null) return existing.CustomerLedgerEntryId;

        var currencyCode = await CustomerCurrencyAsync(advance.CustomerId, ct);
        var context = new EntryContext(advance.CustomerId, currencyCode, advance.AdvanceNumber, null, advance.TripId);
        var entry = await NewEntryAsync(context, entryDate, userId, LedgerEntryTypes.AdvanceReversal, LedgerSourceTypes.CustomerAdvance, customerAdvanceId,
            debit: advance.Amount, credit: 0, narration: $"Reversal of advance {advance.AdvanceNumber}", ct, reversesEntryId: advance.LedgerEntryId);

        await SaveEntriesAsync(advance.CustomerId, currencyCode, [entry], ct);
        if (entry.CustomerLedgerEntryId != 0) return entry.CustomerLedgerEntryId;
        return (await db.CustomerLedgerEntries.FirstAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.CustomerAdvance && e.SourceId == customerAdvanceId && e.EntryType == LedgerEntryTypes.AdvanceReversal, ct)).CustomerLedgerEntryId;
    }

    public async Task<long> PostAdvanceRefundAsync(long customerAdvanceId, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        var advance = await db.CustomerAdvances.FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.CustomerAdvanceId == customerAdvanceId, ct)
            ?? throw new NotFoundException($"Customer advance {customerAdvanceId} was not found.");

        var existing = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.CustomerAdvance && e.SourceId == customerAdvanceId && e.EntryType == LedgerEntryTypes.Refund, ct);
        if (existing is not null) return existing.CustomerLedgerEntryId;

        var currencyCode = await CustomerCurrencyAsync(advance.CustomerId, ct);
        var context = new EntryContext(advance.CustomerId, currencyCode, advance.AdvanceNumber, null, advance.TripId);
        var entry = await NewEntryAsync(context, entryDate, userId, LedgerEntryTypes.Refund, LedgerSourceTypes.CustomerAdvance, customerAdvanceId,
            debit: advance.Amount, credit: 0, narration: $"Refund of advance {advance.AdvanceNumber}", ct);

        await SaveEntriesAsync(advance.CustomerId, currencyCode, [entry], ct);
        if (entry.CustomerLedgerEntryId != 0) return entry.CustomerLedgerEntryId;
        return (await db.CustomerLedgerEntries.FirstAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.CustomerAdvance && e.SourceId == customerAdvanceId && e.EntryType == LedgerEntryTypes.Refund, ct)).CustomerLedgerEntryId;
    }

    public async Task<(long OutEntryId, long InEntryId)> PostTransferAsync(long invoicePaymentTransferId, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        var existingOut = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.PaymentTransfer && e.SourceId == invoicePaymentTransferId && e.EntryType == LedgerEntryTypes.TransferOut, ct);
        var existingIn = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.PaymentTransfer && e.SourceId == invoicePaymentTransferId && e.EntryType == LedgerEntryTypes.TransferIn, ct);
        if (existingOut is not null && existingIn is not null) return (existingOut.CustomerLedgerEntryId, existingIn.CustomerLedgerEntryId);

        var transfer = await db.InvoicePaymentTransfers.FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.InvoicePaymentTransferId == invoicePaymentTransferId, ct)
            ?? throw new NotFoundException($"Invoice payment transfer {invoicePaymentTransferId} was not found.");
        var oldInvoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == transfer.OldInvoiceId, ct)
            ?? throw new NotFoundException($"Invoice {transfer.OldInvoiceId} was not found.");
        var newInvoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == transfer.NewInvoiceId, ct)
            ?? throw new NotFoundException($"Invoice {transfer.NewInvoiceId} was not found.");

        var outEntry = await NewEntryAsync(ForInvoice(oldInvoice), entryDate, userId, LedgerEntryTypes.TransferOut, LedgerSourceTypes.PaymentTransfer, invoicePaymentTransferId,
            debit: transfer.AmountTransferred, credit: 0, narration: $"Transfer {transfer.TransferNumber} to {newInvoice.InvoiceNumber}", ct);
        var inEntry = await NewEntryAsync(ForInvoice(newInvoice), entryDate, userId, LedgerEntryTypes.TransferIn, LedgerSourceTypes.PaymentTransfer, invoicePaymentTransferId,
            debit: 0, credit: transfer.AmountTransferred, narration: $"Transfer {transfer.TransferNumber} from {oldInvoice.InvoiceNumber}", ct);

        await SaveEntriesAsync(oldInvoice.CustomerId, oldInvoice.CurrencyCode, [outEntry, inEntry], ct);
        if (outEntry.CustomerLedgerEntryId != 0 && inEntry.CustomerLedgerEntryId != 0) return (outEntry.CustomerLedgerEntryId, inEntry.CustomerLedgerEntryId);
        var savedOut = await db.CustomerLedgerEntries.FirstAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.PaymentTransfer && e.SourceId == invoicePaymentTransferId && e.EntryType == LedgerEntryTypes.TransferOut, ct);
        var savedIn = await db.CustomerLedgerEntries.FirstAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.PaymentTransfer && e.SourceId == invoicePaymentTransferId && e.EntryType == LedgerEntryTypes.TransferIn, ct);
        return (savedOut.CustomerLedgerEntryId, savedIn.CustomerLedgerEntryId);
    }

    public async Task<(long OutEntryId, long InEntryId)> PostCarryForwardAsync(long invoiceCreditCarryForwardId, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        var existingOut = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.CarryForward && e.SourceId == invoiceCreditCarryForwardId && e.EntryType == LedgerEntryTypes.CarryForwardOut, ct);
        var existingIn = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.CarryForward && e.SourceId == invoiceCreditCarryForwardId && e.EntryType == LedgerEntryTypes.CarryForwardIn, ct);
        if (existingOut is not null && existingIn is not null) return (existingOut.CustomerLedgerEntryId, existingIn.CustomerLedgerEntryId);

        var carryForward = await db.InvoiceCreditCarryForwards.FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.InvoiceCreditCarryForwardId == invoiceCreditCarryForwardId, ct)
            ?? throw new NotFoundException($"Carry forward {invoiceCreditCarryForwardId} was not found.");
        var sourceInvoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == carryForward.SourceInvoiceId, ct)
            ?? throw new NotFoundException($"Invoice {carryForward.SourceInvoiceId} was not found.");
        var targetInvoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == carryForward.TargetInvoiceId, ct)
            ?? throw new NotFoundException($"Invoice {carryForward.TargetInvoiceId} was not found.");

        var outEntry = await NewEntryAsync(ForInvoice(sourceInvoice), entryDate, userId, LedgerEntryTypes.CarryForwardOut, LedgerSourceTypes.CarryForward, invoiceCreditCarryForwardId,
            debit: carryForward.Amount, credit: 0, narration: $"Carry forward to {targetInvoice.InvoiceNumber}", ct);
        var inEntry = await NewEntryAsync(ForInvoice(targetInvoice), entryDate, userId, LedgerEntryTypes.CarryForwardIn, LedgerSourceTypes.CarryForward, invoiceCreditCarryForwardId,
            debit: 0, credit: carryForward.Amount, narration: $"Carry forward from {sourceInvoice.InvoiceNumber}", ct);

        await SaveEntriesAsync(sourceInvoice.CustomerId, sourceInvoice.CurrencyCode, [outEntry, inEntry], ct);
        if (outEntry.CustomerLedgerEntryId != 0 && inEntry.CustomerLedgerEntryId != 0) return (outEntry.CustomerLedgerEntryId, inEntry.CustomerLedgerEntryId);
        var savedOut = await db.CustomerLedgerEntries.FirstAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.CarryForward && e.SourceId == invoiceCreditCarryForwardId && e.EntryType == LedgerEntryTypes.CarryForwardOut, ct);
        var savedIn = await db.CustomerLedgerEntries.FirstAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.CarryForward && e.SourceId == invoiceCreditCarryForwardId && e.EntryType == LedgerEntryTypes.CarryForwardIn, ct);
        return (savedOut.CustomerLedgerEntryId, savedIn.CustomerLedgerEntryId);
    }

    public async Task<long> PostInvoiceRefundAsync(long customerRefundId, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        var existing = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.Refund && e.SourceId == customerRefundId && e.EntryType == LedgerEntryTypes.Refund, ct);
        if (existing is not null) return existing.CustomerLedgerEntryId;

        var refund = await db.CustomerRefunds.FirstOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.CustomerRefundId == customerRefundId, ct)
            ?? throw new NotFoundException($"Refund {customerRefundId} was not found.");
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == refund.InvoiceId, ct)
            ?? throw new NotFoundException($"Invoice {refund.InvoiceId} was not found.");

        var entry = await NewEntryAsync(ForInvoice(invoice), entryDate, userId, LedgerEntryTypes.Refund, LedgerSourceTypes.Refund, customerRefundId,
            debit: refund.Amount, credit: 0, narration: $"Refund against {invoice.InvoiceNumber}", ct);

        await SaveEntriesAsync(invoice.CustomerId, invoice.CurrencyCode, [entry], ct);
        if (entry.CustomerLedgerEntryId != 0) return entry.CustomerLedgerEntryId;
        return (await db.CustomerLedgerEntries.FirstAsync(e => e.TenantId == tenant.TenantId
            && e.SourceType == LedgerSourceTypes.Refund && e.SourceId == customerRefundId && e.EntryType == LedgerEntryTypes.Refund, ct)).CustomerLedgerEntryId;
    }

    public async Task<long> PostOpeningBalanceAsync(int customerId, string currencyCode, long pseudoInvoiceId, string documentNo,
        decimal debit, decimal credit, DateOnly entryDate, string narration, int userId, CancellationToken ct = default)
    {
        var existing = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId && e.CustomerId == customerId
            && e.CurrencyCode == currencyCode && e.SourceType == LedgerSourceTypes.OpeningBalance && e.SourceId == pseudoInvoiceId && e.EntryType == LedgerEntryTypes.OpeningBalance, ct);
        if (existing is not null) return existing.CustomerLedgerEntryId;

        var context = new EntryContext(customerId, currencyCode, documentNo, pseudoInvoiceId, null);
        var entry = await NewEntryAsync(context, entryDate, userId, LedgerEntryTypes.OpeningBalance, LedgerSourceTypes.OpeningBalance, pseudoInvoiceId,
            debit, credit, narration, ct);

        await SaveEntriesAsync(customerId, currencyCode, [entry], ct);
        if (entry.CustomerLedgerEntryId != 0) return entry.CustomerLedgerEntryId;
        return (await db.CustomerLedgerEntries.FirstAsync(e => e.TenantId == tenant.TenantId && e.CustomerId == customerId
            && e.CurrencyCode == currencyCode && e.SourceType == LedgerSourceTypes.OpeningBalance && e.SourceId == pseudoInvoiceId && e.EntryType == LedgerEntryTypes.OpeningBalance, ct)).CustomerLedgerEntryId;
    }

    private async Task<string> CustomerCurrencyAsync(int customerId, CancellationToken ct) =>
        await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId).Select(c => c.CurrencyCode).FirstOrDefaultAsync(ct) ?? "PKR";

    public async Task<IReadOnlyList<LedgerEntryModel>> ListForInvoiceAsync(long invoiceId, CancellationToken ct = default)
    {
        var entries = await db.CustomerLedgerEntries.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && e.InvoiceId == invoiceId)
            .OrderBy(e => e.CustomerSeq).ToListAsync(ct);
        return entries.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<LedgerEntryModel>> ListForTripAsync(long tripId, CancellationToken ct = default)
    {
        var entries = await db.CustomerLedgerEntries.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && e.TripId == tripId)
            .OrderBy(e => e.CustomerSeq).ToListAsync(ct);
        return entries.Select(ToModel).ToList();
    }

    /// <summary>What every entry needs regardless of what caused it — an invoice-caused entry supplies
    /// <see cref="InvoiceId"/>/<see cref="DocumentNo"/> from that invoice; a trip-advance entry (L8/L10, no
    /// invoice yet) supplies <see cref="TripId"/> and its own advance number as <see cref="DocumentNo"/> instead.</summary>
    private readonly record struct EntryContext(int CustomerId, string CurrencyCode, string DocumentNo, long? InvoiceId, long? TripId);

    private static EntryContext ForInvoice(Invoice invoice) => new(invoice.CustomerId, invoice.CurrencyCode, invoice.InvoiceNumber, invoice.InvoiceId, null);

    private async Task<CustomerLedgerEntry> NewEntryAsync(EntryContext context, DateOnly entryDate, int userId, string entryType, string sourceType, long sourceId,
        decimal debit, decimal credit, string narration, CancellationToken ct, long? reversesEntryId = null) => new()
    {
        EntryNumber = await numbers.NextAsync(ct), CustomerId = context.CustomerId, InvoiceId = context.InvoiceId, TripId = context.TripId, EntryType = entryType,
        EntryDate = entryDate, PostedOn = DateTime.UtcNow, DebitAmount = debit, CreditAmount = credit, CurrencyCode = context.CurrencyCode,
        SourceType = sourceType, SourceId = sourceId, ReversesEntryId = reversesEntryId, DocumentNo = context.DocumentNo, Narration = narration,
        CustomerSeq = await sequence.NextAsync(context.CustomerId, ct), IsSystemGenerated = true, CreatedBy = userId
    };

    private async Task SaveEntriesAsync(int customerId, string currencyCode, List<CustomerLedgerEntry> entries, CancellationToken ct)
    {
        // §40A.5 LR-7 (Recommended Design): "entries with EntryDate in a closed month are rejected unless
        // posted by Admin." The reason itself is whatever the causing action's own request already collects
        // (a submission's remarks, a payment's own reason, ...) — no separate reason is threaded through this
        // shared posting layer, since every single caller would need a new parameter for one rarely-used bypass;
        // a super-admin's own already-audited action is enough justification at this layer.
        if (!tenant.IsSuperAdmin)
        {
            var months = entries.Select(e => e.EntryDate.ToString("yyyyMM")).Distinct().ToList();
            var closed = await db.LedgerPeriods.AsNoTracking()
                .Where(p => p.TenantId == tenant.TenantId && months.Contains(p.YearMonth) && p.Status == LedgerPeriodStatuses.Closed)
                .Select(p => p.YearMonth).FirstOrDefaultAsync(ct);
            if (closed is not null)
            {
                var closedDate = DateOnly.ParseExact(closed + "01", "yyyyMMdd");
                throw new BusinessRuleException("PERIOD_CLOSED", $"Entry date falls in a closed period ({closedDate:MMM yyyy}).", []);
            }
        }

        db.CustomerLedgerEntries.AddRange(entries);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // §40A.5's own literal guarantee: "the same event can never post twice" — a genuine race lost this
            // exact insert; someone else's identical posting already exists, so there is nothing left to do.
            return;
        }

        var netDelta = entries.Sum(e => e.DebitAmount - e.CreditAmount);
        var balance = await db.CustomerBalances.FirstOrDefaultAsync(b => b.TenantId == tenant.TenantId && b.CustomerId == customerId && b.CurrencyCode == currencyCode, ct);
        if (balance is null)
        {
            db.CustomerBalances.Add(new CustomerBalance { CustomerId = customerId, CurrencyCode = currencyCode, BalanceAmount = netDelta, LastEntryId = entries[^1].CustomerLedgerEntryId });
        }
        else
        {
            balance.BalanceAmount += netDelta;
            balance.LastEntryId = entries[^1].CustomerLedgerEntryId;
        }
        await db.SaveChangesAsync(ct);
    }

    private static LedgerEntryModel ToModel(CustomerLedgerEntry e) => new()
    {
        CustomerLedgerEntryId = e.CustomerLedgerEntryId, EntryNumber = e.EntryNumber, CustomerId = e.CustomerId, InvoiceId = e.InvoiceId, TripId = e.TripId,
        EntryType = e.EntryType, EntryDate = e.EntryDate, DebitAmount = e.DebitAmount, CreditAmount = e.CreditAmount, CurrencyCode = e.CurrencyCode,
        SourceType = e.SourceType, SourceId = e.SourceId, ReversesEntryId = e.ReversesEntryId, DocumentNo = e.DocumentNo, Narration = e.Narration, CustomerSeq = e.CustomerSeq
    };
}
