using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-14: driver default and override (§20, AC-17, AC-18).</summary>
[Collection(ApiCollection.Name)]
public sealed class DriverAssignmentTests(ApiFactory factory)
{
    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_TRIP_OVERRIDE_DRIVER]);
        return (vehicles, admin);
    }

    private static async Task<(int CustomerId, Dictionary<string, int> Cities)> ReadyCustomerAsync(HttpClient admin)
    {
        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        var byAbbr = cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "Line 1" })).DataAsync();
        var id = customer.GetProperty("customerId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{id}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();
        return (id, byAbbr);
    }

    private static object OpenTripBody(int customerId, Dictionary<string, int> cities, int vehicleId, int? driverId = null, string? reason = null) => new
    {
        customerId, from = new { locationType = "City", cityId = cities["LHR"] }, to = new { locationType = "City", cityId = cities["MUL"] },
        vehicleId, driverId, driverOverrideReason = reason, tripAmount = 1000, tripDate = "2026-09-01"
    };

    [Fact]
    public async Task AC_17_selecting_the_vehicle_auto_fills_its_default_driver()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, cities) = await ReadyCustomerAsync(admin);
        var truck = await vehicles.ActiveAsync();
        var ali = await vehicles.DriverAsync();
        (await admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(truck)}/driver", new { driverId = ali })).EnsureSuccessStatusCode();

        var created = await admin.PostAsJsonAsync("/api/trips/open", OpenTripBody(customerId, cities, VehicleWorld.Id(truck)));
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var data = await created.DataAsync();
        Assert.Equal(ali, data.GetProperty("driverId").GetInt32());
        Assert.Equal(ali, data.GetProperty("defaultDriverId").GetInt32());
        Assert.False(data.GetProperty("isDriverOverridden").GetBoolean());
    }

    [Fact]
    public async Task AC_18_an_authorised_override_stores_both_drivers_and_the_reason()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, cities) = await ReadyCustomerAsync(admin);
        var truck = await vehicles.ActiveAsync();
        var ali = await vehicles.DriverAsync();
        var bilal = await vehicles.DriverAsync();
        (await admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(truck)}/driver", new { driverId = ali })).EnsureSuccessStatusCode();

        var created = await admin.PostAsJsonAsync("/api/trips/open", OpenTripBody(customerId, cities, VehicleWorld.Id(truck), bilal, "Ali is on leave"));
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var data = await created.DataAsync();
        Assert.Equal(bilal, data.GetProperty("driverId").GetInt32());
        Assert.Equal(ali, data.GetProperty("defaultDriverId").GetInt32());
        Assert.True(data.GetProperty("isDriverOverridden").GetBoolean());
        Assert.Equal("Ali is on leave", data.GetProperty("driverOverrideReason").GetString());
    }

    [Fact]
    public async Task An_override_needs_the_override_permission_and_a_reason()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, cities) = await ReadyCustomerAsync(admin);
        var truck = await vehicles.ActiveAsync();
        var ali = await vehicles.DriverAsync();
        var bilal = await vehicles.DriverAsync();
        (await admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(truck)}/driver", new { driverId = ali })).EnsureSuccessStatusCode();

        var noReason = await admin.PostAsJsonAsync("/api/trips/open", OpenTripBody(customerId, cities, VehicleWorld.Id(truck), bilal));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var noPermission = vehicles.As("NoOverride", 42, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        var forbidden = await noPermission.PostAsJsonAsync("/api/trips/open", OpenTripBody(customerId, cities, VehicleWorld.Id(truck), bilal, "Ali is on leave"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task With_no_default_driver_at_all_a_trip_can_still_be_created_with_no_driver()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, cities) = await ReadyCustomerAsync(admin);
        var truck = await vehicles.ActiveAsync();   // never given a default driver

        var created = await admin.PostAsJsonAsync("/api/trips/open", OpenTripBody(customerId, cities, VehicleWorld.Id(truck)));
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var data = await created.DataAsync();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("driverId").ValueKind);
        Assert.False(data.GetProperty("isDriverOverridden").GetBoolean());
    }

    [Fact]
    public async Task Providing_a_driver_when_the_vehicle_never_had_a_default_is_not_an_override()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, cities) = await ReadyCustomerAsync(admin);
        var truck = await vehicles.ActiveAsync();   // never given a default driver
        var someone = await vehicles.DriverAsync();

        // No override permission held, but this should not need it: nothing was proposed to override.
        var noOverridePermission = vehicles.As("Ordinary", 43, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        var created = await noOverridePermission.PostAsJsonAsync("/api/trips/open", OpenTripBody(customerId, cities, VehicleWorld.Id(truck), someone));
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var data = await created.DataAsync();
        Assert.Equal(someone, data.GetProperty("driverId").GetInt32());
        Assert.False(data.GetProperty("isDriverOverridden").GetBoolean());
    }
}
