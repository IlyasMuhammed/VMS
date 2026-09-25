using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-15: the trip lifecycle state machine, completion date, active flag (§24, AC-21).</summary>
[Collection(ApiCollection.Name)]
public sealed class TripLifecycleTests(ApiFactory factory)
{
    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        return (vehicles, admin);
    }

    /// <summary>A Draft fixed trip, ready to move through the lifecycle — a rated, driver-assigned trip so every
    /// transition (including Started's odometer requirement) has what it needs.</summary>
    private static async Task<System.Text.Json.JsonElement> DraftTripAsync(VehicleWorld vehicles, HttpClient admin, string? tripDate = null)
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
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount = 25000 })).EnsureSuccessStatusCode();

        var driverId = await vehicles.DriverAsync();
        var created = await admin.PostAsJsonAsync("/api/trips",
            new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = tripDate ?? "2026-07-10" });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        return await created.DataAsync();
    }

    private static async Task<System.Text.Json.JsonElement> TransitionAsync(HttpClient client, long tripId, string toStatus, string rowVersion, object? body = null)
    {
        var payload = body switch
        {
            null => new { rowVersion },
            _ => Merge(body, rowVersion)
        };
        var response = await client.PostAsJsonAsync($"/api/trips/{tripId}/status/{toStatus}", payload);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.DataAsync();
    }

    private static object Merge(object body, string rowVersion)
    {
        var dict = System.Text.Json.JsonSerializer.SerializeToElement(body).EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value);
        dict["rowVersion"] = rowVersion;
        return dict;
    }

    [Fact]
    public async Task A_fixed_trip_moves_through_the_normal_transitions_to_completed()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var rv = trip.GetProperty("rowVersion").GetString()!;

        var planned = await TransitionAsync(admin, tripId, "Planned", rv);
        Assert.Equal("Planned", planned.GetProperty("status").GetString());
        rv = planned.GetProperty("rowVersion").GetString()!;

        var assigned = await TransitionAsync(admin, tripId, "Assigned", rv);
        Assert.Equal("Assigned", assigned.GetProperty("status").GetString());
        rv = assigned.GetProperty("rowVersion").GetString()!;

        var started = await TransitionAsync(admin, tripId, "Started", rv, new { startOdometer = 1000 });
        Assert.Equal("Started", started.GetProperty("status").GetString());
        rv = started.GetProperty("rowVersion").GetString()!;

        var inTransit = await TransitionAsync(admin, tripId, "InTransit", rv);
        rv = inTransit.GetProperty("rowVersion").GetString()!;

        var atDelivery = await TransitionAsync(admin, tripId, "AtDelivery", rv);
        rv = atDelivery.GetProperty("rowVersion").GetString()!;

        var delivered = await TransitionAsync(admin, tripId, "Delivered", rv, new { endOdometer = 1200 });
        Assert.Equal("Delivered", delivered.GetProperty("status").GetString());
        rv = delivered.GetProperty("rowVersion").GetString()!;

        var completed = await TransitionAsync(admin, tripId, "Completed", rv);
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
        Assert.False(completed.GetProperty("completionDate").ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task AC_21_assigned_to_completed_directly_is_rejected()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var rv = trip.GetProperty("rowVersion").GetString()!;
        var planned = await TransitionAsync(admin, tripId, "Planned", rv);
        rv = planned.GetProperty("rowVersion").GetString()!;
        var assigned = await TransitionAsync(admin, tripId, "Assigned", rv);
        rv = assigned.GetProperty("rowVersion").GetString()!;

        var response = await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Completed", new { rowVersion = rv });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
    }

    [Fact]
    public async Task Skipping_a_step_needs_the_skip_status_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var rv = trip.GetProperty("rowVersion").GetString()!;
        var planned = await TransitionAsync(admin, tripId, "Planned", rv);
        rv = planned.GetProperty("rowVersion").GetString()!;

        // Planned -> Started directly is not a normal edge (Assigned sits between them).
        var refused = await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Started", new { rowVersion = rv, startOdometer = 1000 });
        Assert.Equal((HttpStatusCode)422, refused.StatusCode);

        var withSkip = vehicles.As("Can Skip", 7, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_TRIP_SKIPSTATUS]);
        var allowed = await withSkip.PostAsJsonAsync($"/api/trips/{tripId}/status/Started", new { rowVersion = rv, startOdometer = 1000 });
        Assert.True(allowed.IsSuccessStatusCode, await allowed.Content.ReadAsStringAsync());
        Assert.Equal("Started", (await allowed.DataAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Started_requires_start_odometer_and_delivered_requires_end_odometer()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var rv = trip.GetProperty("rowVersion").GetString()!;
        var planned = await TransitionAsync(admin, tripId, "Planned", rv);
        var assigned = await TransitionAsync(admin, tripId, "Assigned", planned.GetProperty("rowVersion").GetString()!);
        rv = assigned.GetProperty("rowVersion").GetString()!;

        var noOdometer = await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Started", new { rowVersion = rv });
        Assert.Equal(HttpStatusCode.BadRequest, noOdometer.StatusCode);
    }

    [Fact]
    public async Task Assigned_needs_a_driver_on_the_trip()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        // A trip built with no driver so the Assigned transition itself must be refused.
        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        var byAbbr = cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "Line 1" })).DataAsync();
        var customerId = customer.GetProperty("customerId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();
        var truck = await vehicles.ActiveAsync();   // never given a default driver
        var created = await (await admin.PostAsJsonAsync("/api/trips/open", new
        {
            customerId, from = new { locationType = "City", cityId = byAbbr["LHR"] }, to = new { locationType = "City", cityId = byAbbr["MUL"] },
            vehicleId = VehicleWorld.Id(truck), tripAmount = 1000, tripDate = "2026-09-01"
        })).DataAsync();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, created.GetProperty("driverId").ValueKind);
        var tripId = created.GetProperty("tripId").GetInt64();
        var rv = created.GetProperty("rowVersion").GetString()!;
        var planned = await TransitionAsync(admin, tripId, "Planned", rv);

        var response = await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Assigned", new { rowVersion = planned.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Hold_and_resume_return_to_the_previous_status()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var planned = await TransitionAsync(admin, tripId, "Planned", trip.GetProperty("rowVersion").GetString()!);
        var rv = planned.GetProperty("rowVersion").GetString()!;

        var held = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/hold", new { reason = "Waiting on customer", rowVersion = rv })).DataAsync();
        Assert.Equal("OnHold", held.GetProperty("status").GetString());
        Assert.Equal("Planned", held.GetProperty("heldFromStatus").GetString());
        Assert.Equal("Waiting on customer", held.GetProperty("holdReason").GetString());
        rv = held.GetProperty("rowVersion").GetString()!;

        var resumed = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/resume", new { rowVersion = rv })).DataAsync();
        Assert.Equal("Planned", resumed.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, resumed.GetProperty("heldFromStatus").ValueKind);
    }

    [Fact]
    public async Task Hold_needs_a_reason()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var planned = await TransitionAsync(admin, tripId, "Planned", trip.GetProperty("rowVersion").GetString()!);

        var response = await admin.PostAsJsonAsync($"/api/trips/{tripId}/hold", new { reason = "", rowVersion = planned.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cancelling_after_started_needs_the_elevated_status_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var planned = await TransitionAsync(admin, tripId, "Planned", trip.GetProperty("rowVersion").GetString()!);
        var assigned = await TransitionAsync(admin, tripId, "Assigned", planned.GetProperty("rowVersion").GetString()!);
        var started = await TransitionAsync(admin, tripId, "Started", assigned.GetProperty("rowVersion").GetString()!, new { startOdometer = 500 });

        // A caller with only view + create (no TRP.TRIP.STATUS) cannot cancel a Started trip.
        var limited = vehicles.As("Limited", 9, PermissionCodes.TRP_TRIP_VIEW, PermissionCodes.TRP_TRIP_CREATE);
        var forbidden = await limited.PostAsJsonAsync($"/api/trips/{tripId}/cancel", new { reason = "Breakdown", rowVersion = started.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var cancelled = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/cancel", new { reason = "Breakdown", rowVersion = started.GetProperty("rowVersion").GetString() })).DataAsync();
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal("Breakdown", cancelled.GetProperty("cancelReason").GetString());
    }

    [Fact]
    public async Task A_cancelled_trip_cannot_be_transitioned_again()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var cancelled = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/cancel", new { reason = "Customer cancelled", rowVersion = trip.GetProperty("rowVersion").GetString() })).DataAsync();

        var response = await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Planned", new { rowVersion = cancelled.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
    }

    [Fact]
    public async Task Reopen_needs_the_reopen_permission_and_only_works_from_completed()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        // Not yet Completed — reopening is refused regardless of permission.
        var tooEarly = await admin.PostAsJsonAsync($"/api/trips/{tripId}/reopen", new { rowVersion = trip.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);

        var planned = await TransitionAsync(admin, tripId, "Planned", trip.GetProperty("rowVersion").GetString()!);
        var assigned = await TransitionAsync(admin, tripId, "Assigned", planned.GetProperty("rowVersion").GetString()!);
        var started = await TransitionAsync(admin, tripId, "Started", assigned.GetProperty("rowVersion").GetString()!, new { startOdometer = 500 });
        var inTransit = await TransitionAsync(admin, tripId, "InTransit", started.GetProperty("rowVersion").GetString()!);
        var atDelivery = await TransitionAsync(admin, tripId, "AtDelivery", inTransit.GetProperty("rowVersion").GetString()!);
        var delivered = await TransitionAsync(admin, tripId, "Delivered", atDelivery.GetProperty("rowVersion").GetString()!, new { endOdometer = 700 });
        var completed = await TransitionAsync(admin, tripId, "Completed", delivered.GetProperty("rowVersion").GetString()!);

        var noPermission = vehicles.As("No Reopen", 11, [.. TripsWorld.Everything.Where(p => p != PermissionCodes.TRP_TRIP_REOPEN)]);
        var forbidden = await noPermission.PostAsJsonAsync($"/api/trips/{tripId}/reopen", new { rowVersion = completed.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var reopened = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/reopen", new { rowVersion = completed.GetProperty("rowVersion").GetString() })).DataAsync();
        Assert.Equal("Delivered", reopened.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, reopened.GetProperty("completionDate").ValueKind);
    }

    [Fact]
    public async Task Inactivate_and_reactivate_round_trip_with_a_reason()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        var noReason = await admin.PostAsJsonAsync($"/api/trips/{tripId}/inactivate", new { rowVersion = trip.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var inactivated = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/inactivate", new { reason = "Duplicate entry", rowVersion = trip.GetProperty("rowVersion").GetString() })).DataAsync();
        Assert.False(inactivated.GetProperty("isActive").GetBoolean());
        Assert.Equal("Duplicate entry", inactivated.GetProperty("inactiveReason").GetString());

        var again = await admin.PostAsJsonAsync($"/api/trips/{tripId}/inactivate", new { reason = "Again", rowVersion = inactivated.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var reactivated = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/reactivate", new { rowVersion = inactivated.GetProperty("rowVersion").GetString() })).DataAsync();
        Assert.True(reactivated.GetProperty("isActive").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, reactivated.GetProperty("inactiveReason").ValueKind);
    }

    [Fact]
    public async Task The_trips_own_driver_can_transition_it_without_the_status_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var driverId = trip.GetProperty("driverId").GetInt32();
        var tripId = trip.GetProperty("tripId").GetInt64();

        var driverClient = factory.CreateClient().WithToken(
            TestTokens.ForScoped(vehicles.Tenant, "Own Driver", driverId, null, null, driverId));

        var planned = await (await driverClient.PostAsJsonAsync($"/api/trips/{tripId}/status/Planned", new { rowVersion = trip.GetProperty("rowVersion").GetString() })).DataAsync();
        Assert.Equal("Planned", planned.GetProperty("status").GetString());

        // A different partner (not the assigned driver, no TRP.TRIP.STATUS) is refused.
        var someoneElseId = await vehicles.DriverAsync();
        var otherDriverClient = factory.CreateClient().WithToken(
            TestTokens.ForScoped(vehicles.Tenant, "Other Driver", someoneElseId, null, null, someoneElseId));
        var forbidden = await otherDriverClient.PostAsJsonAsync($"/api/trips/{tripId}/status/Assigned", new { rowVersion = planned.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task An_unknown_status_name_is_rejected()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        var response = await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/NotARealStatus", new { rowVersion = trip.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_stale_row_version_is_refused_as_a_concurrency_conflict()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        await TransitionAsync(admin, tripId, "Planned", trip.GetProperty("rowVersion").GetString()!);

        // Reusing the trip's very first (now stale) row version.
        var response = await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Assigned", new { rowVersion = trip.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
