using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-22: trip operational P&amp;L (§31, AC-22).</summary>
[Collection(ApiCollection.Name)]
public sealed class TripPnLTests(ApiFactory factory)
{
    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_FUEL_EDIT, PermissionCodes.TRP_EXPENSE_EDIT, PermissionCodes.TRP_EXPENSE_APPROVE, PermissionCodes.TRP_INCOME_EDIT, PermissionCodes.TRP_PNL_VIEW]);
        return (vehicles, admin);
    }

    private static async Task<(System.Text.Json.JsonElement Trip, int CustomerId, long ConfigId, VehicleWorld Vehicles)> FixedTripAsync(
        VehicleWorld vehicles, HttpClient admin, decimal? rateAmount = 80000, string tripDate = "2026-07-10")
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
        var created = await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        return (await created.DataAsync(), customerId, configId, vehicles);
    }

    private static async Task<int> ExpenseTypeIdAsync(HttpClient admin, string code)
    {
        var types = await (await admin.GetAsync("/api/lookups/TRIP_EXPENSE_TYPE")).DataAsync();
        return types.EnumerateArray().First(t => t.GetProperty("code").GetString() == code).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task AC_22_trip_pnl_matches_the_fsds_own_worked_example()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _, _, _) = await FixedTripAsync(vehicles, admin, rateAmount: 80000);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var tollId = await ExpenseTypeIdAsync(admin, "TOLL_TAX");
        var parkingId = await ExpenseTypeIdAsync(admin, "PARKING");
        var driverExpenseId = await ExpenseTypeIdAsync(admin, "DRIVER_EXPENSE");

        (await admin.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 50, rate = 300, paymentMethod = "Cash" })).EnsureSuccessStatusCode();   // 15,000
        (await admin.PostAsJsonAsync($"/api/trips/{tripId}/expenses", new { expenseTypeId = tollId, amount = 3000, paymentMethod = "Cash" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trips/{tripId}/expenses", new { expenseTypeId = parkingId, amount = 1000, paymentMethod = "Cash" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trips/{tripId}/expenses", new { expenseTypeId = driverExpenseId, amount = 2000, paymentMethod = "Cash" })).EnsureSuccessStatusCode();

        var pnl = await (await admin.GetAsync($"/api/trips/{tripId}/pnl")).DataAsync();
        Assert.True(pnl.GetProperty("isPriced").GetBoolean());
        Assert.Equal(80000, pnl.GetProperty("revenue").GetDecimal());
        Assert.Equal(15000, pnl.GetProperty("fuel").GetDecimal());
        Assert.Equal(6000, pnl.GetProperty("approvedExpenses").GetDecimal());
        Assert.Equal(59000, pnl.GetProperty("operationalPnL").GetDecimal());
    }

    [Fact]
    public async Task Rejected_expenses_are_excluded_from_pnl()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _, _, _) = await FixedTripAsync(vehicles, admin, rateAmount: 50000);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var driverId = trip.GetProperty("driverId").GetInt32();
        var repairId = await ExpenseTypeIdAsync(admin, "REPAIR");

        var driverClient = factory.CreateClient().WithToken(TestTokens.ForScoped(vehicles.Tenant, "Own Driver", driverId, null, null, driverId));
        var created = await (await driverClient.PostAsJsonAsync($"/api/trips/{tripId}/expenses", new { expenseTypeId = repairId, amount = 10000, paymentMethod = "PaidByDriver" })).DataAsync();
        await admin.PostAsJsonAsync($"/api/trip-expenses/{created.GetProperty("tripExpenseId").GetInt64()}/approve", new { approved = false, reason = "No receipt" });

        var pnl = await (await admin.GetAsync($"/api/trips/{tripId}/pnl")).DataAsync();
        Assert.Equal(0, pnl.GetProperty("approvedExpenses").GetDecimal());
        Assert.Equal(50000, pnl.GetProperty("operationalPnL").GetDecimal());
    }

    [Fact]
    public async Task Voided_fuel_and_income_do_not_affect_pnl()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, customerId, _, _) = await FixedTripAsync(vehicles, admin, rateAmount: 20000);
        var tripId = trip.GetProperty("tripId").GetInt64();

        var fuel = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 10, rate = 300, paymentMethod = "Cash" })).DataAsync();
        await admin.PostAsJsonAsync($"/api/trip-fuel/{fuel.GetProperty("tripFuelId").GetInt64()}/void", new { reason = "Wrong trip" });

        var incomeTypes = await (await admin.GetAsync("/api/lookups/TRIP_INCOME_TYPE")).DataAsync();
        var detentionId = incomeTypes.EnumerateArray().First(t => t.GetProperty("code").GetString() == "DETENTION").GetProperty("id").GetInt32();
        var income = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/income", new { incomeTypeId = detentionId, amount = 5000 })).DataAsync();
        await admin.PostAsJsonAsync($"/api/trip-income/{income.GetProperty("tripIncomeId").GetInt64()}/void", new { reason = "Duplicate" });

        var pnl = await (await admin.GetAsync($"/api/trips/{tripId}/pnl")).DataAsync();
        Assert.Equal(0, pnl.GetProperty("fuel").GetDecimal());
        Assert.Equal(0, pnl.GetProperty("approvedIncome").GetDecimal());
        Assert.Equal(20000, pnl.GetProperty("operationalPnL").GetDecimal());
    }

    [Fact]
    public async Task An_unpriced_trip_shows_no_revenue_and_no_pnl()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _, _, _) = await FixedTripAsync(vehicles, admin, rateAmount: null);   // no rate at all -> RateMissing
        var tripId = trip.GetProperty("tripId").GetInt64();

        var pnl = await (await admin.GetAsync($"/api/trips/{tripId}/pnl")).DataAsync();
        Assert.False(pnl.GetProperty("isPriced").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, pnl.GetProperty("revenue").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, pnl.GetProperty("operationalPnL").ValueKind);
    }

    [Fact]
    public async Task The_summary_excludes_unpriced_trips_from_totals_but_counts_them()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (priced, customerId, _, _) = await FixedTripAsync(vehicles, admin, rateAmount: 10000, tripDate: "2026-07-01");
        var (unpriced, _, _, _) = await FixedTripAsync(vehicles, admin, rateAmount: null, tripDate: "2026-07-02");

        var summary = await (await admin.GetAsync($"/api/trips/pnl-summary?customerId={customerId}")).DataAsync();
        // Two different customers were created (FixedTripAsync makes a fresh one each call), so filtering by the
        // first trip's own customer should surface only that one priced trip.
        Assert.Equal(1, summary.GetProperty("pricedTripCount").GetInt32());
        Assert.Equal(10000, summary.GetProperty("totalPnL").GetDecimal());

        var everything = await (await admin.GetAsync("/api/trips/pnl-summary")).DataAsync();
        Assert.True(everything.GetProperty("unpricedTripCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Viewing_pnl_needs_the_pnl_view_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _, _, _) = await FixedTripAsync(vehicles, admin);
        var noPermission = vehicles.As("NoPnL", 55, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.GetAsync($"/api/trips/{trip.GetProperty("tripId").GetInt64()}/pnl")).StatusCode);
    }
}
