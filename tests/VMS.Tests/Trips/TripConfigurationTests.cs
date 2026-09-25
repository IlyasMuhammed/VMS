using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-10: trip configurations, stops and allowed vehicles (§18, §19) — the first task combining
/// Customer, Route/City and the existing (first-FSD) Vehicle module.</summary>
[Collection(ApiCollection.Name)]
public sealed class TripConfigurationTests(ApiFactory factory)
{
    /// <summary>A single tenant holding both this module's own data and a real Vehicle (from the first FSD's
    /// module) — <see cref="VehicleWorld"/> creates its own tenant, and this admin token reaches every permission
    /// in both modules against that same tenant.</summary>
    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        return (vehicles, admin);
    }

    private static async Task<Dictionary<string, int>> CityIdsByAbbrAsync(HttpClient admin)
    {
        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        return cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());
    }

    private static async Task<int> ActiveCustomerAsync(HttpClient admin, string name)
    {
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = name, addressLine1 = "Line 1" })).DataAsync();
        var id = customer.GetProperty("customerId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{id}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<(int CustomerId, int RouteId)> ActiveCustomerAndRouteAsync(HttpClient admin)
    {
        var cities = await CityIdsByAbbrAsync(admin);
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "Line 1" })).DataAsync();
        var customerId = customer.GetProperty("customerId").GetInt32();
        // Force-activate for tests that only care about trip configurations, not the customer's own checklist.
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();

        var route = await (await admin.PostAsJsonAsync("/api/routes", new
        {
            routeName = "Lahore - Faisalabad",
            stops = new[] { new { cityId = cities["LHR"], stopType = "Origin" }, new { cityId = cities["FSD"], stopType = "Destination" } }
        })).DataAsync();
        return (customerId, route.GetProperty("routeId").GetInt32());
    }

    [Fact]
    public async Task A_configuration_copies_its_stops_from_the_route_and_auto_suggests_its_trip_code()
    {
        var (_, admin) = await WorldAsync(factory);
        var (customerId, routeId) = await ActiveCustomerAndRouteAsync(admin);

        var created = await admin.PostAsJsonAsync("/api/trip-configurations", new { customerId, name = "Daily LHR-FSD", routeId, directionType = "OneWay" });
        created.EnsureSuccessStatusCode();
        var data = await created.DataAsync();
        Assert.Equal("Draft", data.GetProperty("status").GetString());
        Assert.Matches(@"^[A-Z0-9]+-LHR-FSD-01$", data.GetProperty("tripCode").GetString()!);
        Assert.Equal(2, data.GetProperty("stops").GetArrayLength());
    }

    [Fact]
    public async Task Two_customers_on_the_same_route_get_independent_configurations()
    {
        var (_, admin) = await WorldAsync(factory);
        var (customerA, routeId) = await ActiveCustomerAndRouteAsync(admin);
        var customerB = await ActiveCustomerAsync(admin, "Customer B");

        (await admin.PostAsJsonAsync("/api/trip-configurations", new { customerId = customerA, name = "A's config", routeId, directionType = "OneWay" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/trip-configurations", new { customerId = customerB, name = "B's config", routeId, directionType = "OneWay" })).EnsureSuccessStatusCode();

        var forA = await (await admin.GetAsync($"/api/customers/{customerA}/trip-configurations")).DataAsync();
        Assert.Single(forA.EnumerateArray());
        Assert.Equal("A's config", forA[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_new_configuration_needs_an_active_customer_and_an_active_route()
    {
        var (_, admin) = await WorldAsync(factory);
        var (customerId, routeId) = await ActiveCustomerAndRouteAsync(admin);

        var draftCustomer = (await (await admin.PostAsJsonAsync("/api/customers", new { customerName = "Still Draft", addressLine1 = "Line 1" })).DataAsync()).GetProperty("customerId").GetInt32();
        Assert.Equal((HttpStatusCode)422, (await admin.PostAsJsonAsync("/api/trip-configurations", new { customerId = draftCustomer, name = "X", routeId, directionType = "OneWay" })).StatusCode);

        var routes = await (await admin.GetAsync("/api/routes")).DataAsync();
        (await admin.PutAsJsonAsync($"/api/routes/{routeId}", new { routeName = "Lahore - Faisalabad", status = "Inactive" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/trip-configurations", new { customerId, name = "X", routeId, directionType = "OneWay" })).StatusCode);
    }

    [Fact]
    public async Task Activation_needs_at_least_one_active_vehicle()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, routeId) = await ActiveCustomerAndRouteAsync(admin);
        var created = await (await admin.PostAsJsonAsync("/api/trip-configurations", new { customerId, name = "X", routeId, directionType = "OneWay" })).DataAsync();
        var configId = created.GetProperty("tripConfigurationId").GetInt64();
        var rowVersion = created.GetProperty("rowVersion").GetString();

        var noVehicle = await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/activate", new { rowVersion });
        Assert.Equal((HttpStatusCode)422, noVehicle.StatusCode);
        Assert.Contains("TRIPCONFIG_NO_ACTIVE_VEHICLE", await noVehicle.Content.ReadAsStringAsync());

        var truck = await vehicles.ActiveAsync();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId = VehicleWorld.Id(truck) })).EnsureSuccessStatusCode();

        var activated = await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/activate", new { rowVersion });
        activated.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_non_operational_vehicle_is_refused_and_overlapping_ranges_on_the_same_vehicle_are_rejected()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, routeId) = await ActiveCustomerAndRouteAsync(admin);
        var config = await (await admin.PostAsJsonAsync("/api/trip-configurations", new { customerId, name = "X", routeId, directionType = "OneWay" })).DataAsync();
        var configId = config.GetProperty("tripConfigurationId").GetInt64();

        var draftTruck = await vehicles.CreateAsync(vehicles.Truck());   // still Draft — not operational
        var refused = await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId = VehicleWorld.Id(draftTruck) });
        Assert.Equal((HttpStatusCode)422, refused.StatusCode);
        Assert.Contains("VEHICLE_NOT_OPERATIONAL", await refused.Content.ReadAsStringAsync());

        var activeTruck = await vehicles.ActiveAsync();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId = VehicleWorld.Id(activeTruck), effectiveFrom = "2026-01-01", effectiveTo = "2026-06-30" })).EnsureSuccessStatusCode();

        var overlap = await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId = VehicleWorld.Id(activeTruck), effectiveFrom = "2026-05-01" });
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);

        var noOverlap = await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId = VehicleWorld.Id(activeTruck), effectiveFrom = "2026-07-01" });
        noOverlap.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_same_vehicle_may_be_allowed_on_several_configurations_and_customers()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var truck = await vehicles.ActiveAsync();
        var (customerA, routeId) = await ActiveCustomerAndRouteAsync(admin);
        var customerB = await ActiveCustomerAsync(admin, "Customer B");

        var configA = await (await admin.PostAsJsonAsync("/api/trip-configurations", new { customerId = customerA, name = "A", routeId, directionType = "OneWay" })).DataAsync();
        var configB = await (await admin.PostAsJsonAsync("/api/trip-configurations", new { customerId = customerB, name = "B", routeId, directionType = "OneWay" })).DataAsync();

        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configA.GetProperty("tripConfigurationId").GetInt64()}/vehicles", new { vehicleId = VehicleWorld.Id(truck) })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configB.GetProperty("tripConfigurationId").GetInt64()}/vehicles", new { vehicleId = VehicleWorld.Id(truck) })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Copying_a_configuration_copies_stops_and_vehicles_to_a_new_draft()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, routeId) = await ActiveCustomerAndRouteAsync(admin);
        var truck = await vehicles.ActiveAsync();
        var source = await (await admin.PostAsJsonAsync("/api/trip-configurations", new { customerId, name = "Original", routeId, directionType = "OneWay" })).DataAsync();
        var sourceId = source.GetProperty("tripConfigurationId").GetInt64();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{sourceId}/vehicles", new { vehicleId = VehicleWorld.Id(truck) })).EnsureSuccessStatusCode();

        var copy = await (await admin.PostAsJsonAsync($"/api/trip-configurations/{sourceId}/copy", new { name = "Copy of original" })).DataAsync();
        Assert.Equal("Draft", copy.GetProperty("status").GetString());
        Assert.Equal(2, copy.GetProperty("stops").GetArrayLength());
        Assert.NotEqual(source.GetProperty("tripCode").GetString(), copy.GetProperty("tripCode").GetString());

        var copyVehicles = await (await admin.GetAsync($"/api/trip-configurations/{copy.GetProperty("tripConfigurationId").GetInt64()}/vehicles")).DataAsync();
        Assert.Single(copyVehicles.EnumerateArray());
    }

    [Fact]
    public async Task View_permission_alone_cannot_create_a_configuration()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, routeId) = await ActiveCustomerAndRouteAsync(admin);
        var viewer = vehicles.As("Viewer", 5, PermissionCodes.TRP_TRIPCONFIG_VIEW);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/customers/{customerId}/trip-configurations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/trip-configurations", new { customerId, name = "X", routeId, directionType = "OneWay" })).StatusCode);
    }
}
