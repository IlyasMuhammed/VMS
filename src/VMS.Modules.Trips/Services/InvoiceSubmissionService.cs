using Microsoft.Data.SqlClient;
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

/// <summary>Invoice submit and cancel (§36, §47.3, AC-35, AC-55). No approval step exists (§36): a Generated
/// invoice is submitted directly. §40A's "auto posting on submission date" (L1-L3) and §36's "reversal of posted
/// entries" on Cancel (L11) now post through <see cref="ICustomerLedgerPostingService"/> (CC-30) — genuinely
/// wired in, not the placeholder CC-29 originally left this exact point as.</summary>
public interface IInvoiceSubmissionService
{
    Task<InvoiceModel> SubmitAsync(long invoiceId, SubmitInvoiceRequest request, int userId, CancellationToken ct = default);
    Task<InvoiceModel> CancelAsync(long invoiceId, CancelInvoiceRequest request, int userId, CancellationToken ct = default);
}

internal sealed class InvoiceSubmissionService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IOperatingClock clock,
    ICustomerBillingConfigurationService billingConfigurations, IInvoiceCreationService invoices, ICustomerLedgerPostingService ledger, IAdvanceService advances) : IInvoiceSubmissionService
{
    public async Task<InvoiceModel> SubmitAsync(long invoiceId, SubmitInvoiceRequest request, int userId, CancellationToken ct = default)
    {
        RequireRowVersion(request.RowVersion);
        var channel = string.IsNullOrWhiteSpace(request.SubmissionChannel) ? SubmissionChannels.Hand : request.SubmissionChannel.Trim();
        if (!SubmissionChannels.All.Contains(channel))
            throw new ValidationException(messages.Error("submissionChannel", Msg.OneOf, ("Field", "Submission channel"), ("Allowed", string.Join(", ", SubmissionChannels.All))));

        return await RunAsync(async ct2 =>
        {
            var invoice = await Find(invoiceId, ct2);
            // §36: "Invoice approval is not required — a Generated invoice is submitted directly" (AC-55).
            if (invoice.Status != InvoiceStatuses.Generated)
                throw new BusinessRuleException("INVALID_STATUS", $"Only a Generated invoice can be submitted (current status: {invoice.Status}).", []);

            // §41: "Submitting an invoice requires evidence status Generated when the customer's
            // EvidenceRequired = 1." Read fresh (not a snapshot) — the same toggle CC-25 already reads live
            // rather than off the invoice, since it is a current customer setting, not a historical fact.
            var billing = await billingConfigurations.GetCurrentAsync(invoice.CustomerId, ct2);
            if (billing.EvidenceRequired)
            {
                var latestEvidence = await db.InvoiceEvidences.AsNoTracking()
                    .Where(e => e.TenantId == tenant.TenantId && e.InvoiceId == invoiceId)
                    .OrderByDescending(e => e.EvidenceVersion).FirstOrDefaultAsync(ct2);
                if (latestEvidence is null || latestEvidence.Status != InvoiceEvidenceStatuses.Generated)
                    throw new BusinessRuleException("EVIDENCE_NOT_READY", "Invoice evidence is not ready. Please wait or retry evidence generation.", []);
            }
            // §36's own "ledger period open" check has no period-lock table yet (a later CC in the ledger
            // cluster) — nothing to check today, so PERIOD_CLOSED can never fire until then.

            var fromStatus = invoice.Status;
            invoice.Status = InvoiceStatuses.Submitted;
            invoice.SubmittedBy = userId;
            invoice.SubmittedOn = request.SubmittedOn ?? await clock.TodayAsync(tenant.TenantId);
            invoice.SubmissionChannel = channel;
            // AcknowledgementDocumentId: accepted on the request per §47.3's own body shape, but no column
            // exists to store it yet — a deliberately minimal choice, since nothing in this task's own
            // acceptance criteria (AC-35, AC-55, evidence gate) needs it; add the column if a real caller does.

            db.InvoiceHistories.Add(new InvoiceHistory
            {
                InvoiceId = invoice.InvoiceId, FromStatus = fromStatus, ToStatus = invoice.Status, ChangedBy = userId, ChangedAtUtc = DateTime.UtcNow
            });

            // Set the concurrency check against the CALLER's own claimed row version before anything saves
            // this entity — whichever SaveChangesAsync call flushes it first must validate against it, so this
            // has to happen before the save below, not after.
            db.Entry(invoice).Property(i => i.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion);
            try { await db.SaveChangesAsync(ct2); }
            catch (DbUpdateConcurrencyException) { throw await StaleAsync(invoiceId, ct2); }

            // §40A LR-1: "On Submit the system posts the invoice debit, one entry per adjustment and one per
            // deduction, all dated on the submission date" — only after the status change itself is safely
            // committed (inside this same transaction, so a posting failure still rolls back the whole submit).
            await ledger.PostSubmissionAsync(invoice.InvoiceId, invoice.SubmittedOn!.Value, userId, ct2);

            // §37.4: "When the invoice containing that trip is submitted, the system automatically applies all
            // Open advances of the trip to that invoice" — one call per trip line this invoice actually carries;
            // a trip with no Open advances is a cheap no-op (AdvanceService checks before doing anything).
            var tripIds = await db.InvoiceLines.Where(l => l.TenantId == tenant.TenantId && l.InvoiceId == invoiceId && l.TripId != null)
                .Select(l => l.TripId!.Value).Distinct().ToListAsync(ct2);
            foreach (var tripId in tripIds) await advances.ApplyForTripAsync(tripId, invoice.InvoiceId, invoice.SubmittedOn!.Value, userId, ct2);

            return await invoices.GetAsync(invoiceId, ct2);
        }, ct);
    }

    public async Task<InvoiceModel> CancelAsync(long invoiceId, CancelInvoiceRequest request, int userId, CancellationToken ct = default)
    {
        RequireRowVersion(request.RowVersion);
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Cancellation reason")));

        return await RunAsync(async ct2 =>
        {
            var invoice = await Find(invoiceId, ct2);
            if (invoice.Status is InvoiceStatuses.Cancelled or InvoiceStatuses.Inactive)
                throw new BusinessRuleException("INVALID_STATUS", $"An invoice that is already {invoice.Status} cannot be cancelled again.", []);

            // §36: "blocked if any non-reversed payment, write-off or discount exists." None of those tables
            // exist yet (Payments = CC-31, write-off/discount = CC-33/34) — there is nothing to check today;
            // this is the exact point those later tasks need to add their own guard, not an oversight here.

            var fromStatus = invoice.Status;
            invoice.Status = InvoiceStatuses.Cancelled;
            invoice.IsActive = false;   // §32.1: an inactive/cancelled invoice never blocks a future period overlap check again.
            invoice.CancelledBy = userId;
            invoice.CancelledOn = DateTime.UtcNow;
            invoice.CancelReason = request.Reason.Trim();

            // §32.1/§46.4: "trips released" — the same unlink shape CC-23's own doc comment reserved for
            // regeneration (a later CC-35 concern), reused here since Cancel is the other place a trip's
            // billing lock needs to come off.
            var links = await db.InvoiceTripLinks.Where(l => l.TenantId == tenant.TenantId && l.InvoiceId == invoiceId && l.IsActive).ToListAsync(ct2);
            var tripIds = links.Select(l => l.TripId).ToList();
            foreach (var link in links) { link.IsActive = false; link.UnlinkedOn = DateTime.UtcNow; link.UnlinkReason = "Invoice cancelled"; }

            if (tripIds.Count > 0)
            {
                var trips = await db.Trips.Where(t => t.TenantId == tenant.TenantId && tripIds.Contains(t.TripId)).ToListAsync(ct2);
                foreach (var trip in trips) trip.InvoiceId = null;
            }

            // §30: releases the billed-income lock too, so a voided/cancelled invoice's income can be voided
            // or picked up again by whichever invoice replaces it — otherwise TripIncomeService.VoidAsync's own
            // lock (CC-21) would leave it stuck forever pointing at a cancelled invoice's own line.
            var incomeLineIds = await db.InvoiceLines.Where(l => l.TenantId == tenant.TenantId && l.InvoiceId == invoiceId && l.LineType == InvoiceLineTypes.Income)
                .Select(l => l.InvoiceLineId).ToListAsync(ct2);
            if (incomeLineIds.Count > 0)
            {
                var incomes = await db.TripIncomes.Where(i => i.TenantId == tenant.TenantId && i.InvoiceLineId != null && incomeLineIds.Contains(i.InvoiceLineId!.Value)).ToListAsync(ct2);
                foreach (var income in incomes) income.InvoiceLineId = null;
            }

            db.InvoiceHistories.Add(new InvoiceHistory
            {
                InvoiceId = invoice.InvoiceId, FromStatus = fromStatus, ToStatus = invoice.Status, Reason = invoice.CancelReason, ChangedBy = userId, ChangedAtUtc = DateTime.UtcNow
            });

            db.Entry(invoice).Property(i => i.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion);
            try { await db.SaveChangesAsync(ct2); }
            catch (DbUpdateConcurrencyException) { throw await StaleAsync(invoiceId, ct2); }

            // §36/§40A L11: mirrors L1-L3 if the invoice had actually been Submitted; a safe no-op otherwise
            // (Draft/Generated invoices were never posted, so there's nothing to mirror).
            await ledger.PostCancellationMirrorAsync(invoice.InvoiceId, await clock.TodayAsync(tenant.TenantId), userId, ct2);

            return await invoices.GetAsync(invoiceId, ct2);
        }, ct);
    }

    /// <summary>Wraps <c>db.InTransactionAsync</c> so a genuine SQL deadlock (error 1205) between two concurrent
    /// requests for the same invoice — which EF's own retry strategy may not always absorb, since a retry can
    /// legitimately fail again with a real business conflict once the winner has already committed — turns into
    /// the same clean, retryable 409 <see cref="TripRateService"/>'s own Serializable-transaction posting already
    /// established, rather than an opaque 500.</summary>
    private async Task<InvoiceModel> RunAsync(Func<CancellationToken, Task<InvoiceModel>> work, CancellationToken ct)
    {
        try { return await db.InTransactionAsync(work, ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 })
        {
            throw new ConcurrencyConflictException("Another request is updating this invoice at the same time. Please try again.");
        }
    }

    private async Task<Invoice> Find(long invoiceId, CancellationToken ct) =>
        await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == invoiceId, ct)
        ?? throw new NotFoundException($"Invoice {invoiceId} was not found.");

    private void RequireRowVersion(string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) throw new ValidationException(messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));
    }

    private async Task<Exception> StaleAsync(long invoiceId, CancellationToken ct)
    {
        var last = await db.Set<VMS.Shared.Auditing.AuditEntry>().AsNoTracking()
            .Where(a => a.TenantId == tenant.TenantId && a.RootEntity == "Invoice" && a.RootRecordId == invoiceId.ToString())
            .OrderByDescending(a => a.AuditEntryID).FirstOrDefaultAsync(ct);
        return new ConcurrencyConflictException(messages.Text(Msg.ChangedByAnother, ("User", last?.UserName ?? "another user"),
            ("At", last is null ? "just now" : $"{last.OccurredAt:yyyy-MM-dd HH:mm} UTC")));
    }
}
