using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;

namespace VMS.Modules.Trips.Services;

/// <summary>Builds a display route label the same way for a Fixed trip (its own Route's ordered stops) as for an
/// Open trip's own From/Stops/To. This duplicates, deliberately, the inline label-building CC-13 already does
/// once at Open-trip creation time — nothing stores a route label on the trip row itself, so any later read
/// (search-eligible-trips, invoice creation) has to rebuild it fresh from stored columns. Shared here so the two
/// read paths that need it (<see cref="InvoiceEligibilityService"/>, <see cref="InvoiceCreationService"/>) build
/// the exact same string rather than two slowly-drifting copies.</summary>
internal static class RouteLabels
{
    public static async Task<string?> BuildAsync(TripsDbContext db, Guid tenantId, Trip trip, CancellationToken ct)
    {
        if (trip.RouteId is { } routeId)
        {
            var stops = await db.RouteStops.AsNoTracking().Where(s => s.TenantId == tenantId && s.RouteId == routeId).OrderBy(s => s.Sequence).ToListAsync(ct);
            if (stops.Count == 0) return null;
            var cities = await db.Cities.AsNoTracking().Where(c => c.TenantId == tenantId && stops.Select(s => s.CityId).Contains(c.CityId)).ToDictionaryAsync(c => c.CityId, ct);
            return string.Join(" → ", stops.Select(s => cities.TryGetValue(s.CityId, out var city) ? city.Abbreviation : "?"));
        }
        if (trip.FromLocationType is null || trip.ToLocationType is null) return null;
        var cityIds = new List<int?> { trip.FromCityId, trip.ToCityId }.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var openCities = cityIds.Count == 0 ? new Dictionary<int, City>() : await db.Cities.AsNoTracking().Where(c => c.TenantId == tenantId && cityIds.Contains(c.CityId)).ToDictionaryAsync(c => c.CityId, ct);
        string Label(string locationType, int? cityId, string? otherName) =>
            locationType == TripLocationTypes.City ? (cityId is { } id && openCities.TryGetValue(id, out var c) ? c.Abbreviation : "?") : otherName ?? "?";
        var stopRows = await db.TripStops.AsNoTracking().Where(s => s.TenantId == tenantId && s.TripId == trip.TripId).OrderBy(s => s.Sequence).ToListAsync(ct);
        var parts = new List<string> { Label(trip.FromLocationType, trip.FromCityId, trip.FromOtherLocationName) };
        parts.AddRange(stopRows.Select(s => Label(s.LocationType, s.CityId, s.OtherLocationName)));
        parts.Add(Label(trip.ToLocationType, trip.ToCityId, trip.ToOtherLocationName));
        return string.Join(" → ", parts);
    }
}
