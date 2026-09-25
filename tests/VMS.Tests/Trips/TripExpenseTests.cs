using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-20: trip expenses with approval (§29, AC-24).</summary>
[Collection(ApiCollection.Name)]
public sealed class TripExpenseTests(ApiFactory factory)
{
    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_EXPENSE_EDIT, PermissionCodes.TRP_EXPENSE_APPROVE]);
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

    private static async Task<Dictionary<string, int>> ExpenseTypesByCodeAsync(HttpClient admin)
    {
        var types = await (await admin.GetAsync("/api/lookups/TRIP_EXPENSE_TYPE")).DataAsync();
        return types.EnumerateArray().ToDictionary(t => t.GetProperty("code").GetString()!, t => t.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task A_back_office_expense_starts_approved()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var types = await ExpenseTypesByCodeAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/trips/{trip.GetProperty("tripId").GetInt64()}/expenses",
            new { expenseTypeId = types["TOLL_TAX"], amount = 500, paymentMethod = "Cash" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var expense = await response.DataAsync();
        Assert.Equal("Approved", expense.GetProperty("approvalStatus").GetString());
        Assert.Equal("Manual", expense.GetProperty("source").GetString());
    }

    [Fact]
    public async Task A_driver_app_expense_starts_pending_and_can_be_approved()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var driverId = trip.GetProperty("driverId").GetInt32();
        var types = await ExpenseTypesByCodeAsync(admin);

        var driverClient = factory.CreateClient().WithToken(TestTokens.ForScoped(vehicles.Tenant, "Own Driver", driverId, null, null, driverId));
        var created = await (await driverClient.PostAsJsonAsync($"/api/trips/{tripId}/expenses",
            new { expenseTypeId = types["PARKING"], amount = 200, paymentMethod = "PaidByDriver" })).DataAsync();
        Assert.Equal("Pending", created.GetProperty("approvalStatus").GetString());
        Assert.Equal("DriverApp", created.GetProperty("source").GetString());

        var approved = await (await admin.PostAsJsonAsync($"/api/trip-expenses/{created.GetProperty("tripExpenseId").GetInt64()}/approve", new { approved = true })).DataAsync();
        Assert.Equal("Approved", approved.GetProperty("approvalStatus").GetString());
    }

    [Fact]
    public async Task AC_24_other_expense_type_needs_its_own_description()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var types = await ExpenseTypesByCodeAsync(admin);

        var noText = await admin.PostAsJsonAsync($"/api/trips/{tripId}/expenses", new { expenseTypeId = types["OTHER"], amount = 300, paymentMethod = "Cash" });
        Assert.Equal(HttpStatusCode.BadRequest, noText.StatusCode);

        var withText = await admin.PostAsJsonAsync($"/api/trips/{tripId}/expenses", new { expenseTypeId = types["OTHER"], amount = 300, paymentMethod = "Cash", otherExpenseType = "Ferry crossing" });
        Assert.True(withText.IsSuccessStatusCode, await withText.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Fuel_cannot_be_selected_as_an_expense_type()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var types = await ExpenseTypesByCodeAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/trips/{trip.GetProperty("tripId").GetInt64()}/expenses", new { expenseTypeId = types["FUEL"], amount = 1000, paymentMethod = "Cash" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Amount_defaults_to_quantity_times_rate_when_both_are_given()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var types = await ExpenseTypesByCodeAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/trips/{trip.GetProperty("tripId").GetInt64()}/expenses",
            new { expenseTypeId = types["LOADING_UNLOADING"], quantity = 4, rate = 150, paymentMethod = "Cash" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(600, (await response.DataAsync()).GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Rejecting_an_expense_needs_a_reason_and_locks_further_decisions()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var driverId = trip.GetProperty("driverId").GetInt32();
        var types = await ExpenseTypesByCodeAsync(admin);
        var driverClient = factory.CreateClient().WithToken(TestTokens.ForScoped(vehicles.Tenant, "Own Driver", driverId, null, null, driverId));
        var created = await (await driverClient.PostAsJsonAsync($"/api/trips/{tripId}/expenses", new { expenseTypeId = types["REPAIR"], amount = 3000, paymentMethod = "PaidByDriver" })).DataAsync();
        var expenseId = created.GetProperty("tripExpenseId").GetInt64();

        var noReason = await admin.PostAsJsonAsync($"/api/trip-expenses/{expenseId}/approve", new { approved = false });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var rejected = await (await admin.PostAsJsonAsync($"/api/trip-expenses/{expenseId}/approve", new { approved = false, reason = "No receipt" })).DataAsync();
        Assert.Equal("Rejected", rejected.GetProperty("approvalStatus").GetString());
        Assert.Equal("No receipt", rejected.GetProperty("rejectionReason").GetString());

        var again = await admin.PostAsJsonAsync($"/api/trip-expenses/{expenseId}/approve", new { approved = true });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Voiding_an_expense_never_deletes_it()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var types = await ExpenseTypesByCodeAsync(admin);
        var created = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/expenses", new { expenseTypeId = types["PARKING"], amount = 100, paymentMethod = "Cash" })).DataAsync();
        var expenseId = created.GetProperty("tripExpenseId").GetInt64();

        var voided = await (await admin.PostAsJsonAsync($"/api/trip-expenses/{expenseId}/void", new { reason = "Duplicate entry" })).DataAsync();
        Assert.True(voided.GetProperty("isVoided").GetBoolean());

        var list = await (await admin.GetAsync($"/api/trips/{tripId}/expenses")).DataAsync();
        Assert.Equal(1, list.GetArrayLength());
        Assert.True(list[0].GetProperty("isVoided").GetBoolean());
    }

    [Fact]
    public async Task Expense_actions_need_permission_or_being_the_trips_own_driver()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var trip = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var types = await ExpenseTypesByCodeAsync(admin);
        var stranger = vehicles.As("Stranger", 88);

        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/trips/{tripId}/expenses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await stranger.PostAsJsonAsync($"/api/trips/{tripId}/expenses", new { expenseTypeId = types["PARKING"], amount = 100, paymentMethod = "Cash" })).StatusCode);
    }
}
