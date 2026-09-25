using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Pagination;
using VMS.Shared.Partners;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Trips.Services;

public interface ITripService
{
    Task<TripModel> GetAsync(long tripId, CancellationToken ct = default);
    Task<TripModel> CreateFixedTripAsync(CreateFixedTripRequest request, TripCaller caller, CancellationToken ct = default);
    Task<TripModel> CreateOpenTripAsync(CreateOpenTripRequest request, TripCaller caller, CancellationToken ct = default);
    Task<TripHistory> HistoryAsync(long tripId, int page, int pageSize, ClaimsPrincipal user, CancellationToken ct = default);
}

internal sealed class TripService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, ITripNumberAllocator numbers,
    ITripRateService rates, IVehicleDirectory vehicles, IPartnerDirectory partners, ICustomerBillingConfigurationService billingConfigurations,
    ITripEventRecorder events) : ITripService
{
    public async Task<TripModel> GetAsync(long tripId, CancellationToken ct = default)
    {
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct)
            ?? throw new NotFoundException($"Trip {tripId} was not found.");
        var stops = trip.TripType == TripTypes.Open
            ? await db.TripStops.AsNoTracking().Where(s => s.TenantId == tenant.TenantId && s.TripId == tripId).OrderBy(s => s.Sequence).ToListAsync(ct)
            : [];
        return await ToModelAsync(trip, stops, ct);
    }

    public async Task<TripModel> CreateFixedTripAsync(CreateFixedTripRequest request, TripCaller caller, CancellationToken ct = default)
    {
        // The FSD's own entry order (§21): Customer → Configuration → Vehicle → Driver → Date → Rate.
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        var customer = await RequireActiveCustomerAsync(request.CustomerId, Add, ct);

        var config = await db.TripConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.TripConfigurationId == request.TripConfigurationId, ct);
        if (config is null) Add("tripConfigurationId", Msg.Invalid, ("Field", "Trip configuration"));
        else
        {
            if (config.CustomerId != request.CustomerId) Add("tripConfigurationId", Msg.Invalid, ("Field", "Trip configuration (belongs to a different customer)"));
            if (config.Status != TripConfigurationStatuses.Active) Add("tripConfigurationId", Msg.Invalid, ("Field", "Trip configuration (not Active)"));
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        // Here a vehicle only needs to still be *allowed on this configuration on the Trip Date* (§19's own
        // assignment already proved it was operational when assigned) — not re-checked for operational status.
        var allowed = await db.TripConfigurationVehicles.AnyAsync(v => v.TenantId == tenant.TenantId && v.TripConfigurationId == request.TripConfigurationId
            && v.VehicleId == request.VehicleId && v.Status == ActiveInactiveStatuses.Active
            && v.EffectiveFrom <= request.TripDate && (v.EffectiveTo == null || v.EffectiveTo >= request.TripDate), ct);
        if (!allowed) Add("vehicleId", Msg.Invalid, ("Field", "Vehicle (not allowed on this configuration for the trip date)"));

        var driver = await ResolveDriverAsync(request.VehicleId, request.DriverId, request.DriverOverrideReason, caller, Add, ct);
        var (reference, warnings) = await ValidateReferenceAsync(request.CustomerId, request.CustomerTripReference, Add, ct);
        if (errors.Count > 0) throw new ValidationException(errors);
        warnings.AddRange(await SoftDriverClashWarningsAsync(driver.DriverId, ct));

        var resolution = await rates.ResolveAsync(request.CustomerId, request.TripConfigurationId, request.TripDate, ct);

        var trip = new Trip
        {
            TripType = TripTypes.Fixed, CustomerId = request.CustomerId, CustomerTripReference = reference,
            TripConfigurationId = request.TripConfigurationId, RouteId = config!.RouteId, VehicleId = request.VehicleId,
            DriverId = driver.DriverId, DefaultDriverId = driver.DefaultDriverId, IsDriverOverridden = driver.IsOverridden, DriverOverrideReason = driver.OverrideReason,
            TripDate = request.TripDate, PlannedStart = request.PlannedStart, Status = TripStatuses.Draft, IsActive = true,
            Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim()
        };

        if (resolution.Found)
        {
            trip.TripRateId = resolution.TripRateId;
            trip.TripRateAmount = resolution.RateAmount;
            trip.RateEffectiveFrom = resolution.EffectiveFrom;
            trip.RateEffectiveTo = resolution.EffectiveTo;
            trip.RateSource = TripRateSources.Configured;
            trip.TripAmount = resolution.RateAmount;
            trip.CurrencyCode = resolution.CurrencyCode;
            trip.RateMissing = false;
        }
        else
        {
            // §26: "Never fall back to previous, next, average or zero rate." The trip is still saved.
            trip.RateSource = TripRateSources.Missing;
            trip.RateMissing = true;
        }

        await SaveNewTripAsync(trip, caller, ct);
        var model = await ToModelAsync(trip, [], ct);
        model.Warnings = warnings;
        return model;
    }

    public async Task<TripModel> CreateOpenTripAsync(CreateOpenTripRequest request, TripCaller caller, CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        await RequireActiveCustomerAsync(request.CustomerId, Add, ct);
        await ValidateLocationAsync("from", request.From, Add, ct);
        await ValidateLocationAsync("to", request.To, Add, ct);
        for (var i = 0; i < request.Stops.Count; i++) await ValidateLocationAsync($"stops[{i}]", request.Stops[i], Add, ct);
        if (!request.IsRoundTrip && LocationsMatch(request.From, request.To)) Add("to", Msg.Invalid, ("Field", "From and To must differ unless the trip is a round trip"));

        var vehicle = await vehicles.FindAsync(request.VehicleId, ct);
        if (vehicle is null) Add("vehicleId", Msg.Invalid, ("Field", "Vehicle"));
        else if (!vehicle.IsOperational) throw new BusinessRuleException("VEHICLE_NOT_OPERATIONAL", $"{vehicle.RegistrationNo} is not operational.",
            [new BusinessRuleDetail("vehicleId", request.VehicleId, "Any operational vehicle may be used on an open trip (§22).")]);

        var driver = await ResolveDriverAsync(request.VehicleId, request.DriverId, request.DriverOverrideReason, caller, Add, ct);
        if (request.TripAmount <= 0) Add("tripAmount", Msg.Min, ("Field", "Trip amount"), ("Min", "0.01"));

        var (reference, warnings) = await ValidateReferenceAsync(request.CustomerId, request.CustomerTripReference, Add, ct);
        if (errors.Count > 0) throw new ValidationException(errors);
        warnings.AddRange(await SoftDriverClashWarningsAsync(driver.DriverId, ct));

        var trip = new Trip
        {
            TripType = TripTypes.Open, CustomerId = request.CustomerId, CustomerTripReference = reference, VehicleId = request.VehicleId,
            DriverId = driver.DriverId, DefaultDriverId = driver.DefaultDriverId, IsDriverOverridden = driver.IsOverridden, DriverOverrideReason = driver.OverrideReason,
            TripDate = request.TripDate, PlannedStart = request.PlannedStart, Status = TripStatuses.Draft, IsActive = true,
            RateSource = TripRateSources.Manual, TripAmount = request.TripAmount, IsRoundTrip = request.IsRoundTrip,
            FromLocationType = request.From.LocationType, FromCityId = request.From.CityId, FromOtherLocationType = request.From.OtherLocationType,
            FromOtherLocationName = Trim(request.From.OtherLocationName), FromOtherNearestCityId = request.From.OtherNearestCityId,
            ToLocationType = request.To.LocationType, ToCityId = request.To.CityId, ToOtherLocationType = request.To.OtherLocationType,
            ToOtherLocationName = Trim(request.To.OtherLocationName), ToOtherNearestCityId = request.To.OtherNearestCityId,
            Remarks = Trim(request.Remarks)
        };

        var stopRows = request.Stops.Select((s, i) => new TripStop
        {
            Sequence = i + 1, LocationType = s.LocationType, CityId = s.CityId, OtherLocationType = s.OtherLocationType,
            OtherLocationName = Trim(s.OtherLocationName), OtherNearestCityId = s.OtherNearestCityId
        }).ToList();

        await db.InTransactionAsync(async ct2 =>
        {
            trip.TripNumber = await numbers.NextAsync(ct2);
            db.Trips.Add(trip);
            await db.SaveChangesAsync(ct2);
            events.Record(trip.TripId, TripEventTypes.Created, TripEventSources.Manual, caller.UserId);
            foreach (var stop in stopRows) stop.TripId = trip.TripId;
            db.TripStops.AddRange(stopRows);
            await db.SaveChangesAsync(ct2);
        }, ct);

        var model = await ToModelAsync(trip, stopRows, ct);
        model.Warnings = warnings;
        return model;
    }

    /// <summary>§48.1's own History tab — mirrors <c>CustomerService.HistoryAsync</c> exactly. Every child entity
    /// this module has built for a trip (events, stops, fuel, expenses, income, documents, POD, issues, rate
    /// history) roots its own audit rows back to "Trip" (see each one's <c>GetAuditRoot()</c>), so one query here
    /// already shows the whole trip's story, not just changes to the <see cref="Trip"/> row itself.</summary>
    public async Task<TripHistory> HistoryAsync(long tripId, int page, int pageSize, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var exists = await db.Trips.AsNoTracking().AnyAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct);
        if (!exists) throw new NotFoundException($"Trip {tripId} was not found.");
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 50 : Math.Min(pageSize, 100);
        var root = tripId.ToString();

        var changes = db.Set<AuditEntry>().AsNoTracking().Where(a => a.TenantId == tenant.TenantId && a.RootEntity == "Trip" && a.RootRecordId == root);
        var total = await changes.CountAsync(ct);
        var rows = await changes.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.AuditEntryID)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var items = rows.Select(a =>
        {
            var hidden = a.RequiredPermission is { } permission && !user.HasPermission(permission);
            return new TripHistoryChange
            {
                Id = a.AuditEntryID, OccurredAt = a.OccurredAt, GroupId = a.GroupId, UserName = a.UserName, Entity = a.Entity, RecordId = a.RecordId,
                Action = a.Action, Field = a.Field, Reason = a.Reason, Restricted = hidden, OldValue = hidden ? null : a.OldValue, NewValue = hidden ? null : a.NewValue
            };
        }).ToList();

        return new TripHistory { Changes = new PaginatedResponse<TripHistoryChange> { Items = items, TotalCount = total, Page = page, PageSize = pageSize } };
    }

    // ── Shared validation ────────────────────────────────────────────────────────────

    private async Task<Customer?> RequireActiveCustomerAsync(int customerId, Action<string, string, (string, object?)[]> add, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId, ct);
        if (customer is null) { add("customerId", Msg.Invalid, [("Field", "Customer")]); return null; }
        if (customer.Status != CustomerStatuses.Active)
            throw new BusinessRuleException("CUSTOMER_INACTIVE", "Customer is inactive.", [new BusinessRuleDetail("customerId", customerId, "Only an Active customer can have new trips.")]);
        return customer;
    }

    private sealed record ResolvedDriver(int? DriverId, int? DefaultDriverId, bool IsOverridden, string? OverrideReason);

    /// <summary>
    /// §20's own algorithm: look up the vehicle's default driver, propose it, and only call it an "override" (needing
    /// <see cref="PermissionCodes.TRP_TRIP_OVERRIDE_DRIVER"/> and a reason) when the caller picks something else
    /// *and* the vehicle actually had a default to differ from — providing any driver when there was never a
    /// default proposed is just the ordinary case, not an override. Driver may end up null (§20 rule 3: "if none
    /// exists, Driver is empty and required before the trip moves to Assigned" — CC-15's own gate, not this one's).
    /// </summary>
    private async Task<ResolvedDriver> ResolveDriverAsync(int vehicleId, int? requestedDriverId, string? overrideReason,
        TripCaller caller, Action<string, string, (string, object?)[]> add, CancellationToken ct)
    {
        var vehicle = await vehicles.FindAsync(vehicleId, ct);
        var defaultDriverId = vehicle?.DefaultDriverId;
        var driverId = requestedDriverId ?? defaultDriverId;

        if (driverId is not null)
        {
            var driver = await partners.FindAsync(driverId.Value, ct);
            if (driver is null || !driver.IsAvailable) add("driverId", Msg.Invalid, [("Field", "Driver")]);
            else if (!driver.HasRole(PartnerRoleCodes.Driver)) add("driverId", Msg.Invalid, [("Field", "Driver (does not hold the Driver role)")]);
        }

        var isOverridden = defaultDriverId is not null && requestedDriverId is not null && requestedDriverId != defaultDriverId;
        if (isOverridden)
        {
            if (!caller.Has(PermissionCodes.TRP_TRIP_OVERRIDE_DRIVER))
                throw new ForbiddenException("Only an authorised user may override the vehicle's own default driver (§20).");
            if (string.IsNullOrWhiteSpace(overrideReason)) add("driverOverrideReason", Msg.Required, [("Field", "Override reason")]);
        }

        return new ResolvedDriver(driverId, defaultDriverId, isOverridden, isOverridden ? overrideReason?.Trim() : null);
    }

    /// <summary>§20: "a soft warning is shown if the same driver has another trip in Started/In Transit status"
    /// (Recommended Design) — never blocks. No trip can reach either status yet (CC-15, the lifecycle task, hasn't
    /// landed), so this always returns empty today; it starts finding real clashes the moment CC-15 does.</summary>
    private async Task<IReadOnlyList<string>> SoftDriverClashWarningsAsync(int? driverId, CancellationToken ct)
    {
        if (driverId is null) return [];
        var clashing = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && t.DriverId == driverId
            && (t.Status == TripStatuses.Started || t.Status == TripStatuses.InTransit))
            .Select(t => t.TripNumber).ToListAsync(ct);
        return clashing.Count == 0 ? [] : [$"This driver already has another trip in progress: {string.Join(", ", clashing)}."];
    }

    /// <summary>§23: the reference duplicate-behaviour check, shared by Fixed and Open trips alike. Reads through
    /// the billing-configuration *service*, not the raw table, so a customer whose Billing Configuration tab was
    /// never opened still gets its lazily-created default (§13) instead of silently skipping this check.</summary>
    private async Task<(string? Reference, List<string> Warnings)> ValidateReferenceAsync(
        int customerId, string? requested, Action<string, string, (string, object?)[]> add, CancellationToken ct)
    {
        var warnings = new List<string>();
        var reference = string.IsNullOrWhiteSpace(requested) ? null : requested.Trim();
        var billingConfig = await billingConfigurations.GetCurrentAsync(customerId, ct);

        if (billingConfig.CustomerReferenceRequired && reference is null) add("customerTripReference", Msg.Required, [("Field", "Customer trip reference")]);
        else if (reference is not null && billingConfig.DuplicateReferenceBehaviour != DuplicateReferenceBehaviours.Allow)
        {
            var duplicate = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && t.CustomerId == customerId
                && t.CustomerTripReference != null && t.CustomerTripReference.ToUpper() == reference.ToUpper())
                .Select(t => t.TripNumber).FirstOrDefaultAsync(ct);
            if (duplicate is not null)
            {
                if (billingConfig.DuplicateReferenceBehaviour == DuplicateReferenceBehaviours.Block)
                    add("customerTripReference", Msg.AlreadyUsed, [("Field", "customer trip reference"), ("Code", reference), ("Name", duplicate)]);
                else warnings.Add($"This customer reference is already used on {duplicate}. Continue?");
            }
        }
        return (reference, warnings);
    }

    private async Task ValidateLocationAsync(string field, LocationInput location, Action<string, string, (string, object?)[]> add, CancellationToken ct)
    {
        if (!TripLocationTypes.All.Contains(location.LocationType))
        {
            add($"{field}.locationType", Msg.OneOf, [("Field", "Location type"), ("Allowed", string.Join(", ", TripLocationTypes.All))]);
            return;
        }
        if (location.LocationType == TripLocationTypes.City)
        {
            if (location.CityId is not { } cityId) add($"{field}.cityId", Msg.Required, [("Field", "City")]);
            else if (!await db.Cities.AnyAsync(c => c.TenantId == tenant.TenantId && c.CityId == cityId && c.Status == ActiveInactiveStatuses.Active, ct))
                add($"{field}.cityId", Msg.Invalid, [("Field", "City (unknown or inactive)")]);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(location.OtherLocationName)) add($"{field}.otherLocationName", Msg.Required, [("Field", "Location name")]);
            if (string.IsNullOrWhiteSpace(location.OtherLocationType) || !OtherLocationTypes.All.Contains(location.OtherLocationType))
                add($"{field}.otherLocationType", Msg.OneOf, [("Field", "Location type"), ("Allowed", string.Join(", ", OtherLocationTypes.All))]);
            if (location.OtherNearestCityId is { } nearestId
                && !await db.Cities.AnyAsync(c => c.TenantId == tenant.TenantId && c.CityId == nearestId, ct))
                add($"{field}.otherNearestCityId", Msg.Invalid, [("Field", "Nearest city")]);
        }
    }

    private static bool LocationsMatch(LocationInput a, LocationInput b)
    {
        if (a.LocationType != b.LocationType) return false;
        return a.LocationType == TripLocationTypes.City
            ? a.CityId == b.CityId
            : string.Equals((a.OtherLocationName ?? string.Empty).Trim(), (b.OtherLocationName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task SaveNewTripAsync(Trip trip, TripCaller caller, CancellationToken ct) =>
        await db.InTransactionAsync(async ct2 =>
        {
            trip.TripNumber = await numbers.NextAsync(ct2);
            db.Trips.Add(trip);
            await db.SaveChangesAsync(ct2);
            // §25: every operational step is an event, including the trip's own creation.
            events.Record(trip.TripId, TripEventTypes.Created, TripEventSources.Manual, caller.UserId);
            await db.SaveChangesAsync(ct2);
        }, ct);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ── Mapping ──────────────────────────────────────────────────────────────────────

    private async Task<TripModel> ToModelAsync(Trip t, IReadOnlyList<TripStop> stops, CancellationToken ct)
    {
        var cityIds = new List<int?> { t.FromCityId, t.FromOtherNearestCityId, t.ToCityId, t.ToOtherNearestCityId }
            .Concat(stops.SelectMany(s => new[] { s.CityId, s.OtherNearestCityId })).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var cities = cityIds.Count == 0 ? new Dictionary<int, City>() : await db.Cities.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && cityIds.Contains(c.CityId)).ToDictionaryAsync(c => c.CityId, ct);

        LocationModel? from = t.FromLocationType is null ? null : ToLocation(t.FromLocationType, t.FromCityId, t.FromOtherLocationType, t.FromOtherLocationName, t.FromOtherNearestCityId, cities);
        LocationModel? to = t.ToLocationType is null ? null : ToLocation(t.ToLocationType, t.ToCityId, t.ToOtherLocationType, t.ToOtherLocationName, t.ToOtherNearestCityId, cities);
        var stopModels = stops.Select(s => ToLocation(s.LocationType, s.CityId, s.OtherLocationType, s.OtherLocationName, s.OtherNearestCityId, cities)).ToList();

        string? routeLabel = null;
        if (from is not null && to is not null)
            routeLabel = string.Join(" → ", new[] { from.Label }.Concat(stopModels.Select(s => s.Label)).Append(to.Label));
        else if (t.RouteId is { } routeId)
            routeLabel = await RouteLabelAsync(routeId, ct);

        return new TripModel
        {
            TripId = t.TripId, TripNumber = t.TripNumber, TripType = t.TripType, CustomerId = t.CustomerId, CustomerTripReference = t.CustomerTripReference,
            TripConfigurationId = t.TripConfigurationId, RouteId = t.RouteId, VehicleId = t.VehicleId, DriverId = t.DriverId, DefaultDriverId = t.DefaultDriverId,
            IsDriverOverridden = t.IsDriverOverridden, DriverOverrideReason = t.DriverOverrideReason, TripDate = t.TripDate, PlannedStart = t.PlannedStart,
            ActualStart = t.ActualStart, ActualEnd = t.ActualEnd, StartOdometer = t.StartOdometer, EndOdometer = t.EndOdometer,
            TripRateId = t.TripRateId, TripRateAmount = t.TripRateAmount, RateEffectiveFrom = t.RateEffectiveFrom, RateEffectiveTo = t.RateEffectiveTo,
            RateSource = t.RateSource, TripAmount = t.TripAmount, CurrencyCode = t.CurrencyCode, RateMissing = t.RateMissing, Status = t.Status,
            HeldFromStatus = t.HeldFromStatus, HoldReason = t.HoldReason, CancelReason = t.CancelReason, CompletionDate = t.CompletionDate,
            IsActive = t.IsActive, InactiveReason = t.InactiveReason, InvoiceId = t.InvoiceId, Remarks = t.Remarks, RowVersion = Convert.ToBase64String(t.RowVersion),
            From = from, To = to, Stops = stopModels, IsRoundTrip = t.IsRoundTrip, RouteLabel = routeLabel
        };
    }

    /// <summary>A Fixed trip has no From/To of its own — its route is the <see cref="Route"/> it was created
    /// against — so its `LHR → SKP → FSD` label (§22's own wording, reused here rather than inventing a second
    /// display convention) is built from that route's own stops instead.</summary>
    private async Task<string?> RouteLabelAsync(int routeId, CancellationToken ct)
    {
        var stops = await db.RouteStops.AsNoTracking().Where(s => s.TenantId == tenant.TenantId && s.RouteId == routeId).OrderBy(s => s.Sequence).ToListAsync(ct);
        if (stops.Count == 0) return null;
        var cityIds = stops.Select(s => s.CityId).Distinct().ToList();
        var cities = await db.Cities.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && cityIds.Contains(c.CityId)).ToDictionaryAsync(c => c.CityId, ct);
        return string.Join(" → ", stops.Select(s => cities.TryGetValue(s.CityId, out var city) ? city.Abbreviation : "?"));
    }

    private static LocationModel ToLocation(string locationType, int? cityId, string? otherType, string? otherName, int? nearestCityId, IReadOnlyDictionary<int, City> cities) => new()
    {
        LocationType = locationType, CityId = cityId, OtherLocationType = otherType, OtherLocationName = otherName, OtherNearestCityId = nearestCityId,
        Label = locationType == TripLocationTypes.City
            ? (cityId is { } id && cities.TryGetValue(id, out var city) ? city.Abbreviation : "?")
            : otherName ?? "?"
    };
}
