using System.Diagnostics;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>
/// CC-46, §54's own performance budgets ("eligible-trip search ≤ 3s for 10,000 trips in period," "trip list ≤ 2s
/// at 1M trips") — genuinely seeding those literal volumes into a throwaway LocalDB database per test run is not
/// practical for this suite (each of those runs would itself take minutes, dwarfing the rest of the ~1,000-test
/// suite this file lives in). What follows is a scaled proxy, not a literal proof of the NFR's own number: a few
/// hundred rows, timed against a still-real, still-generous absolute ceiling — enough to catch a genuinely
/// pathological regression (an accidental per-row query in a loop, the exact class of bug the "page first, batch
/// resolve after" idiom this module keeps reusing was built to avoid) without pretending to be a load test. A
/// real volume/load test belongs in a dedicated environment with a purpose-built tool, not xUnit against
/// LocalDB — documented here rather than faked.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PerformanceSmokeTests(ApiFactory factory)
{
    private const int SeededTripCount = 200;

    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin, int CustomerId, long ConfigId)> SeededWorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_GENERATE]);

        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        var byAbbr = cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Perf {Guid.NewGuid():N}", addressLine1 = "Line 1" })).DataAsync();
        var customerId = customer.GetProperty("customerId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();

        var route = await (await admin.PostAsJsonAsync("/api/routes", new
        {
            routeName = $"Perf Route {Guid.NewGuid():N}"[..20],
            stops = new[] { new { cityId = byAbbr["LHR"], stopType = "Origin" }, new { cityId = byAbbr["FSD"], stopType = "Destination" } }
        })).DataAsync();
        var config = await (await admin.PostAsJsonAsync("/api/trip-configurations",
            new { customerId, name = "Perf Config", routeId = route.GetProperty("routeId").GetInt32(), directionType = "OneWay" })).DataAsync();
        var configId = config.GetProperty("tripConfigurationId").GetInt64();
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId = truck, effectiveFrom = "2020-01-01" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/activate", new { rowVersion = config.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-01-01", rateAmount = 10000 })).EnsureSuccessStatusCode();

        var driverId = await vehicles.DriverAsync();
        for (var i = 0; i < SeededTripCount; i++)
            (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId = truck, driverId, tripDate = Day(-i % 60) }))
                .EnsureSuccessStatusCode();

        // The read paths under test (search, eligibility) care about Status/CompletionDate, not the full six-step
        // lifecycle walk to get there — a single bulk UPDATE (the same "direct-SQL stand-in for a scenario the
        // API doesn't need to reach one row at a time" idiom CC-17/21 already established) reaches the same end
        // state without ~1,200 extra sequential status-transition calls just to set up this one test file.
        factory.Execute("UPDATE trp.Trips SET Status = 'Completed', CompletionDate = CAST(SYSUTCDATETIME() AS date) WHERE TenantId = @tenantId AND TripConfigurationId = @configId",
            ("@tenantId", vehicles.Tenant), ("@configId", configId));

        return (vehicles, admin, customerId, configId);
    }

    [Fact]
    public async Task Eligible_trip_search_stays_fast_across_a_few_hundred_trips_in_period()
    {
        var (_, admin, customerId, _) = await SeededWorldAsync(factory);

        var stopwatch = Stopwatch.StartNew();
        var response = await admin.PostAsJsonAsync("/api/invoices/search-eligible-trips", new { customerId, periodFrom = Day(-90), periodTo = Day(30) });
        stopwatch.Stop();

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();
        Assert.Equal(SeededTripCount, result.GetProperty("summary").GetProperty("completedTrips").GetInt32());
        Assert.True(stopwatch.ElapsedMilliseconds < 5000,
            $"Eligible-trip search over {SeededTripCount} trips took {stopwatch.ElapsedMilliseconds} ms — §54's own budget is 3s at 10,000 trips; this scaled-down check still caught an accidental per-row query.");
    }

    [Fact]
    public async Task Trip_list_search_stays_fast_across_a_few_hundred_trips()
    {
        var (_, admin, customerId, _) = await SeededWorldAsync(factory);

        var stopwatch = Stopwatch.StartNew();
        var response = await admin.GetAsync($"/api/trips/search?customerId={customerId}&pageSize=50");
        stopwatch.Stop();

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var page = await response.DataAsync();
        Assert.Equal(SeededTripCount, page.GetProperty("totalCount").GetInt32());
        Assert.True(stopwatch.ElapsedMilliseconds < 5000,
            $"Trip search over {SeededTripCount} trips took {stopwatch.ElapsedMilliseconds} ms — §54's own budget is 2s at 1,000,000 trips; this scaled-down check still caught an accidental per-row query.");
    }
}
