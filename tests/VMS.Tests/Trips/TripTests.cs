using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-12: the Trip entity, numbering, fixed trip creation with rate snapshot (§21, §23, §46.4).</summary>
[Collection(ApiCollection.Name)]
public sealed class TripTests(ApiFactory factory)
{
    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        return (vehicles, admin);
    }

    /// <summary>An Active customer, an Active route/configuration with one Active vehicle, and a driver — everything
    /// a fixed trip needs except its own rate (added or not, per test).</summary>
    private static async Task<(int CustomerId, long ConfigId, int VehicleId, int DriverId)> ReadyConfigAsync(VehicleWorld vehicles, HttpClient admin)
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
        // An early effectiveFrom (default is "today") so hardcoded 2026-07-* trip dates in these tests fall inside it.
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId, effectiveFrom = "2020-01-01" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/activate", new { rowVersion = config.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();

        var driverId = await vehicles.DriverAsync();
        return (customerId, configId, vehicleId, driverId);
    }

    [Fact]
    public async Task A_fixed_trip_snapshots_the_resolved_rate_and_gets_a_trp_number()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, configId, vehicleId, driverId) = await ReadyConfigAsync(vehicles, admin);
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", effectiveTo = "2026-07-31", rateAmount = 25000 })).EnsureSuccessStatusCode();

        var created = await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = "2026-07-10" });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var data = await created.DataAsync();
        Assert.Matches(@"^TRP-\d{4}-\d{6}$", data.GetProperty("tripNumber").GetString()!);
        Assert.Equal("Fixed", data.GetProperty("tripType").GetString());
        Assert.Equal("Configured", data.GetProperty("rateSource").GetString());
        Assert.Equal(25000, data.GetProperty("tripAmount").GetDecimal());
        Assert.False(data.GetProperty("rateMissing").GetBoolean());
        Assert.Equal("Draft", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task AC_16_a_trip_with_no_covering_rate_is_saved_rate_missing_with_no_amount()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, configId, vehicleId, driverId) = await ReadyConfigAsync(vehicles, admin);
        // No rate created at all for this configuration.

        var created = await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = "2026-07-10" });
        created.EnsureSuccessStatusCode();
        var data = await created.DataAsync();
        Assert.True(data.GetProperty("rateMissing").GetBoolean());
        Assert.Equal("Missing", data.GetProperty("rateSource").GetString());
        Assert.False(data.TryGetProperty("tripAmount", out var amount) && amount.ValueKind != System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task AC_15_the_trip_keeps_its_rate_snapshot_even_after_the_rate_row_changes()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, configId, vehicleId, driverId) = await ReadyConfigAsync(vehicles, admin);
        var rate = await (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount = 27000 })).DataAsync();

        var created = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = "2026-07-20" })).DataAsync();
        Assert.Equal(27000, created.GetProperty("tripAmount").GetDecimal());

        (await admin.PutAsJsonAsync($"/api/trip-rates/{rate.GetProperty("tripRateId").GetInt64()}",
            new { rateAmount = 30000, rowVersion = rate.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();

        var reloaded = await (await admin.GetAsync($"/api/trips/{created.GetProperty("tripId").GetInt64()}")).DataAsync();
        Assert.Equal(27000, reloaded.GetProperty("tripAmount").GetDecimal());
    }

    [Fact]
    public async Task A_vehicle_not_allowed_on_the_configuration_for_the_trip_date_is_refused()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, configId, _, driverId) = await ReadyConfigAsync(vehicles, admin);
        var otherTruck = await vehicles.ActiveAsync();   // never assigned to this configuration

        var response = await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId = VehicleWorld.Id(otherTruck), driverId, tripDate = "2026-07-10" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_partner_without_the_driver_role_is_refused()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, configId, vehicleId, _) = await ReadyConfigAsync(vehicles, admin);
        var workshop = await vehicles.PartnerAsync("Workshop");

        var response = await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId = workshop, tripDate = "2026-07-10" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC_20_a_duplicate_customer_reference_warns_for_the_same_customer_but_not_another()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, configId, vehicleId, driverId) = await ReadyConfigAsync(vehicles, admin);
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount = 25000 })).EnsureSuccessStatusCode();

        var first = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = "2026-07-01", customerTripReference = "PO-2026-4587" })).DataAsync();
        var firstTripNumber = first.GetProperty("tripNumber").GetString();

        var second = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = "2026-07-02", customerTripReference = "po-2026-4587" })).DataAsync();
        // The warning names the trip that already used it (§23's own example text), matched case-insensitively and trimmed.
        Assert.Contains(second.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains(firstTripNumber!));

        // A different customer using the same reference is accepted without any warning.
        var (customerB, configB, vehicleB, driverB) = await ReadyConfigAsync(vehicles, admin);
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configB}/rates", new { effectiveFrom = "2026-07-01", rateAmount = 25000 })).EnsureSuccessStatusCode();
        var thirdResponse = await admin.PostAsJsonAsync("/api/trips", new { customerId = customerB, tripConfigurationId = configB, vehicleId = vehicleB, driverId = driverB, tripDate = "2026-07-01", customerTripReference = "PO-2026-4587" });
        var third = await thirdResponse.DataAsync();
        Assert.Empty(third.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task View_needs_view_permission_create_needs_the_trip_create_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, configId, vehicleId, driverId) = await ReadyConfigAsync(vehicles, admin);
        var viewer = vehicles.As("Viewer", 5, PermissionCodes.TRP_TRIP_VIEW);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = "2026-07-10" })).StatusCode);
    }

    /// <summary>CC-43's own Trip List / Trip Desk board (§48.4) — the first test of the first list/search
    /// endpoint this module has ever had for trips. Confirms filtering by customer and that names (customer,
    /// vehicle, route) are resolved rather than left as bare ids.</summary>
    [Fact]
    public async Task Search_filters_by_customer_and_resolves_names()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, configId, vehicleId, driverId) = await ReadyConfigAsync(vehicles, admin);
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount = 25000 })).EnsureSuccessStatusCode();
        var created = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = "2026-07-10" })).DataAsync();

        var (otherCustomerId, otherConfigId, otherVehicleId, otherDriverId) = await ReadyConfigAsync(vehicles, admin);
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{otherConfigId}/rates", new { effectiveFrom = "2026-07-01", rateAmount = 25000 })).EnsureSuccessStatusCode();
        await admin.PostAsJsonAsync("/api/trips", new { customerId = otherCustomerId, tripConfigurationId = otherConfigId, vehicleId = otherVehicleId, driverId = otherDriverId, tripDate = "2026-07-10" });

        var page = await (await admin.GetAsync($"/api/trips/search?customerId={customerId}")).DataAsync();
        var items = page.GetProperty("items").EnumerateArray().ToList();
        var row = Assert.Single(items);
        Assert.Equal(created.GetProperty("tripId").GetInt64(), row.GetProperty("tripId").GetInt64());
        Assert.False(string.IsNullOrEmpty(row.GetProperty("customerName").GetString()));
        Assert.False(string.IsNullOrEmpty(row.GetProperty("vehicleRegistrationNo").GetString()));
        Assert.Contains("LHR", row.GetProperty("routeLabel").GetString());
        Assert.Equal("Draft", row.GetProperty("status").GetString());
    }

    [Fact]
    public async Task History_shows_the_trip_s_own_creation()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, configId, vehicleId, driverId) = await ReadyConfigAsync(vehicles, admin);
        var created = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = "2026-07-10" })).DataAsync();

        var history = await (await admin.GetAsync($"/api/trips/{created.GetProperty("tripId").GetInt64()}/history")).DataAsync();
        var rows = history.GetProperty("changes").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(rows, r => r.GetProperty("action").GetString() == "Created");
    }
}
