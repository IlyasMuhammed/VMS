using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-45: driver app API and trip creation review queue (§43, §47.2, AC-53, AC-54, AC-66).</summary>
[Collection(ApiCollection.Name)]
public sealed class DriverAppTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_TRIP_REVIEW]);
        return (vehicles, admin);
    }

    private HttpClient DriverClient(VehicleWorld vehicles, string name, int driverId) =>
        factory.CreateClient().WithToken(TestTokens.ForScoped(vehicles.Tenant, name, driverId, null, null, driverId));

    private static async Task<(int CustomerId, Dictionary<string, int> Cities)> ReadyCustomerAsync(HttpClient admin)
    {
        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        var byAbbr = cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "Line 1" })).DataAsync();
        var id = customer.GetProperty("customerId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{id}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();
        return (id, byAbbr);
    }

    /// <summary>An Active, rated configuration for the given vehicle — the same shape a driver's own vehicle
    /// needs to appear in <c>GET /api/driver/trip-options</c> and to create a Fixed trip against.</summary>
    private static async Task<long> ReadyConfigAsync(HttpClient admin, int customerId, Dictionary<string, int> cities, int vehicleId, string name = "Daily")
    {
        var route = await (await admin.PostAsJsonAsync("/api/routes", new
        {
            routeName = $"Route {Guid.NewGuid():N}"[..20],
            stops = new[] { new { cityId = cities["LHR"], stopType = "Origin" }, new { cityId = cities["FSD"], stopType = "Destination" } }
        })).DataAsync();
        var config = await (await admin.PostAsJsonAsync("/api/trip-configurations",
            new { customerId, name, routeId = route.GetProperty("routeId").GetInt32(), directionType = "OneWay" })).DataAsync();
        var configId = config.GetProperty("tripConfigurationId").GetInt64();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId, effectiveFrom = "2020-01-01" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/activate", new { rowVersion = config.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount = 25000 })).EnsureSuccessStatusCode();
        return configId;
    }

    /// <summary>Assigns a driver as a vehicle's own default — §43's own "vehicle pre-filled with the driver's
    /// assigned vehicle" reads this same column back (<c>OwnVehicleIdAsync</c>).</summary>
    private static async Task<(int VehicleId, int DriverId)> ReadyDriverVehicleAsync(VehicleWorld vehicles, HttpClient admin)
    {
        var truck = await vehicles.ActiveAsync();
        var vehicleId = VehicleWorld.Id(truck);
        var driverId = await vehicles.DriverAsync();
        (await admin.PostAsJsonAsync($"/api/vehicles/{vehicleId}/driver", new { driverId })).EnsureSuccessStatusCode();
        return (vehicleId, driverId);
    }

    [Fact]
    public async Task AC_66_driver_creates_a_fixed_trip_that_lands_in_pending_review_with_no_amount_shown()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, cities) = await ReadyCustomerAsync(admin);
        var (vehicleId, driverId) = await ReadyDriverVehicleAsync(vehicles, admin);
        var configId = await ReadyConfigAsync(admin, customerId, cities, vehicleId);
        var driver = DriverClient(vehicles, "Ali", driverId);

        var options = await (await driver.GetAsync("/api/driver/trip-options")).DataAsync();
        Assert.Equal(vehicleId, options.GetProperty("vehicleId").GetInt32());
        Assert.Contains(options.GetProperty("customers").EnumerateArray(), c => c.GetProperty("customerId").GetInt32() == customerId);

        var response = await driver.PostAsJsonAsync("/api/driver/trips", new { customerId, tripConfigurationId = configId, tripDate = Day() });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var created = await response.DataAsync();
        Assert.Equal("Draft", created.GetProperty("status").GetString());
        Assert.False(created.TryGetProperty("tripAmount", out _), "The driver's own response must never carry a trip amount.");
        Assert.False(created.TryGetProperty("tripRateAmount", out _), "The driver's own response must never carry a rate.");
        var tripId = created.GetProperty("tripId").GetInt64();

        var queue = await (await admin.GetAsync("/api/trips/pending-review")).DataAsync();
        Assert.Contains(queue.EnumerateArray(), t => t.GetProperty("tripId").GetInt64() == tripId);

        // The office's own, full view confirms the trip really was created Source = DriverApp with a resolved rate.
        var officeView = await (await admin.GetAsync($"/api/trips/{tripId}")).DataAsync();
        Assert.Equal(25000, officeView.GetProperty("tripAmount").GetDecimal());
    }

    [Fact]
    public async Task Driver_creates_an_open_trip_with_no_amount_and_it_needs_one_before_release()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, cities) = await ReadyCustomerAsync(admin);
        var (_, driverId) = await ReadyDriverVehicleAsync(vehicles, admin);
        var driver = DriverClient(vehicles, "Ali", driverId);

        var response = await driver.PostAsJsonAsync("/api/driver/trips/open", new
        {
            customerId, from = new { locationType = "City", cityId = cities["LHR"] }, to = new { locationType = "City", cityId = cities["MUL"] },
            tripDate = Day()
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var created = await response.DataAsync();
        var tripId = created.GetProperty("tripId").GetInt64();

        var pending = (await (await admin.GetAsync("/api/trips/pending-review")).DataAsync()).EnumerateArray().Single(t => t.GetProperty("tripId").GetInt64() == tripId);
        Assert.True(pending.GetProperty("rateMissing").GetBoolean());
        var rowVersion = pending.GetProperty("rowVersion").GetString();

        // Releasing without an amount is refused — an Open trip still has nothing to bill.
        var withoutAmount = await admin.PostAsJsonAsync($"/api/trips/{tripId}/release", new { rowVersion });
        Assert.Equal((HttpStatusCode)422, withoutAmount.StatusCode);
        Assert.Contains("RATE_MISSING", await withoutAmount.Content.ReadAsStringAsync());

        // §43: "Operations enters the trip amount when reviewing."
        var released = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/release", new { rowVersion, tripAmount = 18000 })).DataAsync();
        Assert.Equal("Assigned", released.GetProperty("status").GetString());
        Assert.Equal(18000, released.GetProperty("tripAmount").GetDecimal());
    }

    [Fact]
    public async Task Rejecting_a_pending_trip_cancels_it_with_the_given_reason()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, cities) = await ReadyCustomerAsync(admin);
        var (vehicleId, driverId) = await ReadyDriverVehicleAsync(vehicles, admin);
        var configId = await ReadyConfigAsync(admin, customerId, cities, vehicleId);
        var driver = DriverClient(vehicles, "Ali", driverId);

        var created = await (await driver.PostAsJsonAsync("/api/driver/trips", new { customerId, tripConfigurationId = configId, tripDate = Day() })).DataAsync();
        var tripId = created.GetProperty("tripId").GetInt64();
        var rowVersion = (await (await admin.GetAsync($"/api/trips/{tripId}")).DataAsync()).GetProperty("rowVersion").GetString();

        var rejected = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/reject", new { rowVersion, reason = "Wrong customer selected" })).DataAsync();
        Assert.Equal("Cancelled", rejected.GetProperty("status").GetString());
        Assert.Equal("Wrong customer selected", rejected.GetProperty("cancelReason").GetString());
    }

    [Fact]
    public async Task Pending_review_actions_need_their_own_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var noReview = vehicles.As("NoReview", 88, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        var response = await noReview.GetAsync("/api/trips/pending-review");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AC_53_a_driver_sees_only_their_own_trips_never_another_drivers()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, cities) = await ReadyCustomerAsync(admin);
        var (vehicleId, aliId) = await ReadyDriverVehicleAsync(vehicles, admin);
        var configId = await ReadyConfigAsync(admin, customerId, cities, vehicleId);

        var trip = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId = aliId, tripDate = Day() })).DataAsync();
        var tripId = trip.GetProperty("tripId").GetInt64();
        var planned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Planned", new { rowVersion = trip.GetProperty("rowVersion").GetString() })).DataAsync();
        var assigned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Assigned", new { rowVersion = planned.GetProperty("rowVersion").GetString() })).DataAsync();
        Assert.Equal("Assigned", assigned.GetProperty("status").GetString());

        var ali = DriverClient(vehicles, "Ali", aliId);
        var myTrips = await (await ali.GetAsync("/api/driver/trips")).DataAsync();
        Assert.Contains(myTrips.GetProperty("today").EnumerateArray().Concat(myTrips.GetProperty("upcoming").EnumerateArray()), t => t.GetProperty("tripId").GetInt64() == tripId);
        var own = await ali.GetAsync($"/api/driver/trips/{tripId}");
        Assert.True(own.IsSuccessStatusCode);
        Assert.DoesNotContain("tripAmount", await own.Content.ReadAsStringAsync());

        // Someone else's trip — not 403, genuinely 404 (§44).
        var bilalId = await vehicles.DriverAsync();
        var bilal = DriverClient(vehicles, "Bilal", bilalId);
        var forbidden = await bilal.GetAsync($"/api/driver/trips/{tripId}");
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
    }

    [Fact]
    public async Task A_driver_never_sees_a_trip_still_in_Draft_or_Planned()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, cities) = await ReadyCustomerAsync(admin);
        var (vehicleId, driverId) = await ReadyDriverVehicleAsync(vehicles, admin);
        var configId = await ReadyConfigAsync(admin, customerId, cities, vehicleId);

        var trip = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = Day() })).DataAsync();
        var tripId = trip.GetProperty("tripId").GetInt64();

        var driver = DriverClient(vehicles, "Ali", driverId);
        var myTrips = await (await driver.GetAsync("/api/driver/trips")).DataAsync();
        var everything = myTrips.GetProperty("today").EnumerateArray()
            .Concat(myTrips.GetProperty("upcoming").EnumerateArray()).Concat(myTrips.GetProperty("recent").EnumerateArray());
        Assert.DoesNotContain(everything, t => t.GetProperty("tripId").GetInt64() == tripId);

        // A direct read of the trip's own id is still reachable while it is their own (creating it in the app —
        // Draft, Source DriverApp — is exactly this shape); only "My Trips" itself excludes Draft/Planned.
        var directRead = await driver.GetAsync($"/api/driver/trips/{tripId}");
        Assert.True(directRead.IsSuccessStatusCode);
    }

    [Fact]
    public async Task AC_54_a_retried_status_step_with_the_same_client_event_id_is_not_duplicated()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, cities) = await ReadyCustomerAsync(admin);
        var (vehicleId, driverId) = await ReadyDriverVehicleAsync(vehicles, admin);
        var configId = await ReadyConfigAsync(admin, customerId, cities, vehicleId);

        var trip = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = Day() })).DataAsync();
        var tripId = trip.GetProperty("tripId").GetInt64();
        var planned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Planned", new { rowVersion = trip.GetProperty("rowVersion").GetString() })).DataAsync();
        var assigned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Assigned", new { rowVersion = planned.GetProperty("rowVersion").GetString() })).DataAsync();

        var driver = DriverClient(vehicles, "Ali", driverId);
        var clientEventId = Guid.NewGuid();
        var body = new { toStatus = "Started", startOdometer = 100, rowVersion = assigned.GetProperty("rowVersion").GetString(), clientEventId };

        var first = await (await driver.PostAsJsonAsync($"/api/driver/trips/{tripId}/steps", body)).DataAsync();
        Assert.Equal("Started", first.GetProperty("status").GetString());

        // Retried with the SAME clientEventId (and the now-stale rowVersion an offline device would still be
        // holding) — answered with the trip's own current state, not a second attempt that would otherwise fail
        // on a stale row version or an invalid transition.
        var retry = await driver.PostAsJsonAsync($"/api/driver/trips/{tripId}/steps", body);
        Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
        var retryData = await retry.DataAsync();
        Assert.Equal("Started", retryData.GetProperty("status").GetString());

        var events = await (await admin.GetAsync($"/api/trips/{tripId}/events")).DataAsync();
        Assert.Single(events.EnumerateArray(), e => e.GetProperty("eventType").GetString() == "Started");
    }

    [Fact]
    public async Task Offline_sync_replays_a_batch_and_one_bad_item_never_aborts_the_rest()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, cities) = await ReadyCustomerAsync(admin);
        var (vehicleId, driverId) = await ReadyDriverVehicleAsync(vehicles, admin);
        var configId = await ReadyConfigAsync(admin, customerId, cities, vehicleId);

        var trip = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = Day() })).DataAsync();
        var tripId = trip.GetProperty("tripId").GetInt64();
        var planned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Planned", new { rowVersion = trip.GetProperty("rowVersion").GetString() })).DataAsync();
        var assigned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Assigned", new { rowVersion = planned.GetProperty("rowVersion").GetString() })).DataAsync();

        var driver = DriverClient(vehicles, "Ali", driverId);
        var sync = await driver.PostAsJsonAsync("/api/driver/sync", new
        {
            actions = new object[]
            {
                new
                {
                    actionType = "Step", tripId, toStatus = "Started",
                    payload = new { startOdometer = 100, rowVersion = assigned.GetProperty("rowVersion").GetString(), clientEventId = Guid.NewGuid() }
                },
                new
                {
                    actionType = "Fuel", tripId,
                    payload = new { fuelType = "Diesel", quantity = 40, rate = 280, paymentMethod = "Cash", clientEventId = Guid.NewGuid() }
                },
                new { actionType = "Fuel", tripId = 999999, payload = new { fuelType = "Diesel", quantity = 1, rate = 1, paymentMethod = "Cash" } },
            }
        });
        Assert.True(sync.IsSuccessStatusCode, await sync.Content.ReadAsStringAsync());
        var results = (await sync.DataAsync()).GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(3, results.Count);
        Assert.True(results[0].GetProperty("success").GetBoolean());
        Assert.True(results[1].GetProperty("success").GetBoolean());
        Assert.False(results[2].GetProperty("success").GetBoolean());

        var officeView = await (await admin.GetAsync($"/api/trips/{tripId}")).DataAsync();
        Assert.Equal("Started", officeView.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_user_with_no_linked_driver_profile_is_refused_the_whole_driver_surface()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var noProfile = vehicles.As("Office User", 90, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        var response = await noProfile.GetAsync("/api/driver/trips");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
