using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;

namespace VMS.Modules.Trips.Services;

/// <summary>Appends one system-authored event to a trip's timeline. Caller still owns <c>SaveChangesAsync</c> —
/// this only adds the entity, so it lands in whichever transaction the calling service (<c>TripService</c>,
/// <c>TripLifecycleService</c>) is already inside, satisfying §25's "status transitions create events
/// automatically" without a second, separate save.</summary>
public interface ITripEventRecorder
{
    void Record(long tripId, string eventType, string source, int userId, string? remarks = null, decimal? odometer = null, long? attachmentId = null);
}

internal sealed class TripEventRecorder(TripsDbContext db) : ITripEventRecorder
{
    public void Record(long tripId, string eventType, string source, int userId, string? remarks = null, decimal? odometer = null, long? attachmentId = null) =>
        db.TripEvents.Add(new TripEvent
        {
            TripId = tripId, EventType = eventType, EventDateTime = DateTime.UtcNow, Odometer = odometer,
            UserId = userId, Remarks = remarks, AttachmentId = attachmentId, Source = source
        });
}

public interface ITripEventService
{
    Task<IReadOnlyList<TripEventModel>> ListAsync(long tripId, TripCaller caller, CancellationToken ct = default);
    Task<TripEventModel> CreateManualAsync(long tripId, CreateTripEventRequest request, TripCaller caller, CancellationToken ct = default);
}

internal sealed class TripEventService(TripsDbContext db, ITenantContext tenant, ICallerScope scope, IMessageCatalogue messages) : ITripEventService
{
    public async Task<IReadOnlyList<TripEventModel>> ListAsync(long tripId, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await FindTripAsync(tripId, ct);
        TripAccess.RequireViewOrOwnDriver(trip, caller, scope, "Viewing this trip's timeline");
        var events = await db.TripEvents.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && e.TripId == tripId)
            .OrderBy(e => e.EventDateTime).ThenBy(e => e.TripEventId).ToListAsync(ct);
        return events.Select(ToModel).ToList();
    }

    public async Task<TripEventModel> CreateManualAsync(long tripId, CreateTripEventRequest request, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await FindTripAsync(tripId, ct);
        TripAccess.RequireStatusOrOwnDriver(trip, caller, scope, "Adding a trip event");

        if (!TripEventTypes.ManuallyCreatable.Contains(request.EventType))
            throw new ValidationException(messages.Error("eventType", Msg.OneOf, ("Field", "Event type"), ("Allowed", string.Join(", ", TripEventTypes.ManuallyCreatable))));

        var source = request.Source ?? (TripAccess.IsOwnDriver(trip, scope) ? TripEventSources.DriverApp : TripEventSources.Manual);
        if (source is not (TripEventSources.Manual or TripEventSources.DriverApp))
            throw new ValidationException(messages.Error("source", Msg.OneOf, ("Field", "Source"), ("Allowed", $"{TripEventSources.Manual}, {TripEventSources.DriverApp}")));

        var when = request.EventDateTime ?? DateTime.UtcNow;
        if (when > DateTime.UtcNow.AddMinutes(10))
            throw new ValidationException(messages.Error("eventDateTime", Msg.Invalid, ("Field", "Event time (more than 10 minutes in the future)")));

        // AC-54: a retried offline sync with the same ClientEventId is answered with the original event, not a duplicate.
        if (request.ClientEventId is { } clientId)
        {
            var existing = await db.TripEvents.AsNoTracking()
                .FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId && e.TripId == tripId && e.ClientEventId == clientId, ct);
            if (existing is not null) return ToModel(existing);
        }

        var entity = new TripEvent
        {
            TripId = tripId, EventType = request.EventType, EventDateTime = when, CityId = request.CityId, LocationText = Trim(request.LocationText),
            Latitude = request.Latitude, Longitude = request.Longitude, Odometer = request.Odometer, UserId = caller.UserId,
            Remarks = Trim(request.Remarks), AttachmentId = request.AttachmentId, Source = source, ClientEventId = request.ClientEventId
        };
        db.TripEvents.Add(entity);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } && request.ClientEventId is not null)
        {
            // Another request with the same ClientEventId won the race — the DB's own unique index is the backstop
            // for the app-level check above (the same idiom RecurringChargeGenerator/NotificationEvaluator use).
            db.Entry(entity).State = EntityState.Detached;
            var winner = await db.TripEvents.AsNoTracking()
                .FirstAsync(e => e.TenantId == tenant.TenantId && e.TripId == tripId && e.ClientEventId == request.ClientEventId, ct);
            return ToModel(winner);
        }
        return ToModel(entity);
    }

    private async Task<Trip> FindTripAsync(long tripId, CancellationToken ct) =>
        await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct)
        ?? throw new NotFoundException($"Trip {tripId} was not found.");

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TripEventModel ToModel(TripEvent e) => new()
    {
        TripEventId = e.TripEventId, TripId = e.TripId, EventType = e.EventType, EventDateTime = e.EventDateTime, CityId = e.CityId,
        LocationText = e.LocationText, Latitude = e.Latitude, Longitude = e.Longitude, Odometer = e.Odometer, UserId = e.UserId,
        Remarks = e.Remarks, AttachmentId = e.AttachmentId, Source = e.Source, ClientEventId = e.ClientEventId
    };
}
