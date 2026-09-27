using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Partners;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Trips.Services;

/// <summary>§43/§47.2: "Operations/Fleet see driver-created trips in a Pending review queue on the Trip Desk and
/// release them to Assigned (or reject with reason)." A driver-created trip sits at Draft with
/// <see cref="TripEventSources.DriverApp"/> until reviewed — never a separate <c>PendingReview</c> status, the
/// same "the queue is a filter, not a status" reading CC-43's own Trip Desk work already settled on.</summary>
public interface ITripReviewService
{
    Task<IReadOnlyList<TripPendingReviewItem>> PendingReviewAsync(CancellationToken ct = default);
    Task<TripModel> ReleaseAsync(long tripId, ReleaseTripRequest request, int userId, CancellationToken ct = default);
    Task<TripModel> RejectAsync(long tripId, RejectTripRequest request, int userId, CancellationToken ct = default);
}

internal sealed class TripReviewService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, ITripService trips,
    ITripEventRecorder events, IVehicleDirectory vehicles, IPartnerDirectory partners) : ITripReviewService
{
    public async Task<IReadOnlyList<TripPendingReviewItem>> PendingReviewAsync(CancellationToken ct = default)
    {
        var rows = await db.Trips.AsNoTracking()
            .Where(t => t.TenantId == tenant.TenantId && t.Status == TripStatuses.Draft && t.Source == TripEventSources.DriverApp)
            .OrderBy(t => t.TripDate).ToListAsync(ct);
        if (rows.Count == 0) return [];

        var vehicleMap = await vehicles.FindManyAsync(rows.Select(t => t.VehicleId).Distinct(), ct);
        var driverIds = rows.Where(t => t.DriverId is not null).Select(t => t.DriverId!.Value).Distinct();
        var driverMap = await partners.FindManyAsync(driverIds, ct);
        var customerIds = rows.Select(t => t.CustomerId).Distinct().ToList();
        var customerMap = await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && customerIds.Contains(c.CustomerId)).ToDictionaryAsync(c => c.CustomerId, ct);

        return rows.Select(t => new TripPendingReviewItem
        {
            TripId = t.TripId, TripNumber = t.TripNumber, TripType = t.TripType, CustomerId = t.CustomerId,
            CustomerName = customerMap.TryGetValue(t.CustomerId, out var customer) ? customer.CustomerName : string.Empty,
            VehicleId = t.VehicleId, VehicleRegistrationNo = vehicleMap.TryGetValue(t.VehicleId, out var vehicle) ? vehicle.RegistrationNo : string.Empty,
            DriverId = t.DriverId, DriverName = t.DriverId is { } driverId && driverMap.TryGetValue(driverId, out var driver) ? driver.DisplayName : null,
            TripDate = t.TripDate, RateMissing = t.RateMissing, RowVersion = Convert.ToBase64String(t.RowVersion),
        }).ToList();
    }

    public async Task<TripModel> ReleaseAsync(long tripId, ReleaseTripRequest request, int userId, CancellationToken ct = default)
    {
        var trip = await FindPendingAsync(tripId, ct);
        RequireRowVersion(request.RowVersion);
        try { db.Entry(trip).Property(t => t.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        if (trip.DriverId is null) throw new ValidationException(messages.Error("driverId", Msg.Required, ("Field", "Driver (required before a trip can be Assigned)")));

        // §43: "Operations enters the trip amount when reviewing" — only ever meaningful for a trip still
        // missing one (in practice, an Open trip the driver created without an amount).
        if (request.TripAmount is { } amount)
        {
            if (amount <= 0) throw new ValidationException(messages.Error("tripAmount", Msg.Min, ("Field", "Trip amount"), ("Min", "0.01")));
            trip.TripAmount = amount;
            trip.RateMissing = false;
            trip.RateSource = TripRateSources.Manual;
        }
        if (trip.RateMissing)
            throw new BusinessRuleException("RATE_MISSING", "This trip still has no rate or amount and cannot be released.",
                [new BusinessRuleDetail("tripAmount", null, "Provide a trip amount before releasing an Open trip; a Fixed trip needs a rate configured for this date.")]);

        trip.Status = TripStatuses.Assigned;
        events.Record(tripId, TripEventTypes.ForStatus(TripStatuses.Assigned), TripEventSources.Manual, userId, remarks: "Released from Pending review.");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(tripId, ct); }
        return await trips.GetAsync(tripId, ct);
    }

    public async Task<TripModel> RejectAsync(long tripId, RejectTripRequest request, int userId, CancellationToken ct = default)
    {
        var trip = await FindPendingAsync(tripId, ct);
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Rejection reason")));
        RequireRowVersion(request.RowVersion);
        try { db.Entry(trip).Property(t => t.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        trip.Status = TripStatuses.Cancelled;
        trip.CancelReason = request.Reason.Trim();
        events.Record(tripId, TripEventTypes.Cancelled, TripEventSources.Manual, userId, remarks: $"Rejected on review: {trip.CancelReason}");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(tripId, ct); }
        return await trips.GetAsync(tripId, ct);
    }

    private async Task<Trip> FindPendingAsync(long tripId, CancellationToken ct)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct)
            ?? throw new NotFoundException($"Trip {tripId} was not found.");
        if (trip.Status != TripStatuses.Draft || trip.Source != TripEventSources.DriverApp)
            throw new ConflictException("This trip is not awaiting review.");
        return trip;
    }

    private void RequireRowVersion(string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) throw new ValidationException(messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));
    }

    private async Task<Exception> StaleAsync(long tripId, CancellationToken ct)
    {
        var last = await db.Set<VMS.Shared.Auditing.AuditEntry>().AsNoTracking()
            .Where(a => a.TenantId == tenant.TenantId && a.RootEntity == "Trip" && a.RootRecordId == tripId.ToString())
            .OrderByDescending(a => a.AuditEntryID).FirstOrDefaultAsync(ct);
        return new ConcurrencyConflictException(messages.Text(Msg.ChangedByAnother, ("User", last?.UserName ?? "another user"),
            ("At", last is null ? "just now" : $"{last.OccurredAt:yyyy-MM-dd HH:mm} UTC")));
    }
}
