using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Services;

/// <summary>§40: on regeneration, 100% of every still-standing credit on the OLD invoice moves to the NEW one,
/// uncapped — payments (§40's own literal text), applied advances and, for a chained regeneration, transfers
/// already carried onto this invoice by an earlier one (see <see cref="PaymentTransferSourceKinds"/>'s own doc
/// comment). Called once, inside <see cref="IInvoiceRegenerationService.RegenerateAsync"/>'s own transaction,
/// right after the new invoice exists and CC-35's own L12 mirror has posted.</summary>
public interface IInvoicePaymentTransferService
{
    Task<IReadOnlyList<PaymentTransferModel>> TransferAsync(long oldInvoiceId, long newInvoiceId, string reason, DateOnly entryDate, int userId, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentTransferModel>> ListForInvoiceAsync(long invoiceId, CancellationToken ct = default);
}

internal sealed class InvoicePaymentTransferService(
    TripsDbContext db, ITenantContext tenant, ITransferNumberAllocator numbers, ICustomerLedgerPostingService ledger) : IInvoicePaymentTransferService
{
    public async Task<IReadOnlyList<PaymentTransferModel>> TransferAsync(long oldInvoiceId, long newInvoiceId, string reason, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        var candidates = new List<(string Kind, long SourceId, decimal Amount)>();

        // Payments: every row still Posted on the old invoice (Reversed ones stay put — nothing to move).
        var payments = await db.InvoicePayments.Where(p => p.TenantId == tenant.TenantId && p.InvoiceId == oldInvoiceId && p.Status == InvoicePaymentRowStatuses.Posted).ToListAsync(ct);
        candidates.AddRange(payments.Select(p => (PaymentTransferSourceKinds.Payment, p.InvoicePaymentId, p.Amount)));

        // Applied advances (L9-in credits directly on the old invoice) and, for a chained regeneration, prior
        // TRANSFER_IN credits already carried onto the old invoice by an earlier regeneration — both read
        // straight off the ledger, since neither CustomerAdvance nor InvoicePaymentTransfer itself is re-pointed
        // at whichever invoice currently holds the credit (both stay append-only, same as every other §40A row).
        var creditEntries = await db.CustomerLedgerEntries.Where(e => e.TenantId == tenant.TenantId && e.InvoiceId == oldInvoiceId
            && (e.EntryType == LedgerEntryTypes.AdvanceApplyIn || e.EntryType == LedgerEntryTypes.TransferIn)).ToListAsync(ct);
        candidates.AddRange(creditEntries.Select(e => (
            e.EntryType == LedgerEntryTypes.AdvanceApplyIn ? PaymentTransferSourceKinds.AdvanceApplication : PaymentTransferSourceKinds.PriorTransfer,
            e.SourceId, e.CreditAmount)));

        if (candidates.Count == 0) return [];

        // Idempotency (defensive, same "cheap short-circuit" role as every other L-code's own check): a source
        // this old invoice's own regeneration already transferred once is never transferred a second time.
        var alreadyTransferred = await db.InvoicePaymentTransfers.Where(t => t.TenantId == tenant.TenantId && t.OldInvoiceId == oldInvoiceId)
            .Select(t => new { t.SourceKind, t.SourceId }).ToListAsync(ct);
        var alreadyDone = alreadyTransferred.Select(t => (t.SourceKind, t.SourceId)).ToHashSet();

        var oldInvoice = await db.Invoices.FirstAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == oldInvoiceId, ct);
        var newInvoice = await db.Invoices.FirstAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == newInvoiceId, ct);

        var results = new List<PaymentTransferModel>();
        foreach (var (kind, sourceId, amount) in candidates)
        {
            if (alreadyDone.Contains((kind, sourceId))) continue;

            var transfer = new InvoicePaymentTransfer
            {
                TransferNumber = await numbers.NextAsync(ct), OldInvoiceId = oldInvoiceId, NewInvoiceId = newInvoiceId,
                SourceKind = kind, SourceId = sourceId, AmountTransferred = amount, TransferDate = entryDate,
                Reason = reason, TransferredBy = userId, TransferredOn = DateTime.UtcNow
            };
            db.InvoicePaymentTransfers.Add(transfer);
            await db.SaveChangesAsync(ct);

            var (outId, inId) = await ledger.PostTransferAsync(transfer.InvoicePaymentTransferId, entryDate, userId, ct);
            transfer.LedgerEntryOutId = outId;
            transfer.LedgerEntryInId = inId;

            if (kind == PaymentTransferSourceKinds.Payment)
            {
                var payment = payments.First(p => p.InvoicePaymentId == sourceId);
                payment.Status = InvoicePaymentRowStatuses.Transferred;
            }

            // §36's own balance formula (CC-23's TransferredInAmount/TransferredOutAmount columns, unused until
            // now): the new invoice's balance falls by the transferred amount (AC-42/43 — uncapped, so it can go
            // negative); the old, now-Inactive invoice's own header balance reverts to looking outstanding again
            // from ITS own perspective, since the credit that used to sit against it moved elsewhere — the
            // ledger (fully netted to zero by L12 + this L13) stays the actual source of truth for it, matching
            // §40A.2's own "the ledger rows remain the source of truth" principle; nobody reads a superseded
            // invoice's own header balance as a live claim once it carries the "Replaced by" banner.
            oldInvoice.TransferredOutAmount += amount;
            newInvoice.TransferredInAmount += amount;

            results.Add(ToModel(transfer, string.Empty, string.Empty));
        }

        InvoiceBalanceRecalculation.Apply(oldInvoice);
        InvoiceBalanceRecalculation.Apply(newInvoice);
        await db.SaveChangesAsync(ct);

        return results;
    }

    public async Task<IReadOnlyList<PaymentTransferModel>> ListForInvoiceAsync(long invoiceId, CancellationToken ct = default)
    {
        var transfers = await db.InvoicePaymentTransfers.AsNoTracking()
            .Where(t => t.TenantId == tenant.TenantId && (t.OldInvoiceId == invoiceId || t.NewInvoiceId == invoiceId))
            .OrderBy(t => t.TransferredOn).ToListAsync(ct);
        if (transfers.Count == 0) return [];

        var invoiceIds = transfers.SelectMany(t => new[] { t.OldInvoiceId, t.NewInvoiceId }).Distinct().ToList();
        var numbersByInvoice = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId))
            .ToDictionaryAsync(i => i.InvoiceId, i => i.InvoiceNumber, ct);

        return transfers.Select(t => ToModel(t, numbersByInvoice.GetValueOrDefault(t.OldInvoiceId, string.Empty), numbersByInvoice.GetValueOrDefault(t.NewInvoiceId, string.Empty))).ToList();
    }

    private static PaymentTransferModel ToModel(InvoicePaymentTransfer t, string oldInvoiceNumber, string newInvoiceNumber) => new()
    {
        InvoicePaymentTransferId = t.InvoicePaymentTransferId, TransferNumber = t.TransferNumber, OldInvoiceId = t.OldInvoiceId, OldInvoiceNumber = oldInvoiceNumber,
        NewInvoiceId = t.NewInvoiceId, NewInvoiceNumber = newInvoiceNumber, SourceKind = t.SourceKind, SourceId = t.SourceId,
        AmountTransferred = t.AmountTransferred, TransferDate = t.TransferDate, Reason = t.Reason
    };
}
