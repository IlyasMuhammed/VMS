using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;

namespace VMS.Modules.Trips.Services;

public interface IRouteService
{
    Task<IReadOnlyList<RouteModel>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<RouteModel> GetAsync(int routeId, CancellationToken ct = default);
    Task<RouteModel> CreateAsync(CreateRouteRequest request, CancellationToken ct = default);
    Task<RouteModel> UpdateAsync(int routeId, UpdateRouteRequest request, CancellationToken ct = default);
    Task<RouteModel> UpdateStopsAsync(int routeId, UpdateRouteStopsRequest request, CancellationToken ct = default);
}

internal sealed class RouteService(TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages) : IRouteService
{
    public async Task<IReadOnlyList<RouteModel>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var query = db.Routes.AsNoTracking().Where(r => r.TenantId == tenant.TenantId);
        if (!includeInactive) query = query.Where(r => r.Status == ActiveInactiveStatuses.Active);
        var routes = await query.OrderBy(r => r.RouteCode).ToListAsync(ct);
        var stopsByRoute = await StopsByRouteAsync(routes.Select(r => r.RouteId), ct);
        return routes.Select(r => ToModel(r, stopsByRoute.GetValueOrDefault(r.RouteId, []))).ToList();
    }

    public async Task<RouteModel> GetAsync(int routeId, CancellationToken ct = default)
    {
        var route = await Find(routeId, ct);
        var stops = await db.RouteStops.AsNoTracking().Where(s => s.TenantId == tenant.TenantId && s.RouteId == routeId)
            .OrderBy(s => s.Sequence).Select(s => ToModel(s)).ToListAsync(ct);
        return ToModel(route, stops);
    }

    public async Task<RouteModel> CreateAsync(CreateRouteRequest request, CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (string.IsNullOrWhiteSpace(request.RouteName)) Add("routeName", Msg.Required, ("Field", "Route name"));
        if (request.DistanceKm is <= 0) Add("distanceKm", Msg.Min, ("Field", "Distance (km)"), ("Min", "0.01"));
        if (request.StandardDurationMin is <= 0) Add("standardDurationMin", Msg.Min, ("Field", "Standard duration (minutes)"), ("Min", "1"));

        var cities = await ValidateStopsAsync(request.Stops, request.IsRoundTrip, Add, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        var origin = request.Stops[0].CityId;
        var destination = request.Stops[^1].CityId;
        var code = string.IsNullOrWhiteSpace(request.RouteCode) ? await SuggestCodeAsync(origin, destination, cities, ct) : request.RouteCode.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(request.RouteCode) && await db.Routes.AnyAsync(r => r.TenantId == tenant.TenantId && r.RouteCode == code, ct))
            throw new ValidationException(messages.Error("routeCode", Msg.AlreadyUsed, ("Field", "route code"), ("Code", code), ("Name", "another route")));

        var route = new Route
        {
            RouteCode = code, RouteName = request.RouteName.Trim(), OriginCityId = origin, DestinationCityId = destination,
            IsRoundTrip = request.IsRoundTrip, DistanceKm = request.DistanceKm, StandardDurationMin = request.StandardDurationMin,
            Status = ActiveInactiveStatuses.Active, Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim()
        };

        await db.InTransactionAsync(async ct2 =>
        {
            db.Routes.Add(route);
            await db.SaveChangesAsync(ct2);
            db.RouteStops.AddRange(request.Stops.Select((s, i) => ToEntity(route.RouteId, s, i + 1)));
            await db.SaveChangesAsync(ct2);
        }, ct);

        return await GetAsync(route.RouteId, ct);
    }

    public async Task<RouteModel> UpdateAsync(int routeId, UpdateRouteRequest request, CancellationToken ct = default)
    {
        var route = await Find(routeId, ct);
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (string.IsNullOrWhiteSpace(request.RouteName)) Add("routeName", Msg.Required, ("Field", "Route name"));
        if (request.DistanceKm is <= 0) Add("distanceKm", Msg.Min, ("Field", "Distance (km)"), ("Min", "0.01"));
        if (request.StandardDurationMin is <= 0) Add("standardDurationMin", Msg.Min, ("Field", "Standard duration (minutes)"), ("Min", "1"));
        var status = string.IsNullOrWhiteSpace(request.Status) ? route.Status : request.Status;
        if (status is not (ActiveInactiveStatuses.Active or ActiveInactiveStatuses.Inactive))
            Add("status", Msg.OneOf, ("Field", "Status"), ("Allowed", string.Join(", ", ActiveInactiveStatuses.All)));
        if (errors.Count > 0) throw new ValidationException(errors);

        route.RouteName = request.RouteName.Trim();
        route.IsRoundTrip = request.IsRoundTrip;
        route.DistanceKm = request.DistanceKm;
        route.StandardDurationMin = request.StandardDurationMin;
        route.Status = status;
        route.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        await db.SaveChangesAsync(ct);
        return await GetAsync(routeId, ct);
    }

    public async Task<RouteModel> UpdateStopsAsync(int routeId, UpdateRouteStopsRequest request, CancellationToken ct = default)
    {
        var route = await Find(routeId, ct);
        // §17: "afterwards changes create a new route instead" once a trip configuration (CC-10) uses this one —
        // CC-10 doesn't exist yet, so IsLocked is never true today; the check is in place for when it starts being set.
        if (route.IsLocked) throw new ConflictException("This route is used by a trip configuration; create a new route instead of reordering its stops.");

        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));
        await ValidateStopsAsync(request.Stops, route.IsRoundTrip, Add, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        await db.InTransactionAsync(async ct2 =>
        {
            var old = await db.RouteStops.Where(s => s.TenantId == tenant.TenantId && s.RouteId == routeId).ToListAsync(ct2);
            db.RouteStops.RemoveRange(old);
            await db.SaveChangesAsync(ct2);

            db.RouteStops.AddRange(request.Stops.Select((s, i) => ToEntity(routeId, s, i + 1)));
            route.OriginCityId = request.Stops[0].CityId;
            route.DestinationCityId = request.Stops[^1].CityId;
            await db.SaveChangesAsync(ct2);
        }, ct);

        return await GetAsync(routeId, ct);
    }

    private async Task<Route> Find(int routeId, CancellationToken ct) =>
        await db.Routes.FirstOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.RouteId == routeId, ct)
        ?? throw new NotFoundException($"Route {routeId} was not found.");

    private async Task<Dictionary<int, City>> ValidateStopsAsync(IReadOnlyList<RouteStopInput> stops, bool isRoundTrip,
        Action<string, string, (string, object?)[]> add, CancellationToken ct)
    {
        if (stops.Count < 2)
        {
            add("stops", Msg.Min, [("Field", "Stops"), ("Min", "2")]);
            return [];
        }

        var cities = await db.Cities.Where(c => c.TenantId == tenant.TenantId && stops.Select(s => s.CityId).Contains(c.CityId))
            .ToDictionaryAsync(c => c.CityId, ct);
        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            if (!cities.TryGetValue(stop.CityId, out var city)) add($"stops[{i}].cityId", Msg.Invalid, [("Field", "City")]);
            else if (city.Status != ActiveInactiveStatuses.Active) add($"stops[{i}].cityId", Msg.Invalid, [("Field", "City (inactive)")]);

            if (!RouteStopTypes.All.Contains(stop.StopType)) add($"stops[{i}].stopType", Msg.OneOf, [("Field", "Stop type"), ("Allowed", string.Join(", ", RouteStopTypes.All))]);
            else if (i == 0 && stop.StopType != RouteStopTypes.Origin) add($"stops[{i}].stopType", Msg.Invalid, [("Field", "First stop must be Origin")]);
            else if (i == stops.Count - 1 && stop.StopType != RouteStopTypes.Destination) add($"stops[{i}].stopType", Msg.Invalid, [("Field", "Last stop must be Destination")]);
            else if (i > 0 && i < stops.Count - 1 && stop.StopType is RouteStopTypes.Origin or RouteStopTypes.Destination)
                add($"stops[{i}].stopType", Msg.Invalid, [("Field", "Only the first stop can be Origin and only the last can be Destination")]);
        }

        if (!isRoundTrip && stops.Count >= 2 && stops[0].CityId == stops[^1].CityId)
            add("stops", Msg.Invalid, [("Field", "Origin and destination must differ unless the route is a round trip")]);

        return cities;
    }

    private async Task<string> SuggestCodeAsync(int originCityId, int destinationCityId, Dictionary<int, City> cities, CancellationToken ct)
    {
        var origin = cities.TryGetValue(originCityId, out var o) ? o.Abbreviation : (await db.Cities.FirstAsync(c => c.CityId == originCityId, ct)).Abbreviation;
        var destination = cities.TryGetValue(destinationCityId, out var d) ? d.Abbreviation : (await db.Cities.FirstAsync(c => c.CityId == destinationCityId, ct)).Abbreviation;
        var baseCode = $"RT-{origin}-{destination}";
        var code = baseCode;
        var suffix = 2;
        while (await db.Routes.AnyAsync(r => r.TenantId == tenant.TenantId && r.RouteCode == code, ct))
            code = $"{baseCode}-{suffix++}";
        return code;
    }

    private async Task<Dictionary<int, List<RouteStopModel>>> StopsByRouteAsync(IEnumerable<int> routeIds, CancellationToken ct)
    {
        var ids = routeIds.ToList();
        var stops = await db.RouteStops.AsNoTracking().Where(s => s.TenantId == tenant.TenantId && ids.Contains(s.RouteId))
            .OrderBy(s => s.Sequence).ToListAsync(ct);
        return stops.GroupBy(s => s.RouteId).ToDictionary(g => g.Key, g => g.Select(ToModel).ToList());
    }

    private static RouteStop ToEntity(int routeId, RouteStopInput input, int sequence) => new()
    {
        RouteId = routeId, CityId = input.CityId, Sequence = sequence, StopType = input.StopType,
        PlannedDurationMin = input.PlannedDurationMin, Remarks = string.IsNullOrWhiteSpace(input.Remarks) ? null : input.Remarks.Trim()
    };

    private static RouteStopModel ToModel(RouteStop s) => new()
    {
        RouteStopId = s.RouteStopId, CityId = s.CityId, Sequence = s.Sequence, StopType = s.StopType,
        PlannedDurationMin = s.PlannedDurationMin, Remarks = s.Remarks
    };

    private static RouteModel ToModel(Route r, IReadOnlyList<RouteStopModel> stops) => new()
    {
        RouteId = r.RouteId, RouteCode = r.RouteCode, RouteName = r.RouteName, OriginCityId = r.OriginCityId,
        DestinationCityId = r.DestinationCityId, IsRoundTrip = r.IsRoundTrip, DistanceKm = r.DistanceKm,
        StandardDurationMin = r.StandardDurationMin, Status = r.Status, Remarks = r.Remarks, Stops = stops
    };
}
