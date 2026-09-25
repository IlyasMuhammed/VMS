using System.Net;
using System.Net.Http.Json;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-11: trip rates — effective dating, one-day split, open-ended auto-close, overlap rejection (§26,
/// AC-12, AC-13, AC-14, AC-16, AC-56).</summary>
[Collection(ApiCollection.Name)]
public sealed class TripRateTests(ApiFactory factory)
{
    private static async Task<long> ActiveConfigAsync(HttpClient admin)
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
        return config.GetProperty("tripConfigurationId").GetInt64();
    }

    [Fact]
    public async Task AC_12_and_AC_13_a_trip_date_resolves_the_rate_whose_range_covers_it_including_a_one_day_rate()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var configId = await ActiveConfigAsync(w.Admin);
        var customerId = (await (await w.Admin.GetAsync($"/api/trip-configurations/{configId}")).DataAsync()).GetProperty("customerId").GetInt32();

        (await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", effectiveTo = "2026-07-10", rateAmount = 25000 })).EnsureSuccessStatusCode();
        (await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-11", effectiveTo = "2026-07-11", rateAmount = 28000 })).EnsureSuccessStatusCode();
        (await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-12", effectiveTo = "2026-07-31", rateAmount = 25000 })).EnsureSuccessStatusCode();

        var onFirst = await (await w.Admin.GetAsync($"/api/trip-rates/resolve?customerId={customerId}&tripConfigurationId={configId}&date=2026-07-05")).DataAsync();
        Assert.Equal(25000, onFirst.GetProperty("rateAmount").GetDecimal());

        var oneDay = await (await w.Admin.GetAsync($"/api/trip-rates/resolve?customerId={customerId}&tripConfigurationId={configId}&date=2026-07-11")).DataAsync();
        Assert.Equal(28000, oneDay.GetProperty("rateAmount").GetDecimal());
    }

    [Fact]
    public async Task AC_14_an_overlapping_range_is_rejected()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var configId = await ActiveConfigAsync(w.Admin);
        (await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", effectiveTo = "2026-07-15", rateAmount = 25000 })).EnsureSuccessStatusCode();

        var overlapping = await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-10", effectiveTo = "2026-07-20", rateAmount = 26000 });
        Assert.Equal(HttpStatusCode.BadRequest, overlapping.StatusCode);
        Assert.Contains("VAL-TRP-011", await overlapping.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AC_16_no_rate_covering_a_date_means_not_found_never_a_fallback()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var configId = await ActiveConfigAsync(w.Admin);
        var customerId = (await (await w.Admin.GetAsync($"/api/trip-configurations/{configId}")).DataAsync()).GetProperty("customerId").GetInt32();
        (await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", effectiveTo = "2026-07-14", rateAmount = 25000 })).EnsureSuccessStatusCode();

        var missing = await (await w.Admin.GetAsync($"/api/trip-rates/resolve?customerId={customerId}&tripConfigurationId={configId}&date=2026-07-15")).DataAsync();
        Assert.False(missing.GetProperty("found").GetBoolean());
        Assert.False(missing.TryGetProperty("rateAmount", out var amount) && amount.ValueKind != System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task AC_56_a_later_rate_auto_closes_the_open_ended_one_and_resolves_from_its_own_start()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var configId = await ActiveConfigAsync(w.Admin);
        var customerId = (await (await w.Admin.GetAsync($"/api/trip-configurations/{configId}")).DataAsync()).GetProperty("customerId").GetInt32();

        var first = await (await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount = 25000 })).DataAsync();
        var second = await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-08-16", rateAmount = 27000 });
        second.EnsureSuccessStatusCode();

        var firstAfter = (await (await w.Admin.GetAsync($"/api/trip-configurations/{configId}/rates?includeInactive=true")).DataAsync())
            .EnumerateArray().First(r => r.GetProperty("tripRateId").GetInt64() == first.GetProperty("tripRateId").GetInt64());
        Assert.Equal("2026-08-15", firstAfter.GetProperty("effectiveTo").GetString());

        var resolved = await (await w.Admin.GetAsync($"/api/trip-rates/resolve?customerId={customerId}&tripConfigurationId={configId}&date=2026-09-30")).DataAsync();
        Assert.Equal(27000, resolved.GetProperty("rateAmount").GetDecimal());
    }

    [Fact]
    public async Task At_most_one_open_ended_rate_is_allowed_a_second_open_ended_one_starting_earlier_is_rejected()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var configId = await ActiveConfigAsync(w.Admin);
        (await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount = 25000 })).EnsureSuccessStatusCode();

        var earlierOpen = await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-06-01", rateAmount = 20000 });
        Assert.Equal(HttpStatusCode.BadRequest, earlierOpen.StatusCode);
    }

    [Fact]
    public async Task Splitting_a_range_creates_three_rows_matching_the_fsds_own_worked_example()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var configId = await ActiveConfigAsync(w.Admin);
        (await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", effectiveTo = "2026-07-31", rateAmount = 25000 })).EnsureSuccessStatusCode();

        var split = await (await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates/split", new { date = "2026-07-11", rateAmount = 28000 })).DataAsync();
        Assert.Equal(3, split.GetArrayLength());

        var rows = (await (await w.Admin.GetAsync($"/api/trip-configurations/{configId}/rates")).DataAsync()).EnumerateArray().OrderBy(r => r.GetProperty("effectiveFrom").GetString()).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal("2026-07-01", rows[0].GetProperty("effectiveFrom").GetString());
        Assert.Equal("2026-07-10", rows[0].GetProperty("effectiveTo").GetString());
        Assert.Equal("2026-07-11", rows[1].GetProperty("effectiveFrom").GetString());
        Assert.Equal("2026-07-11", rows[1].GetProperty("effectiveTo").GetString());
        Assert.Equal(28000, rows[1].GetProperty("rateAmount").GetDecimal());
        Assert.Equal("2026-07-12", rows[2].GetProperty("effectiveFrom").GetString());
        Assert.Equal("2026-07-31", rows[2].GetProperty("effectiveTo").GetString());
        Assert.Equal(25000, rows[2].GetProperty("rateAmount").GetDecimal());
    }

    [Fact]
    public async Task Inactivating_a_rate_frees_its_range_for_a_new_one()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var configId = await ActiveConfigAsync(w.Admin);
        var created = await (await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", effectiveTo = "2026-07-15", rateAmount = 25000 })).DataAsync();

        (await w.Admin.PostAsync($"/api/trip-rates/{created.GetProperty("tripRateId").GetInt64()}/inactivate",
            System.Net.Http.Json.JsonContent.Create(new { rowVersion = created.GetProperty("rowVersion").GetString() }))).EnsureSuccessStatusCode();

        var reused = await w.Admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", effectiveTo = "2026-07-15", rateAmount = 26000 });
        reused.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Concurrent_inserts_for_overlapping_ranges_cannot_both_succeed()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var configId = await ActiveConfigAsync(w.Admin);

        var clientA = w.As("A", 101, TripsWorld.Everything);
        var clientB = w.As("B", 102, TripsWorld.Everything);

        var taskA = clientA.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", effectiveTo = "2026-07-15", rateAmount = 25000 });
        var taskB = clientB.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-05", effectiveTo = "2026-07-20", rateAmount = 26000 });
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Single(results, r => r.IsSuccessStatusCode);
        Assert.Single(results, r => !r.IsSuccessStatusCode);

        var rows = await (await w.Admin.GetAsync($"/api/trip-configurations/{configId}/rates")).DataAsync();
        Assert.Single(rows.EnumerateArray());
    }

    [Fact]
    public async Task View_needs_view_permission_edit_needs_rate_permission()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var configId = await ActiveConfigAsync(w.Admin);
        var viewer = w.As("Viewer", 5, VMS.Shared.Authorization.PermissionCodes.TRP_RATE_VIEW);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/trip-configurations/{configId}/rates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount = 1 })).StatusCode);
    }
}
