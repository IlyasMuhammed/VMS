using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-13: open trips and Other Location (§22, AC-19).</summary>
[Collection(ApiCollection.Name)]
public sealed class OpenTripTests(ApiFactory factory)
{
    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        return (vehicles, admin);
    }

    private static async Task<int> ActiveCustomerAsync(HttpClient admin)
    {
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "Line 1" })).DataAsync();
        var id = customer.GetProperty("customerId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{id}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<Dictionary<string, int>> CityIdsByAbbrAsync(HttpClient admin)
    {
        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        return cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());
    }

    [Fact]
    public async Task AC_19_an_open_trip_stores_a_manual_amount_and_a_manual_rate_source()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ActiveCustomerAsync(admin);
        var cities = await CityIdsByAbbrAsync(admin);
        var truck = await vehicles.ActiveAsync();
        var driverId = await vehicles.DriverAsync();

        var created = await admin.PostAsJsonAsync("/api/trips/open", new
        {
            customerId,
            from = new { locationType = "City", cityId = cities["LHR"] },
            to = new { locationType = "City", cityId = cities["DGK"] },
            vehicleId = VehicleWorld.Id(truck), driverId, tripAmount = 80000, tripDate = "2026-09-01"
        });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var data = await created.DataAsync();
        Assert.Equal("Open", data.GetProperty("tripType").GetString());
        Assert.Equal("Manual", data.GetProperty("rateSource").GetString());
        Assert.Equal(80000, data.GetProperty("tripAmount").GetDecimal());
        Assert.Equal("LHR → DGK", data.GetProperty("routeLabel").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("tripConfigurationId").ValueKind);
    }

    [Fact]
    public async Task An_other_location_needs_a_type_and_a_name_and_labels_the_route_with_it()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ActiveCustomerAsync(admin);
        var cities = await CityIdsByAbbrAsync(admin);
        var truck = await vehicles.ActiveAsync();
        var driverId = await vehicles.DriverAsync();

        var missingName = await admin.PostAsJsonAsync("/api/trips/open", new
        {
            customerId, from = new { locationType = "Other", otherLocationType = "Factory" }, to = new { locationType = "City", cityId = cities["LHR"] },
            vehicleId = VehicleWorld.Id(truck), driverId, tripAmount = 5000, tripDate = "2026-09-01"
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingName.StatusCode);

        var created = await admin.PostAsJsonAsync("/api/trips/open", new
        {
            customerId, from = new { locationType = "Other", otherLocationType = "Factory", otherLocationName = "ABC Textile Mill" },
            to = new { locationType = "City", cityId = cities["LHR"] }, vehicleId = VehicleWorld.Id(truck), driverId, tripAmount = 5000, tripDate = "2026-09-01"
        });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var data = await created.DataAsync();
        Assert.Equal("ABC Textile Mill → LHR", data.GetProperty("routeLabel").GetString());
    }

    [Fact]
    public async Task Intermediate_stops_are_kept_in_order()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ActiveCustomerAsync(admin);
        var cities = await CityIdsByAbbrAsync(admin);
        var truck = await vehicles.ActiveAsync();
        var driverId = await vehicles.DriverAsync();

        var created = await admin.PostAsJsonAsync("/api/trips/open", new
        {
            customerId, from = new { locationType = "City", cityId = cities["LHR"] }, to = new { locationType = "City", cityId = cities["KHI"] },
            stops = new[] { new { locationType = "City", cityId = cities["MUL"] } },
            vehicleId = VehicleWorld.Id(truck), driverId, tripAmount = 90000, tripDate = "2026-09-01"
        });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var data = await created.DataAsync();
        Assert.Equal("LHR → MUL → KHI", data.GetProperty("routeLabel").GetString());
        Assert.Single(data.GetProperty("stops").EnumerateArray());
    }

    [Fact]
    public async Task From_and_to_must_differ_unless_the_trip_is_a_round_trip()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ActiveCustomerAsync(admin);
        var cities = await CityIdsByAbbrAsync(admin);
        var truck = await vehicles.ActiveAsync();
        var driverId = await vehicles.DriverAsync();

        var oneWay = await admin.PostAsJsonAsync("/api/trips/open", new
        {
            customerId, from = new { locationType = "City", cityId = cities["LHR"] }, to = new { locationType = "City", cityId = cities["LHR"] },
            vehicleId = VehicleWorld.Id(truck), driverId, tripAmount = 1000, tripDate = "2026-09-01"
        });
        Assert.Equal(HttpStatusCode.BadRequest, oneWay.StatusCode);

        var roundTrip = await admin.PostAsJsonAsync("/api/trips/open", new
        {
            customerId, from = new { locationType = "City", cityId = cities["LHR"] }, to = new { locationType = "City", cityId = cities["LHR"] },
            isRoundTrip = true, vehicleId = VehicleWorld.Id(truck), driverId, tripAmount = 1000, tripDate = "2026-09-01"
        });
        roundTrip.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Any_operational_vehicle_works_with_no_configuration_at_all_but_a_non_operational_one_is_refused()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ActiveCustomerAsync(admin);
        var cities = await CityIdsByAbbrAsync(admin);
        var truck = await vehicles.ActiveAsync();   // never assigned to any configuration, unlike Fixed trips
        var driverId = await vehicles.DriverAsync();

        var ok = await admin.PostAsJsonAsync("/api/trips/open", new
        {
            customerId, from = new { locationType = "City", cityId = cities["LHR"] }, to = new { locationType = "City", cityId = cities["MUL"] },
            vehicleId = VehicleWorld.Id(truck), driverId, tripAmount = 1000, tripDate = "2026-09-01"
        });
        ok.EnsureSuccessStatusCode();

        var draftTruck = await vehicles.CreateAsync(vehicles.Truck());
        var refused = await admin.PostAsJsonAsync("/api/trips/open", new
        {
            customerId, from = new { locationType = "City", cityId = cities["LHR"] }, to = new { locationType = "City", cityId = cities["MUL"] },
            vehicleId = VehicleWorld.Id(draftTruck), driverId, tripAmount = 1000, tripDate = "2026-09-01"
        });
        Assert.Equal((HttpStatusCode)422, refused.StatusCode);
        Assert.Contains("VEHICLE_NOT_OPERATIONAL", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_non_positive_trip_amount_is_refused()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ActiveCustomerAsync(admin);
        var cities = await CityIdsByAbbrAsync(admin);
        var truck = await vehicles.ActiveAsync();
        var driverId = await vehicles.DriverAsync();

        var response = await admin.PostAsJsonAsync("/api/trips/open", new
        {
            customerId, from = new { locationType = "City", cityId = cities["LHR"] }, to = new { locationType = "City", cityId = cities["MUL"] },
            vehicleId = VehicleWorld.Id(truck), driverId, tripAmount = 0, tripDate = "2026-09-01"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_needs_the_trip_create_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ActiveCustomerAsync(admin);
        var cities = await CityIdsByAbbrAsync(admin);
        var viewer = vehicles.As("Viewer", 5, PermissionCodes.TRP_TRIP_VIEW);

        var response = await viewer.PostAsJsonAsync("/api/trips/open", new
        {
            customerId, from = new { locationType = "City", cityId = cities["LHR"] }, to = new { locationType = "City", cityId = cities["MUL"] },
            vehicleId = 1, driverId = 1, tripAmount = 1000, tripDate = "2026-09-01"
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
