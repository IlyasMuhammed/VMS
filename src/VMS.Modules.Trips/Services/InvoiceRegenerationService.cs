using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;

namespace VMS.Modules.Trips.Services;

/// <summary>§38/§39: replacing a not-fully-paid invoice with a new number, no approval step. Reuses
/// <see cref="IInvoiceCreationService.CreateAsync"/> directly for the new invoice's own line-building/overlap/
/// eligibility logic (the old invoice is deactivated, and its own trips unlinked, BEFORE that call — which is
/// exactly what makes CreateAsync's existing checks treat both as "free" without needing their own special-cased
/// "excluding the invoice being regenerated" logic), then stamps the version-chain fields on afterward. Payment
/// transfer (§40) is deliberately not done here — CC-36's own job.</summary>
public interface IInvoiceRegenerationService
{
    Task<InvoiceRegenerationModel> RegenerateAsync(long invoiceId, RegenerateInvoiceRequest request, int userId, CancellationToken ct = default);
}

internal sealed class InvoiceRegenerationService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IInvoiceCreationService invoices,
    ITripRepricingService repricing, ICustomerLedgerPostingService ledger, IInvoicePaymentTransferService transfers) : IInvoiceRegenerationService
{
    public async Task<InvoiceRegenerationModel> RegenerateAsync(long invoiceId, RegenerateInvoiceRequest request, int userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RegenerationReason))
            throw new ValidationException(messages.Error("regenerationReason", Msg.Required, ("Field", "Regeneration reason")));
        if (request.TripIds.Count == 0)
            throw new ValidationException(messages.Error("tripIds", Msg.Required, ("Field", "At least one trip")));

        return await db.InTransactionAsync(async ct2 =>
        {
            var old = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == invoiceId, ct2)
                ?? throw new NotFoundException($"Invoice {invoiceId} was not found.");

            // §38: "Invoices in Draft are simply edited, not regenerated. Cancelled and Inactive invoices cannot
            // be regenerated."
            if (old.Status is InvoiceStatuses.Draft or InvoiceStatuses.Cancelled or InvoiceStatuses.Inactive)
                throw new BusinessRuleException("INVALID_STATUS", $"An invoice that is {old.Status} cannot be regenerated.", []);
            // AC-41: "fully paid (incl. fully settled)" — the same BalanceAmount ≤ 0 criterion CC-33's own fix
            // already made cover write-offs/discounts reaching zero, not just payments.
            if (old.PaymentStatus == InvoicePaymentStatuses.Paid)
                throw new BusinessRuleException("INVOICE_FULLY_PAID", "This invoice is fully paid and cannot be regenerated.", []);

            var oldActiveTripIds = await db.InvoiceTripLinks.Where(l => l.TenantId == tenant.TenantId && l.InvoiceId == invoiceId && l.IsActive)
                .Select(l => l.TripId).ToListAsync(ct2);

            // §38 step 5: "old trip links deactivated" — done up front, before CreateAsync ever runs, so its own
            // "already invoiced" and rate-eligibility checks see these trips as free, exactly like any other
            // available trip; no special-cased "except the invoice being regenerated" branch needed anywhere.
            var oldLinks = await db.InvoiceTripLinks.Where(l => l.TenantId == tenant.TenantId && l.InvoiceId == invoiceId && l.IsActive).ToListAsync(ct2);
            foreach (var link in oldLinks) { link.IsActive = false; link.UnlinkedOn = DateTime.UtcNow; link.UnlinkReason = "Regenerated"; }
            if (oldActiveTripIds.Count > 0)
            {
                var oldTrips = await db.Trips.Where(t => t.TenantId == tenant.TenantId && oldActiveTripIds.Contains(t.TripId)).ToListAsync(ct2);
                foreach (var trip in oldTrips) trip.InvoiceId = null;
            }
            // §39: "excluding the invoice being regenerated" — CreateAsync's own overlap check only ever looks
            // at IsActive invoices, so flipping this now is what excludes it, with no separate parameter needed.
            old.Status = InvoiceStatuses.Inactive;
            old.IsActive = false;
            await db.SaveChangesAsync(ct2);

            // §38 step 3/§26: re-pricing is only reachable now that these trips are no longer linked to an
            // active invoice — RepriceAsync itself excludes any trip that still is.
            if (request.RepriceTrips)
                await repricing.RepriceAsync(new RepriceTripsRequest { TripIds = request.TripIds, Commit = true, Reason = request.RegenerationReason }, userId, ct2);

            var newInvoiceModel = await invoices.CreateAsync(new CreateInvoiceRequest
            {
                CustomerId = old.CustomerId, PeriodFrom = request.PeriodFrom ?? old.PeriodFrom, PeriodTo = request.PeriodTo ?? old.PeriodTo,
                InvoiceDate = request.InvoiceDate, CustomerInvoiceTemplateId = request.CustomerInvoiceTemplateId ?? old.CustomerInvoiceTemplateId,
                CustomerBillingAddressId = request.CustomerBillingAddressId ?? old.CustomerBillingAddressId, TripIds = request.TripIds,
                Adjustments = request.Adjustments, SaveAsDraft = false, Remarks = request.Remarks
            }, userId, ct2);

            // §38 step 5's own versioning fields — CreateAsync itself has no notion of regeneration, so these
            // are stamped on afterward, in the same transaction, before anything commits.
            var newInvoice = await db.Invoices.FirstAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == newInvoiceModel.InvoiceId, ct2);
            newInvoice.RootInvoiceId = old.RootInvoiceId;
            newInvoice.PreviousInvoiceId = old.InvoiceId;
            newInvoice.Version = old.Version + 1;
            newInvoice.RegenerationReason = request.RegenerationReason.Trim();
            newInvoice.RegeneratedBy = userId;
            newInvoice.RegeneratedOn = DateTime.UtcNow;
            old.ReplacedByInvoiceId = newInvoice.InvoiceId;
            await db.SaveChangesAsync(ct2);

            // §40A L12: mirror L1-L3 against the OLD invoice — a no-op if it was never Submitted, the same
            // shape as L11's own cancellation mirror (CC-29).
            await ledger.PostRegenerationMirrorAsync(old.InvoiceId, newInvoice.InvoiceDate, userId, ct2);
            // §40/§40A L13: 100% of every still-standing credit on the old invoice (payments, applied advances,
            // and — chained — prior transfers already carried onto it) moves to the new one, uncapped — a no-op
            // if the old invoice was never Submitted (nothing was ever posted against it to move).
            var posted = await transfers.TransferAsync(old.InvoiceId, newInvoice.InvoiceId, request.RegenerationReason.Trim(), newInvoice.InvoiceDate, userId, ct2);

            var releasedTripIds = oldActiveTripIds.Except(request.TripIds).ToList();
            var releasedTripNumbers = releasedTripIds.Count == 0 ? new List<string>() : await db.Trips.AsNoTracking()
                .Where(t => t.TenantId == tenant.TenantId && releasedTripIds.Contains(t.TripId)).Select(t => t.TripNumber).ToListAsync(ct2);
            var warnings = new List<string>(newInvoiceModel.Warnings);
            if (releasedTripNumbers.Count > 0)
                warnings.Add($"{releasedTripNumbers.Count} trip(s) on {old.InvoiceNumber} fall outside the new selection and are now uninvoiced: {string.Join(", ", releasedTripNumbers)}.");

            var finalModel = await invoices.GetAsync(newInvoice.InvoiceId, ct2);
            return new InvoiceRegenerationModel
            {
                Invoice = finalModel, PreviousInvoiceId = old.InvoiceId, PreviousInvoiceNumber = old.InvoiceNumber,
                ReleasedTripNumbers = releasedTripNumbers, Transfers = posted.ToList(), Warnings = warnings
            };
        }, ct);
    }
}
