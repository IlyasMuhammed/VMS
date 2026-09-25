using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-19: trip fuel (§27, AC-23).</summary>
[Collection(ApiCollection.Name)]
public sealed class TripFuelTests(ApiFactory factory)
{
    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_FUEL_EDIT]);
        return (vehicles, admin);
    }

    private static async Task<System.Text.Json.JsonElement> DraftTripAsync(VehicleWorld vehicles, HttpClient admin)
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
        var created = await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = "2026-07-10" });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        return await created.DataAsync();
    }

    private static async Task<System.Text.Json.JsonElement> StartedTripAsync(VehicleWorld vehicles, HttpClient admin)
    {
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var planned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Planned", new { rowVersion = trip.GetProperty("rowVersion").GetString() })).DataAsync();
        var assigned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Assigned", new { rowVersion = planned.GetProperty("rowVersion").GetString() })).DataAsync();
        var started = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Started", new { rowVersion = assigned.GetProperty("rowVersion").GetString(), startOdometer = 1000 })).DataAsync();
        return started;
    }

    [Fact]
    public async Task Logging_cash_fuel_defaults_the_amount_to_quantity_times_rate()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await StartedTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        var response = await admin.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 10, rate = 280, paymentMethod = "Cash" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var fuel = await response.DataAsync();
        Assert.Equal(2800, fuel.GetProperty("amount").GetDecimal());
        Assert.False(fuel.GetProperty("isVoided").GetBoolean());
    }

    [Fact]
    public async Task AC_23_fuel_card_required_when_payment_method_is_fuel_card()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await StartedTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        var response = await admin.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 10, rate = 280, paymentMethod = "FuelCard" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_fuel_card_must_be_assigned_to_the_trips_vehicle_or_driver()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await StartedTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var companyId = await vehicles.PartnerAsync("FuelCardCompany");
        var card = await (await admin.PostAsJsonAsync("/api/fuel-cards", new
        {
            cardNumber = $"CARD-{Guid.NewGuid():N}"[..14], fuelCardCompanyId = companyId, cardHolderName = "Ali", expiryDate = "2027-01-01", monthlyLimit = 50000
        })).DataAsync();
        var cardId = card.GetProperty("fuelCardId").GetInt32();

        var notAssigned = await admin.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 10, rate = 280, paymentMethod = "FuelCard", fuelCardId = cardId });
        Assert.Equal(HttpStatusCode.BadRequest, notAssigned.StatusCode);

        (await admin.PostAsJsonAsync($"/api/fuel-cards/{cardId}/assign",
            new { vehicleId = trip.GetProperty("vehicleId").GetInt32(), assignedFrom = "2020-01-01", rowVersion = card.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();

        var assigned = await admin.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 10, rate = 280, paymentMethod = "FuelCard", fuelCardId = cardId });
        Assert.True(assigned.IsSuccessStatusCode, await assigned.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_inactive_fuel_card_is_refused()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await StartedTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var companyId = await vehicles.PartnerAsync("FuelCardCompany");
        var card = await (await admin.PostAsJsonAsync("/api/fuel-cards", new
        {
            cardNumber = $"CARD-{Guid.NewGuid():N}"[..14], fuelCardCompanyId = companyId, cardHolderName = "Ali", expiryDate = "2027-01-01", monthlyLimit = 50000
        })).DataAsync();
        var cardId = card.GetProperty("fuelCardId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/fuel-cards/{cardId}/assign",
            new { vehicleId = trip.GetProperty("vehicleId").GetInt32(), assignedFrom = "2020-01-01", rowVersion = card.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();
        var reloaded = await (await admin.GetAsync($"/api/fuel-cards/{cardId}")).DataAsync();
        await admin.PutAsJsonAsync($"/api/fuel-cards/{cardId}", new { expiryDate = "2027-01-01", status = "Blocked", rowVersion = reloaded.GetProperty("rowVersion").GetString() });

        var response = await admin.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 10, rate = 280, paymentMethod = "FuelCard", fuelCardId = cardId });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Other_payment_method_needs_a_description()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await StartedTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        var response = await admin.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 10, rate = 280, paymentMethod = "Other" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var withText = await admin.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 10, rate = 280, paymentMethod = "Other", otherPaymentText = "Company account" });
        Assert.True(withText.IsSuccessStatusCode, await withText.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_amount_far_from_quantity_times_rate_warns_but_still_saves()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await StartedTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        var response = await admin.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 10, rate = 280, amount = 5000, paymentMethod = "Cash" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var fuel = await response.DataAsync();
        Assert.Equal(5000, fuel.GetProperty("amount").GetDecimal());
        Assert.Contains(fuel.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("Amount"));
    }

    [Fact]
    public async Task Voiding_a_fuel_entry_excludes_it_from_totals_but_keeps_it_listed()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await StartedTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var fuel = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 10, rate = 280, paymentMethod = "Cash" })).DataAsync();

        var noReason = await admin.PostAsJsonAsync($"/api/trip-fuel/{fuel.GetProperty("tripFuelId").GetInt64()}/void", new { });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var voided = await (await admin.PostAsJsonAsync($"/api/trip-fuel/{fuel.GetProperty("tripFuelId").GetInt64()}/void", new { reason = "Duplicate entry" })).DataAsync();
        Assert.True(voided.GetProperty("isVoided").GetBoolean());

        var list = await (await admin.GetAsync($"/api/trips/{tripId}/fuel")).DataAsync();
        Assert.Equal(1, list.GetProperty("entries").GetArrayLength());
        Assert.Equal(0, list.GetProperty("totalQuantity").GetDecimal());

        var again = await admin.PostAsJsonAsync($"/api/trip-fuel/{fuel.GetProperty("tripFuelId").GetInt64()}/void", new { reason = "Again" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Fuel_efficiency_is_derived_from_the_trips_own_start_and_end_odometer()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await StartedTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        (await admin.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 20, rate = 280, paymentMethod = "Cash" })).EnsureSuccessStatusCode();

        var inTransit = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/InTransit", new { rowVersion = trip.GetProperty("rowVersion").GetString() })).DataAsync();
        var atDelivery = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/AtDelivery", new { rowVersion = inTransit.GetProperty("rowVersion").GetString() })).DataAsync();
        await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Delivered", new { rowVersion = atDelivery.GetProperty("rowVersion").GetString(), endOdometer = 1200 });

        var list = await (await admin.GetAsync($"/api/trips/{tripId}/fuel")).DataAsync();
        Assert.Equal(10, list.GetProperty("fuelEfficiencyKmPerLitre").GetDecimal());   // (1200-1000)/20
    }

    [Fact]
    public async Task Fuel_actions_need_the_fuel_edit_permission_or_being_the_trips_own_driver()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await StartedTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var driverId = trip.GetProperty("driverId").GetInt32();

        var stranger = vehicles.As("Stranger", 77);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 10, rate = 280, paymentMethod = "Cash" })).StatusCode);

        var driverClient = factory.CreateClient().WithToken(TestTokens.ForScoped(vehicles.Tenant, "Own Driver", driverId, null, null, driverId));
        var response = await driverClient.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 10, rate = 280, paymentMethod = "Cash" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var fuel = await response.DataAsync();
        Assert.Equal("DriverApp", fuel.GetProperty("source").GetString());
    }
}
