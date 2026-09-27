using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Time;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Trips.Services;

/// <summary>
/// §43's own driver-scoped facade (§47.2: "GET /api/driver/trips · GET /api/driver/trips/{id} · POST
/// /api/driver/trips/{id}/steps · /fuel · /expenses · /issues · /pod · POST /api/driver/sync"). Every action
/// here that touches an existing trip's own status/fuel/expense/issue delegates to the SAME already-shipped,
/// already-tested service every back-office screen uses (<see cref="ITripLifecycleService"/>,
/// <see cref="ITripFuelService"/>, <see cref="ITripExpenseService"/>, <see cref="ITripIssueService"/>) — each of
/// those already grants the trip's own assigned driver access with no back-office permission at all
/// (<c>TripAccess.IsOwnDriver</c>, CC-16). This facade's own job is narrower than it looks: enforce "any request
/// for another driver's trip returns 404" (§44 — the underlying services throw 403 instead, correct for the
/// back-office channel but not this one), strip amounts/rates from what comes back (§43: "never rates, trip
/// amounts, invoices... or other drivers' data"), and group "My Trips" the way the app's own screen wants it.
/// </summary>
public interface IDriverTripService
{
    Task<DriverTripOptionsModel> TripOptionsAsync(int driverId, CancellationToken ct = default);
    /// <summary>Returns the same amounts/rates-stripped shape everything else here does — §43's own "rate
    /// resolved in the background (hidden from driver)" applies to the response too, not just to later reads.</summary>
    Task<DriverTripDetailModel> CreateFixedAsync(CreateDriverFixedTripRequest request, int driverId, int userId, CancellationToken ct = default);
    Task<DriverTripDetailModel> CreateOpenAsync(CreateDriverOpenTripRequest request, int driverId, int userId, CancellationToken ct = default);
    Task<DriverTripsModel> MyTripsAsync(int driverId, CancellationToken ct = default);
    Task<DriverTripDetailModel> GetAsync(long tripId, int driverId, CancellationToken ct = default);
    Task<DriverTripDetailModel> StepAsync(long tripId, string toStatus, TransitionTripRequest request, int driverId, TripCaller caller, CancellationToken ct = default);
    Task<TripFuelModel> AddFuelAsync(long tripId, CreateTripFuelRequest request, int driverId, TripCaller caller, CancellationToken ct = default);
    Task<TripExpenseModel> AddExpenseAsync(long tripId, CreateTripExpenseRequest request, int driverId, TripCaller caller, CancellationToken ct = default);
    Task<TripIssueModel> AddIssueAsync(long tripId, CreateTripIssueRequest request, int driverId, TripCaller caller, CancellationToken ct = default);
    /// <summary>§43 "Offline": replays a batch of queued actions, each already carrying its own
    /// <c>ClientEventId</c> — a failure on one item never aborts the rest (AC-54's own "syncs once... on retry,"
    /// not "syncs all-or-nothing").</summary>
    Task<DriverSyncResultModel> SyncAsync(DriverSyncRequest request, int driverId, TripCaller caller, CancellationToken ct = default);
}

public sealed class DriverSyncRequest
{
    public List<DriverSyncAction> Actions { get; set; } = [];
}

internal sealed class DriverTripService(
    TripsDbContext db, ITenantContext tenant, IOperatingClock clock, IVehicleDirectory vehicles, ITripService trips,
    ITripLifecycleService lifecycle, ITripFuelService fuel, ITripExpenseService expenses, ITripIssueService issues, IMessageCatalogue messages,
    Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> jsonOptions)
    : IDriverTripService
{
    // §43's own sync payload deserialization must read the same casing the rest of the API's JSON already uses
    // (camelCase) — the same "don't hand-roll a second JSON settings object" lesson CC-29's own IdempotencyStore
    // fix already established, not System.Text.Json's own case-sensitive-by-default options.
    private JsonSerializerOptions Json => jsonOptions.Value.JsonSerializerOptions;

    public async Task<DriverTripOptionsModel> TripOptionsAsync(int driverId, CancellationToken ct = default)
    {
        var vehicleId = await OwnVehicleIdAsync(driverId, ct);
        var vehicle = await vehicles.FindAsync(vehicleId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // §43: "Choose customer (only customers with configurations that allow the driver's vehicle)."
        var configIds = await db.TripConfigurationVehicles.AsNoTracking()
            .Where(v => v.TenantId == tenant.TenantId && v.VehicleId == vehicleId && v.Status == ActiveInactiveStatuses.Active
                && v.EffectiveFrom <= today && (v.EffectiveTo == null || v.EffectiveTo >= today))
            .Select(v => v.TripConfigurationId).Distinct().ToListAsync(ct);

        var configRows = configIds.Count == 0 ? []
            : await db.TripConfigurations.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && configIds.Contains(c.TripConfigurationId) && c.Status == TripConfigurationStatuses.Active)
                .ToListAsync(ct);

        var routeIds = configRows.Select(c => c.RouteId).Distinct().ToList();
        var routeLabels = await RouteLabelsAsync(routeIds, ct);
        var customerIds = configRows.Select(c => c.CustomerId).Distinct().ToList();
        var customers = customerIds.Count == 0 ? []
            : await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && customerIds.Contains(c.CustomerId) && c.Status == CustomerStatuses.Active).ToListAsync(ct);

        var byCustomer = customers.Select(c => new DriverCustomerOptionModel
        {
            CustomerId = c.CustomerId, CustomerName = c.CustomerName,
            Configurations = configRows.Where(cfg => cfg.CustomerId == c.CustomerId)
                .Select(cfg => new DriverConfigurationOptionModel { TripConfigurationId = cfg.TripConfigurationId, Name = cfg.Name, RouteLabel = routeLabels.GetValueOrDefault(cfg.RouteId) })
                .ToList(),
        }).Where(c => c.Configurations.Count > 0).ToList();

        var cities = await db.Cities.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && c.Status == ActiveInactiveStatuses.Active)
            .Select(c => new DriverCityOptionModel { CityId = c.CityId, CityName = c.CityName, Abbreviation = c.Abbreviation }).ToListAsync(ct);

        return new DriverTripOptionsModel { VehicleId = vehicleId, VehicleRegistrationNo = vehicle?.RegistrationNo ?? string.Empty, Customers = byCustomer, Cities = cities };
    }

    public async Task<DriverTripDetailModel> CreateFixedAsync(CreateDriverFixedTripRequest request, int driverId, int userId, CancellationToken ct = default)
    {
        var created = await trips.CreateDriverFixedTripAsync(request, driverId, userId, ct);
        return await ToDetailAsync(await RequireOwnTripAsync(created.TripId, driverId, ct), ct);
    }

    public async Task<DriverTripDetailModel> CreateOpenAsync(CreateDriverOpenTripRequest request, int driverId, int userId, CancellationToken ct = default)
    {
        var created = await trips.CreateDriverOpenTripAsync(request, driverId, userId, ct);
        return await ToDetailAsync(await RequireOwnTripAsync(created.TripId, driverId, ct), ct);
    }

    public async Task<DriverTripsModel> MyTripsAsync(int driverId, CancellationToken ct = default)
    {
        var today = await clock.TodayAsync(tenant.TenantId);
        var recentFrom = today.AddDays(-7);

        // §43: "Drivers see only trips where they are the assigned driver, only from Assigned status onwards" —
        // Draft and Planned (the two statuses before Assigned in §24's own chain) are never shown here at all.
        var rows = await db.Trips.AsNoTracking()
            .Where(t => t.TenantId == tenant.TenantId && t.DriverId == driverId && t.Status != TripStatuses.Draft && t.Status != TripStatuses.Planned
                && t.TripDate >= recentFrom)
            .OrderBy(t => t.TripDate).ToListAsync(ct);
        if (rows.Count == 0) return new DriverTripsModel();

        var items = await ToListItemsAsync(rows, ct);
        var withDates = rows.Zip(items, (t, i) => (t.TripDate, Item: i)).ToList();
        return new DriverTripsModel
        {
            Today = withDates.Where(x => x.TripDate == today).Select(x => x.Item).ToList(),
            Upcoming = withDates.Where(x => x.TripDate > today).Select(x => x.Item).ToList(),
            Recent = withDates.Where(x => x.TripDate < today).Select(x => x.Item).ToList(),
        };
    }

    public async Task<DriverTripDetailModel> GetAsync(long tripId, int driverId, CancellationToken ct = default)
    {
        var trip = await RequireOwnTripAsync(tripId, driverId, ct);
        return await ToDetailAsync(trip, ct);
    }

    public async Task<DriverTripDetailModel> StepAsync(long tripId, string toStatus, TransitionTripRequest request, int driverId, TripCaller caller, CancellationToken ct = default)
    {
        await RequireOwnTripAsync(tripId, driverId, ct);
        await lifecycle.TransitionAsync(tripId, toStatus, request, caller, ct);
        var reloaded = await RequireOwnTripAsync(tripId, driverId, ct);
        return await ToDetailAsync(reloaded, ct);
    }

    public async Task<TripFuelModel> AddFuelAsync(long tripId, CreateTripFuelRequest request, int driverId, TripCaller caller, CancellationToken ct = default)
    {
        await RequireOwnTripAsync(tripId, driverId, ct);
        return await fuel.CreateAsync(tripId, request, caller, ct);
    }

    public async Task<TripExpenseModel> AddExpenseAsync(long tripId, CreateTripExpenseRequest request, int driverId, TripCaller caller, CancellationToken ct = default)
    {
        await RequireOwnTripAsync(tripId, driverId, ct);
        return await expenses.CreateAsync(tripId, request, caller, ct);
    }

    public async Task<TripIssueModel> AddIssueAsync(long tripId, CreateTripIssueRequest request, int driverId, TripCaller caller, CancellationToken ct = default)
    {
        await RequireOwnTripAsync(tripId, driverId, ct);
        return await issues.CreateAsync(tripId, request, caller, ct);
    }

    public async Task<DriverSyncResultModel> SyncAsync(DriverSyncRequest request, int driverId, TripCaller caller, CancellationToken ct = default)
    {
        var results = new List<DriverSyncItemResult>();
        foreach (var action in request.Actions)
        {
            try
            {
                await RequireOwnTripAsync(action.TripId, driverId, ct);
                switch (action.ActionType)
                {
                    case DriverSyncActionTypes.Step:
                        if (string.IsNullOrWhiteSpace(action.ToStatus))
                            throw new ValidationException(messages.Error("toStatus", Msg.Required, ("Field", "Status")));
                        var stepRequest = action.Payload.Deserialize<TransitionTripRequest>(Json) ?? new TransitionTripRequest();
                        await lifecycle.TransitionAsync(action.TripId, action.ToStatus, stepRequest, caller, ct);
                        break;
                    case DriverSyncActionTypes.Fuel:
                        await fuel.CreateAsync(action.TripId, action.Payload.Deserialize<CreateTripFuelRequest>(Json) ?? new CreateTripFuelRequest(), caller, ct);
                        break;
                    case DriverSyncActionTypes.Expense:
                        await expenses.CreateAsync(action.TripId, action.Payload.Deserialize<CreateTripExpenseRequest>(Json) ?? new CreateTripExpenseRequest(), caller, ct);
                        break;
                    case DriverSyncActionTypes.Issue:
                        await issues.CreateAsync(action.TripId, action.Payload.Deserialize<CreateTripIssueRequest>(Json) ?? new CreateTripIssueRequest(), caller, ct);
                        break;
                    default:
                        throw new ValidationException(messages.Error("actionType", Msg.OneOf, ("Field", "Action type"), ("Allowed", string.Join(", ", DriverSyncActionTypes.All))));
                }
                results.Add(new DriverSyncItemResult { ActionType = action.ActionType, TripId = action.TripId, Success = true });
            }
            catch (Exception ex) when (ex is ValidationException or BusinessRuleException or NotFoundException or ForbiddenException or ConflictException)
            {
                // §43: "Conflicts... show 'This trip was changed by the office'" — one bad item never aborts the
                // rest of the batch; the caller decides whether to retry this one on the next sync.
                results.Add(new DriverSyncItemResult { ActionType = action.ActionType, TripId = action.TripId, Success = false, Error = ex.Message });
            }
        }
        return new DriverSyncResultModel { Results = results };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    private async Task<int> OwnVehicleIdAsync(int driverId, CancellationToken ct)
    {
        var all = await vehicles.AllAsync(ct);
        var mine = all.Where(v => v.DefaultDriverId == driverId).OrderBy(v => v.Id).ToList();
        if (mine.Count == 0) throw new BusinessRuleException("NO_ASSIGNED_VEHICLE", "You are not the default driver of any vehicle.", []);
        return mine[0].Id;
    }

    /// <summary>§44: "any request for another driver's trip returns 404" — checked here, before this facade ever
    /// delegates to a back-office service (which would otherwise answer 403 for a non-owning caller with no
    /// permission — correct for that channel, wrong for this one).</summary>
    private async Task<Trip> RequireOwnTripAsync(long tripId, int driverId, CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct);
        if (trip is null || trip.DriverId != driverId) throw new NotFoundException($"Trip {tripId} was not found.");
        return trip;
    }

    private async Task<IReadOnlyList<DriverTripListItem>> ToListItemsAsync(IReadOnlyList<Trip> rows, CancellationToken ct)
    {
        var vehicleMap = await vehicles.FindManyAsync(rows.Select(t => t.VehicleId).Distinct(), ct);
        var customerIds = rows.Select(t => t.CustomerId).Distinct().ToList();
        var customerMap = await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && customerIds.Contains(c.CustomerId)).ToDictionaryAsync(c => c.CustomerId, ct);

        var routeIds = rows.Where(t => t.RouteId is not null).Select(t => t.RouteId!.Value).Distinct().ToList();
        var routeLabels = await RouteLabelsAsync(routeIds, ct);

        var openCityIds = rows.SelectMany(t => new[] { t.FromCityId, t.ToCityId }).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var openCities = openCityIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Cities.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && openCityIds.Contains(c.CityId)).ToDictionaryAsync(c => c.CityId, c => c.Abbreviation, ct);

        string? OpenLabel(Trip t)
        {
            if (t.FromLocationType is null) return null;
            var from = t.FromCityId is { } fromCity ? openCities.GetValueOrDefault(fromCity, "?") : (t.FromOtherLocationName ?? "?");
            var to = t.ToCityId is { } toCity ? openCities.GetValueOrDefault(toCity, "?") : (t.ToOtherLocationName ?? "?");
            return $"{from} → {to}";
        }

        return rows.Select(t => new DriverTripListItem
        {
            TripId = t.TripId, TripNumber = t.TripNumber,
            CustomerShortName = customerMap.TryGetValue(t.CustomerId, out var c) ? (c.ShortName ?? c.CustomerName) : string.Empty,
            RouteLabel = t.RouteId is { } routeId ? routeLabels.GetValueOrDefault(routeId) : OpenLabel(t),
            VehicleRegistrationNo = vehicleMap.TryGetValue(t.VehicleId, out var v) ? v.RegistrationNo : string.Empty,
            TripDate = t.TripDate, PlannedStart = t.PlannedStart, Status = t.Status,
        }).ToList();
    }

    private async Task<DriverTripDetailModel> ToDetailAsync(Trip trip, CancellationToken ct)
    {
        var vehicle = await vehicles.FindAsync(trip.VehicleId, ct);
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == trip.CustomerId, ct);
        // Reuses the office model's own already-correct Stops/RouteLabel mapping (Fixed via Route/RouteStop, Open
        // via From/Stops/To) rather than a second copy of that logic here.
        var full = await trips.GetAsync(trip.TripId, ct);

        // Ownership was already proven by the caller (RequireOwnTripAsync) — a plain query is enough here, no
        // second permission check needed.
        var recentEvents = await db.TripEvents.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && e.TripId == trip.TripId)
            .OrderByDescending(e => e.EventDateTime).Take(20)
            .Select(e => new DriverTripEventModel { EventType = e.EventType, OccurredAtUtc = e.EventDateTime, Remarks = e.Remarks }).ToListAsync(ct);

        return new DriverTripDetailModel
        {
            TripId = trip.TripId, TripNumber = trip.TripNumber, TripType = trip.TripType,
            CustomerShortName = customer?.ShortName ?? customer?.CustomerName ?? string.Empty, CustomerTripReference = trip.CustomerTripReference,
            RouteLabel = full.RouteLabel, Stops = full.Stops, VehicleRegistrationNo = vehicle?.RegistrationNo ?? string.Empty,
            TripDate = trip.TripDate, PlannedStart = trip.PlannedStart, StartOdometer = trip.StartOdometer, EndOdometer = trip.EndOdometer,
            Status = trip.Status, HoldReason = trip.HoldReason, RowVersion = Convert.ToBase64String(trip.RowVersion),
            NextStatuses = TripLifecycle.NextStatuses(trip.Status), RecentEvents = recentEvents,
        };
    }

    private async Task<Dictionary<int, string?>> RouteLabelsAsync(IReadOnlyList<int> routeIds, CancellationToken ct)
    {
        if (routeIds.Count == 0) return new Dictionary<int, string?>();
        var stops = await db.RouteStops.AsNoTracking().Where(s => s.TenantId == tenant.TenantId && routeIds.Contains(s.RouteId)).OrderBy(s => s.Sequence).ToListAsync(ct);
        var cityIds = stops.Select(s => s.CityId).Distinct().ToList();
        var cities = cityIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Cities.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && cityIds.Contains(c.CityId)).ToDictionaryAsync(c => c.CityId, c => c.Abbreviation, ct);
        return routeIds.ToDictionary(id => id, id =>
        {
            var own = stops.Where(s => s.RouteId == id).ToList();
            return own.Count == 0 ? (string?)null : string.Join(" → ", own.Select(s => cities.GetValueOrDefault(s.CityId, "?")));
        });
    }
}
