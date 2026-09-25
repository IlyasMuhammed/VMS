using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-17: rate re-resolution and re-pricing (§26).</summary>
[Collection(ApiCollection.Name)]
public sealed class TripRepricingTests(ApiFactory factory)
{
    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        return (vehicles, admin);
    }

    private static HttpClient FinanceClient(VehicleWorld vehicles) =>
        vehicles.As("Finance", 8, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_RATE_REPRICE]);

    /// <summary>A Fixed trip, its customer/configuration/vehicle, and (optionally) a rate covering the trip date.</summary>
    private static async Task<(long TripId, long ConfigId, int CustomerId)> FixedTripAsync(VehicleWorld vehicles, HttpClient admin, decimal? rateAmount = 25000, string tripDate = "2026-07-10")
    {
        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        var byAbbr = cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());

        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "Line 1" })).DataAsync();
        var customerId = customer.GetProperty("customerId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();

        var route = await (await admin.PostAsJsonAsync("/api/routes", new
        {
            routeName = "Lahore - Faisalabad",
            stops = new[] { new { cityId = byAbbr["LHR"], stopType = "Origin" }, new { cityId = byAbbr["FSD"], stopType = "Destination" } }
        })).DataAsync();
        var config = await (await admin.PostAsJsonAsync("/api/trip-configurations",
            new { customerId, name = "Daily", routeId = route.GetProperty("routeId").GetInt32(), directionType = "OneWay" })).DataAsync();
        var configId = config.GetProperty("tripConfigurationId").GetInt64();

        var truck = await vehicles.ActiveAsync();
        var vehicleId = VehicleWorld.Id(truck);
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId, effectiveFrom = "2020-01-01" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/activate", new { rowVersion = config.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();
        if (rateAmount is not null)
            (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount })).EnsureSuccessStatusCode();

        var driverId = await vehicles.DriverAsync();
        var created = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate })).DataAsync();
        return (created.GetProperty("tripId").GetInt64(), configId, customerId);
    }

    [Fact]
    public async Task Resolve_missing_rates_updates_only_ratemissing_trips()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (missingTripId, configId, _) = await FixedTripAsync(vehicles, admin, rateAmount: null);   // no rate at all yet
        var (ratedTripId, _, _) = await FixedTripAsync(vehicles, admin, rateAmount: 15000);            // already priced

        var beforeFix = await (await admin.GetAsync($"/api/trips/{missingTripId}")).DataAsync();
        Assert.True(beforeFix.GetProperty("rateMissing").GetBoolean());

        // Now add a rate covering the missing trip's own date.
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount = 25000 })).EnsureSuccessStatusCode();

        var result = await (await admin.PostAsJsonAsync("/api/trips/resolve-missing-rates", new { })).DataAsync();
        Assert.Equal(1, result.GetProperty("considered").GetInt32());   // the already-rated trip is never considered
        Assert.Equal(1, result.GetProperty("updated").GetInt32());
        Assert.Equal(0, result.GetProperty("stillMissing").GetInt32());

        var fixedTrip = await (await admin.GetAsync($"/api/trips/{missingTripId}")).DataAsync();
        Assert.False(fixedTrip.GetProperty("rateMissing").GetBoolean());
        Assert.Equal(25000, fixedTrip.GetProperty("tripAmount").GetDecimal());

        var ratedTrip = await (await admin.GetAsync($"/api/trips/{ratedTripId}")).DataAsync();
        Assert.Equal(15000, ratedTrip.GetProperty("tripAmount").GetDecimal());   // untouched

        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM trp.TripRateHistories WHERE TripId = @id AND Action = 'ResolveMissingRates'", ("@id", missingTripId)));
    }

    [Fact]
    public async Task Reprice_needs_the_reprice_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (tripId, _, _) = await FixedTripAsync(vehicles, admin);
        var response = await admin.PostAsJsonAsync("/api/trips/reprice", new { tripIds = new[] { tripId } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_preview_shows_old_and_new_amounts_but_writes_nothing()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var finance = FinanceClient(vehicles);
        var (tripId, configId, _) = await FixedTripAsync(vehicles, admin, rateAmount: 25000);

        var rateRow = (await (await admin.GetAsync($"/api/trip-configurations/{configId}/rates")).DataAsync()).EnumerateArray().First();
        var rateId = rateRow.GetProperty("tripRateId").GetInt64();
        (await admin.PutAsJsonAsync($"/api/trip-rates/{rateId}", new { rateAmount = 30000, rowVersion = rateRow.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();

        var preview = await (await finance.PostAsJsonAsync("/api/trips/reprice", new { tripIds = new[] { tripId } })).DataAsync();
        Assert.False(preview.GetProperty("committed").GetBoolean());
        var item = preview.GetProperty("items").EnumerateArray().First();
        Assert.Equal(25000, item.GetProperty("oldAmount").GetDecimal());
        Assert.Equal(30000, item.GetProperty("newAmount").GetDecimal());
        Assert.False(item.GetProperty("excluded").GetBoolean());

        var stillOld = await (await admin.GetAsync($"/api/trips/{tripId}")).DataAsync();
        Assert.Equal(25000, stillOld.GetProperty("tripAmount").GetDecimal());   // nothing written by a preview
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM trp.TripRateHistories WHERE TripId = @id", ("@id", tripId)));
    }

    [Fact]
    public async Task Committing_a_reprice_needs_a_reason_and_then_writes_history_and_updates_the_trip()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var finance = FinanceClient(vehicles);
        var (tripId, configId, _) = await FixedTripAsync(vehicles, admin, rateAmount: 25000);

        var rateRow = (await (await admin.GetAsync($"/api/trip-configurations/{configId}/rates")).DataAsync()).EnumerateArray().First();
        var rateId = rateRow.GetProperty("tripRateId").GetInt64();
        (await admin.PutAsJsonAsync($"/api/trip-rates/{rateId}", new { rateAmount = 30000, rowVersion = rateRow.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();

        var noReason = await finance.PostAsJsonAsync("/api/trips/reprice", new { tripIds = new[] { tripId }, commit = true });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var committed = await (await finance.PostAsJsonAsync("/api/trips/reprice", new { tripIds = new[] { tripId }, commit = true, reason = "Rate corrected" })).DataAsync();
        Assert.True(committed.GetProperty("committed").GetBoolean());

        var reloaded = await (await admin.GetAsync($"/api/trips/{tripId}")).DataAsync();
        Assert.Equal(30000, reloaded.GetProperty("tripAmount").GetDecimal());
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM trp.TripRateHistories WHERE TripId = @id AND Action = 'Reprice' AND Reason = 'Rate corrected'", ("@id", tripId)));
    }

    [Fact]
    public async Task Trips_on_an_active_invoice_are_excluded_from_repricing()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var finance = FinanceClient(vehicles);
        var (tripId, _, _) = await FixedTripAsync(vehicles, admin, rateAmount: 25000);
        // No invoicing module exists yet — simulate "on an active invoice" directly, the same stand-in this
        // register has used before for a not-yet-built dependency (e.g. VehicleWorld.ActiveAsync's own SQL activation).
        factory.Execute("UPDATE trp.Trips SET InvoiceId = 999 WHERE TripId = @id", ("@id", tripId));

        var result = await (await finance.PostAsJsonAsync("/api/trips/reprice", new { tripIds = new[] { tripId }, commit = true, reason = "Attempted" })).DataAsync();
        var item = result.GetProperty("items").EnumerateArray().First();
        Assert.True(item.GetProperty("excluded").GetBoolean());

        var unchanged = await (await admin.GetAsync($"/api/trips/{tripId}")).DataAsync();
        Assert.Equal(25000, unchanged.GetProperty("tripAmount").GetDecimal());
    }

    [Fact]
    public async Task Open_trips_are_excluded_from_repricing_since_their_amount_is_manual()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var finance = FinanceClient(vehicles);
        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        var byAbbr = cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "Line 1" })).DataAsync();
        var customerId = customer.GetProperty("customerId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();
        var truck = await vehicles.ActiveAsync();
        var open = await (await admin.PostAsJsonAsync("/api/trips/open", new
        {
            customerId, from = new { locationType = "City", cityId = byAbbr["LHR"] }, to = new { locationType = "City", cityId = byAbbr["MUL"] },
            vehicleId = VehicleWorld.Id(truck), tripAmount = 1000, tripDate = "2026-09-01"
        })).DataAsync();

        var result = await (await finance.PostAsJsonAsync("/api/trips/reprice", new { tripIds = new[] { open.GetProperty("tripId").GetInt64() } })).DataAsync();
        Assert.True(result.GetProperty("items").EnumerateArray().First().GetProperty("excluded").GetBoolean());
    }
}
