using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-21: trip income with a billable flag (§30).</summary>
[Collection(ApiCollection.Name)]
public sealed class TripIncomeTests(ApiFactory factory)
{
    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INCOME_EDIT]);
        return (vehicles, admin);
    }

    private static async Task<(System.Text.Json.JsonElement Trip, int CustomerId)> DraftTripAsync(VehicleWorld vehicles, HttpClient admin)
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
        return (await created.DataAsync(), customerId);
    }

    private static async Task<int> IncomeTypeIdAsync(HttpClient admin, string code)
    {
        var types = await (await admin.GetAsync("/api/lookups/TRIP_INCOME_TYPE")).DataAsync();
        return types.EnumerateArray().First(t => t.GetProperty("code").GetString() == code).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Income_defaults_to_the_trips_own_customer_and_today()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, customerId) = await DraftTripAsync(vehicles, admin);
        var detentionId = await IncomeTypeIdAsync(admin, "DETENTION");

        var response = await admin.PostAsJsonAsync($"/api/trips/{trip.GetProperty("tripId").GetInt64()}/income", new { incomeTypeId = detentionId, amount = 2000, isBillable = true });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var income = await response.DataAsync();
        Assert.Equal(customerId, income.GetProperty("customerId").GetInt32());
        Assert.True(income.GetProperty("isBillable").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, income.GetProperty("invoiceLineId").ValueKind);
    }

    [Fact]
    public async Task An_invalid_income_type_or_non_positive_amount_is_rejected()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var detentionId = await IncomeTypeIdAsync(admin, "DETENTION");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/trips/{tripId}/income", new { incomeTypeId = 999999, amount = 100 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/trips/{tripId}/income", new { incomeTypeId = detentionId, amount = 0 })).StatusCode);
    }

    [Fact]
    public async Task Income_can_be_billed_to_a_different_active_customer()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var (otherTrip, otherCustomerId) = await DraftTripAsync(vehicles, admin);
        var detentionId = await IncomeTypeIdAsync(admin, "DETENTION");

        var response = await admin.PostAsJsonAsync($"/api/trips/{trip.GetProperty("tripId").GetInt64()}/income",
            new { incomeTypeId = detentionId, amount = 1500, customerId = otherCustomerId });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(otherCustomerId, (await response.DataAsync()).GetProperty("customerId").GetInt32());
    }

    [Fact]
    public async Task Voiding_needs_a_reason_and_locks_after_the_first_void()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var extraDropId = await IncomeTypeIdAsync(admin, "EXTRA_DROP");
        var created = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/income", new { incomeTypeId = extraDropId, amount = 800 })).DataAsync();
        var incomeId = created.GetProperty("tripIncomeId").GetInt64();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/trip-income/{incomeId}/void", new { })).StatusCode);

        var voided = await (await admin.PostAsJsonAsync($"/api/trip-income/{incomeId}/void", new { reason = "Wrong trip" })).DataAsync();
        Assert.True(voided.GetProperty("isVoided").GetBoolean());

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/trip-income/{incomeId}/void", new { reason = "Again" })).StatusCode);
    }

    [Fact]
    public async Task Billed_income_is_locked_and_cannot_be_voided()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var lossRecoveryId = await IncomeTypeIdAsync(admin, "LOADING_RECOVERY");
        var created = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/income", new { incomeTypeId = lossRecoveryId, amount = 500, isBillable = true })).DataAsync();
        var incomeId = created.GetProperty("tripIncomeId").GetInt64();

        // No invoicing module exists yet — simulate "already billed" directly, the same stand-in used for the
        // trip-repricing task's own "on an active invoice" exclusion.
        factory.Execute("UPDATE trp.TripIncomes SET InvoiceLineId = 555 WHERE TripIncomeId = @id", ("@id", incomeId));

        var response = await admin.PostAsJsonAsync($"/api/trip-income/{incomeId}/void", new { reason = "Trying anyway" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Billable_unbilled_income_is_exposed_for_a_future_invoice_to_read()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, customerId) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var detentionId = await IncomeTypeIdAsync(admin, "DETENTION");
        var billable = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/income", new { incomeTypeId = detentionId, amount = 1000, isBillable = true })).DataAsync();
        await admin.PostAsJsonAsync($"/api/trips/{tripId}/income", new { incomeTypeId = detentionId, amount = 2000, isBillable = false });

        var list = await (await admin.GetAsync($"/api/trip-income/billable?customerId={customerId}")).DataAsync();
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(billable.GetProperty("tripIncomeId").GetInt64(), list[0].GetProperty("tripIncomeId").GetInt64());
    }

    [Fact]
    public async Task Income_actions_need_the_income_edit_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var noPermission = vehicles.As("NoIncome", 66, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.GetAsync($"/api/trips/{trip.GetProperty("tripId").GetInt64()}/income")).StatusCode);
    }
}
