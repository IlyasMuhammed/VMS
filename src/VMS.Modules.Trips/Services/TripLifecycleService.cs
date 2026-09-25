using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Time;

namespace VMS.Modules.Trips.Services;

public interface ITripLifecycleService
{
    Task<TripModel> TransitionAsync(long tripId, string toStatus, TransitionTripRequest request, TripCaller caller, CancellationToken ct = default);
    Task<TripModel> HoldAsync(long tripId, HoldTripRequest request, TripCaller caller, CancellationToken ct = default);
    Task<TripModel> ResumeAsync(long tripId, ResumeTripRequest request, TripCaller caller, CancellationToken ct = default);
    Task<TripModel> CancelAsync(long tripId, CancelTripRequest request, TripCaller caller, CancellationToken ct = default);
    Task<TripModel> ReopenAsync(long tripId, ReopenTripRequest request, TripCaller caller, CancellationToken ct = default);
    Task<TripModel> InactivateAsync(long tripId, ChangeTripActiveRequest request, CancellationToken ct = default);
    Task<TripModel> ReactivateAsync(long tripId, ChangeTripActiveRequest request, CancellationToken ct = default);
}

internal sealed class TripLifecycleService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IOperatingClock clock, ICallerScope scope, ITripService trips,
    ITripEventRecorder events, ICustomerBillingConfigurationService billingConfigurations) : ITripLifecycleService
{
    public async Task<TripModel> TransitionAsync(long tripId, string toStatus, TransitionTripRequest request, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await Find(tripId, ct);
        if (!TripStatuses.All.Contains(toStatus)) throw new ValidationException(messages.Error("status", Msg.OneOf, ("Field", "Status"), ("Allowed", string.Join(", ", TripStatuses.All))));

        var isNormal = TripLifecycle.IsNormalTransition(trip.Status, toStatus);
        if (!isNormal)
        {
            // §24: "Status changes are only through transition actions (never free edit of Status)." Anything not
            // in the table is either genuinely invalid (Hold/Cancel/Reopen/Resume have their own actions below, so
            // reaching them or Draft through here is always wrong) or a deliberate skip.
            if (toStatus is TripStatuses.OnHold or TripStatuses.Cancelled or TripStatuses.Draft || trip.Status is TripStatuses.OnHold or TripStatuses.Cancelled or TripStatuses.Completed)
                throw InvalidTransition(trip.Status, toStatus);
            if (!caller.Has(PermissionCodes.TRP_TRIP_SKIPSTATUS)) throw InvalidTransition(trip.Status, toStatus);
            // §24: "recorded as system events for skipped steps" — CC-16's own TripEvent now exists; recorded below,
            // alongside (not instead of) the destination status's own event, once every other check has passed.
        }

        RequireStatusPermission(trip, caller);
        RequireRowVersion(request.RowVersion);

        if (toStatus == TripStatuses.Started && request.StartOdometer is null)
            throw new ValidationException(messages.Error("startOdometer", Msg.Required, ("Field", "Start odometer")));
        if (toStatus == TripStatuses.Delivered && request.EndOdometer is null)
            throw new ValidationException(messages.Error("endOdometer", Msg.Required, ("Field", "End odometer")));
        if (toStatus == TripStatuses.Assigned && trip.DriverId is null)
            throw new ValidationException(messages.Error("driverId", Msg.Required, ("Field", "Driver (required before a trip can be Assigned)")));

        if (toStatus == TripStatuses.Completed)
        {
            // §24: "Delivered → Completed: End odometer; POD if customer requires" and "Who: Driver (if POD
            // uploaded), Ops, Fleet" — two distinct conditions: the customer's own requirement (blocks everyone
            // until an Approved POD exists) and the driver's own eligibility (blocks only the driver's own,
            // permission-less path, until *some* POD has at least been uploaded).
            var latestPod = await db.TripPODs.AsNoTracking().Where(p => p.TenantId == tenant.TenantId && p.TripId == tripId)
                .OrderByDescending(p => p.UploadedAtUtc).FirstOrDefaultAsync(ct);
            var billing = await billingConfigurations.GetCurrentAsync(trip.CustomerId, ct);
            if (billing.PodRequired && latestPod?.Status != PodStatuses.Approved)
                throw new BusinessRuleException("POD_REQUIRED_FOR_COMPLETION", "This customer requires an approved proof of delivery before the trip can be completed.",
                    [new BusinessRuleDetail("status", toStatus, "No approved proof of delivery on file.")]);
            if (!caller.Has(PermissionCodes.TRP_TRIP_STATUS) && TripAccess.IsOwnDriver(trip, scope) && latestPod is null)
                throw new BusinessRuleException("POD_REQUIRED_FOR_COMPLETION", "Upload a proof of delivery before marking this trip Completed.",
                    [new BusinessRuleDetail("status", toStatus, "No proof of delivery on file yet.")]);
        }

        try { db.Entry(trip).Property(t => t.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        if (request.StartOdometer is not null) { trip.StartOdometer = request.StartOdometer; trip.ActualStart = DateTime.UtcNow; }
        if (request.EndOdometer is not null) trip.EndOdometer = request.EndOdometer;
        var fromStatus = trip.Status;
        trip.Status = toStatus;
        if (toStatus == TripStatuses.Completed)
        {
            trip.ActualEnd ??= DateTime.UtcNow;
            // §24: "the ActualEnd date in the tenant's business time zone, which decides the billing period."
            trip.CompletionDate = await clock.TodayAsync(tenant.TenantId);
        }

        if (!isNormal) events.Record(tripId, TripEventTypes.StatusSkipped, TripEventSources.System, caller.UserId, remarks: $"Skipped from {fromStatus} to {toStatus}.");
        events.Record(tripId, TripEventTypes.ForStatus(toStatus), TripAccess.IsOwnDriver(trip, scope) ? TripEventSources.DriverApp : TripEventSources.Manual, caller.UserId,
            odometer: request.StartOdometer ?? request.EndOdometer);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(tripId, ct); }
        return await trips.GetAsync(tripId, ct);
    }

    public async Task<TripModel> HoldAsync(long tripId, HoldTripRequest request, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await Find(tripId, ct);
        if (!TripLifecycle.CanHold(trip.Status)) throw InvalidTransition(trip.Status, TripStatuses.OnHold);
        RequireStatusPermission(trip, caller);
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Hold reason")));
        RequireRowVersion(request.RowVersion);

        try { db.Entry(trip).Property(t => t.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        trip.HeldFromStatus = trip.Status;
        trip.Status = TripStatuses.OnHold;
        trip.HoldReason = request.Reason.Trim();
        events.Record(tripId, TripEventTypes.OnHold, TripAccess.IsOwnDriver(trip, scope) ? TripEventSources.DriverApp : TripEventSources.Manual, caller.UserId, remarks: trip.HoldReason);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(tripId, ct); }
        return await trips.GetAsync(tripId, ct);
    }

    public async Task<TripModel> ResumeAsync(long tripId, ResumeTripRequest request, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await Find(tripId, ct);
        if (trip.Status != TripStatuses.OnHold || trip.HeldFromStatus is null) throw new ConflictException("This trip is not On Hold.");
        RequireStatusPermission(trip, caller);
        RequireRowVersion(request.RowVersion);

        try { db.Entry(trip).Property(t => t.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        // §24: "resume returns to previous status."
        trip.Status = trip.HeldFromStatus;
        trip.HeldFromStatus = null;
        events.Record(tripId, TripEventTypes.Resumed, TripAccess.IsOwnDriver(trip, scope) ? TripEventSources.DriverApp : TripEventSources.Manual, caller.UserId, remarks: $"Resumed to {trip.Status}.");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(tripId, ct); }
        return await trips.GetAsync(tripId, ct);
    }

    public async Task<TripModel> CancelAsync(long tripId, CancelTripRequest request, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await Find(tripId, ct);
        if (!TripLifecycle.CanCancel(trip.Status)) throw InvalidTransition(trip.Status, TripStatuses.Cancelled);
        // §24: "Cancel after Started requires Fleet/Admin" — the stronger status permission; earlier states use
        // the same permission (or the trip's own driver) as any other transition.
        if (TripLifecycle.CancelNeedsElevatedPermission(trip.Status))
        {
            if (!caller.Has(PermissionCodes.TRP_TRIP_STATUS)) throw new ForbiddenException("Cancelling a trip that has already started needs Fleet Manager or Admin.");
        }
        else RequireStatusPermission(trip, caller);
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Cancellation reason")));
        RequireRowVersion(request.RowVersion);

        try { db.Entry(trip).Property(t => t.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        trip.Status = TripStatuses.Cancelled;
        trip.CancelReason = request.Reason.Trim();
        trip.HeldFromStatus = null;
        events.Record(tripId, TripEventTypes.Cancelled, TripAccess.IsOwnDriver(trip, scope) ? TripEventSources.DriverApp : TripEventSources.Manual, caller.UserId, remarks: trip.CancelReason);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(tripId, ct); }
        return await trips.GetAsync(tripId, ct);
    }

    public async Task<TripModel> ReopenAsync(long tripId, ReopenTripRequest request, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await Find(tripId, ct);
        if (trip.Status != TripStatuses.Completed) throw new ConflictException("Only a Completed trip can be reopened.");
        if (!caller.Has(PermissionCodes.TRP_TRIP_REOPEN)) throw new ForbiddenException("Reopening a completed trip needs the Trip.Reopen permission (Admin, Fleet Manager).");
        // §24: "Only if not on an active invoice." No Invoice table exists yet (a later CC task) — trip.InvoiceId
        // is always null today, so this check is real but currently always passes; it starts refusing the moment
        // invoicing sets InvoiceId.
        if (trip.InvoiceId is not null) throw new ConflictException("This trip is on an active invoice and cannot be reopened.");
        RequireRowVersion(request.RowVersion);

        try { db.Entry(trip).Property(t => t.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        trip.Status = TripStatuses.Delivered;
        trip.CompletionDate = null;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(tripId, ct); }
        return await trips.GetAsync(tripId, ct);
    }

    public async Task<TripModel> InactivateAsync(long tripId, ChangeTripActiveRequest request, CancellationToken ct = default)
    {
        var trip = await Find(tripId, ct);
        if (!trip.IsActive) throw new ConflictException("This trip is already Inactive.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Reason")));
        RequireRowVersion(request.RowVersion);

        try { db.Entry(trip).Property(t => t.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        // §24: "If the trip is already on an invoice, inactivation is allowed but the invoice line is untouched."
        // No Invoice table exists yet — nothing more to do here regardless of InvoiceId's value.
        trip.IsActive = false;
        trip.InactiveReason = request.Reason!.Trim();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(tripId, ct); }
        return await trips.GetAsync(tripId, ct);
    }

    public async Task<TripModel> ReactivateAsync(long tripId, ChangeTripActiveRequest request, CancellationToken ct = default)
    {
        var trip = await Find(tripId, ct);
        if (trip.IsActive) throw new ConflictException("This trip is already Active.");
        // §24: "Reactivation is allowed if the trip is not on another active invoice." Always true today.
        if (trip.InvoiceId is not null) throw new ConflictException("This trip is on another active invoice and cannot be reactivated.");
        RequireRowVersion(request.RowVersion);

        try { db.Entry(trip).Property(t => t.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        trip.IsActive = true;
        trip.InactiveReason = null;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(tripId, ct); }
        return await trips.GetAsync(tripId, ct);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private async Task<Trip> Find(long tripId, CancellationToken ct) =>
        await db.Trips.FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct)
        ?? throw new NotFoundException($"Trip {tripId} was not found.");

    private async Task<Exception> StaleAsync(long tripId, CancellationToken ct)
    {
        var last = await db.Set<VMS.Shared.Auditing.AuditEntry>().AsNoTracking()
            .Where(a => a.TenantId == tenant.TenantId && a.RootEntity == "Trip" && a.RootRecordId == tripId.ToString())
            .OrderByDescending(a => a.AuditEntryID).FirstOrDefaultAsync(ct);
        return new ConcurrencyConflictException(messages.Text(Msg.ChangedByAnother, ("User", last?.UserName ?? "another user"),
            ("At", last is null ? "just now" : $"{last.OccurredAt:yyyy-MM-dd HH:mm} UTC")));
    }

    private void RequireRowVersion(string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) throw new ValidationException(messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));
    }

    /// <summary>Every normal transition, Hold and Resume need either the back-office status permission or to be
    /// the trip's own assigned driver (§24's "Driver (own)" — approximated here as "any of their own trip's
    /// transitions", not FSD's exact per-row driver eligibility, an accepted simplification).</summary>
    private void RequireStatusPermission(Trip trip, TripCaller caller)
    {
        if (caller.Has(PermissionCodes.TRP_TRIP_STATUS)) return;
        if (trip.DriverId is not null && scope.LinkedPartnerId == trip.DriverId) return;
        throw new ForbiddenException("Changing this trip's status needs the Trip.Status permission, or being its own assigned driver.");
    }

    private static BusinessRuleException InvalidTransition(string from, string to) =>
        new("INVALID_TRIP_TRANSITION", $"A trip cannot move from {from} to {to} directly.",
            [new BusinessRuleDetail("status", to, $"Not an allowed transition from {from}.")]);
}
