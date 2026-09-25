using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-09: routes and route stops (§17).</summary>
[Collection(ApiCollection.Name)]
public sealed class RouteTests(ApiFactory factory)
{
    private static async Task<Dictionary<string, int>> CityIdsByAbbrAsync(HttpClient admin)
    {
        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        return cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());
    }

    private static object Stop(int cityId, string type) => new { cityId, stopType = type };

    [Fact]
    public async Task A_route_auto_suggests_its_code_from_the_origin_and_destination_abbreviations()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var cities = await CityIdsByAbbrAsync(w.Admin);

        var created = await w.Admin.PostAsJsonAsync("/api/routes", new
        {
            routeName = "Lahore - Faisalabad via Sheikhupura",
            stops = new[] { Stop(cities["LHR"], "Origin"), Stop(cities["SKP"], "Via"), Stop(cities["FSD"], "Destination") }
        });
        created.EnsureSuccessStatusCode();
        var data = await created.DataAsync();
        Assert.Equal("RT-LHR-FSD", data.GetProperty("routeCode").GetString());
        Assert.Equal(cities["LHR"], data.GetProperty("originCityId").GetInt32());
        Assert.Equal(cities["FSD"], data.GetProperty("destinationCityId").GetInt32());
        Assert.Equal(3, data.GetProperty("stops").GetArrayLength());
    }

    [Fact]
    public async Task A_repeated_suggestion_gets_a_numeric_suffix()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var cities = await CityIdsByAbbrAsync(w.Admin);
        var body = new { routeName = "First", stops = new[] { Stop(cities["LHR"], "Origin"), Stop(cities["MUL"], "Destination") } };
        (await w.Admin.PostAsJsonAsync("/api/routes", body)).EnsureSuccessStatusCode();

        var second = await (await w.Admin.PostAsJsonAsync("/api/routes",
            new { routeName = "Second, different stops in between", stops = new[] { Stop(cities["LHR"], "Origin"), Stop(cities["SKP"], "Via"), Stop(cities["MUL"], "Destination") } })).DataAsync();
        Assert.Equal("RT-LHR-MUL-2", second.GetProperty("routeCode").GetString());
    }

    [Fact]
    public async Task At_least_two_stops_are_required_and_the_first_last_stop_types_are_enforced()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var cities = await CityIdsByAbbrAsync(w.Admin);

        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsJsonAsync("/api/routes",
            new { routeName = "X", stops = new[] { Stop(cities["LHR"], "Origin") } })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsJsonAsync("/api/routes",
            new { routeName = "X", stops = new[] { Stop(cities["LHR"], "Via"), Stop(cities["MUL"], "Destination") } })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsJsonAsync("/api/routes",
            new { routeName = "X", stops = new[] { Stop(cities["LHR"], "Origin"), Stop(cities["MUL"], "Via") } })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsJsonAsync("/api/routes",
            new { routeName = "X", stops = new[] { Stop(cities["LHR"], "Origin"), Stop(cities["SKP"], "Origin"), Stop(cities["MUL"], "Destination") } })).StatusCode);
    }

    [Fact]
    public async Task An_inactive_city_cannot_be_used_on_a_new_route()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var cities = await CityIdsByAbbrAsync(w.Admin);
        var dgk = cities["DGK"];
        // The cities list only returns id by abbreviation; find the row to deactivate it via update.
        var all = await (await w.Admin.GetAsync("/api/cities")).DataAsync();
        var dgkRow = all.EnumerateArray().First(c => c.GetProperty("cityId").GetInt32() == dgk);
        (await w.Admin.PutAsJsonAsync($"/api/cities/{dgk}",
            new { cityName = dgkRow.GetProperty("cityName").GetString(), abbreviation = "DGK", status = "Inactive" })).EnsureSuccessStatusCode();

        var response = await w.Admin.PostAsJsonAsync("/api/routes", new { routeName = "X", stops = new[] { Stop(cities["LHR"], "Origin"), Stop(dgk, "Destination") } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Origin_and_destination_must_differ_unless_the_route_is_a_round_trip()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var cities = await CityIdsByAbbrAsync(w.Admin);
        var oneWay = await w.Admin.PostAsJsonAsync("/api/routes", new { routeName = "X", stops = new[] { Stop(cities["LHR"], "Origin"), Stop(cities["MUL"], "Via"), Stop(cities["LHR"], "Destination") } });
        Assert.Equal(HttpStatusCode.BadRequest, oneWay.StatusCode);

        var roundTrip = await w.Admin.PostAsJsonAsync("/api/routes",
            new { routeName = "X", isRoundTrip = true, stops = new[] { Stop(cities["LHR"], "Origin"), Stop(cities["MUL"], "Via"), Stop(cities["LHR"], "Destination") } });
        roundTrip.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Updating_the_stop_list_re_derives_origin_and_destination()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var cities = await CityIdsByAbbrAsync(w.Admin);
        var created = await (await w.Admin.PostAsJsonAsync("/api/routes",
            new { routeName = "X", stops = new[] { Stop(cities["LHR"], "Origin"), Stop(cities["FSD"], "Destination") } })).DataAsync();
        var routeId = created.GetProperty("routeId").GetInt32();

        var updated = await (await w.Admin.PutAsJsonAsync($"/api/routes/{routeId}/stops",
            new { stops = new[] { Stop(cities["KHI"], "Origin"), Stop(cities["MUL"], "Via"), Stop(cities["ISL"], "Destination") } })).DataAsync();
        Assert.Equal(cities["KHI"], updated.GetProperty("originCityId").GetInt32());
        Assert.Equal(cities["ISL"], updated.GetProperty("destinationCityId").GetInt32());
        Assert.Equal(3, updated.GetProperty("stops").GetArrayLength());
    }

    [Fact]
    public async Task A_citys_abbreviation_is_immutable_once_a_route_uses_it()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var cities = await CityIdsByAbbrAsync(w.Admin);
        (await w.Admin.PostAsJsonAsync("/api/routes", new { routeName = "X", stops = new[] { Stop(cities["LHR"], "Origin"), Stop(cities["MUL"], "Destination") } })).EnsureSuccessStatusCode();

        var response = await w.Admin.PutAsJsonAsync($"/api/cities/{cities["LHR"]}", new { cityName = "Lahore", abbreviation = "LHE" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // A city never used by any route is still fully editable.
        var response2 = await w.Admin.PutAsJsonAsync($"/api/cities/{cities["ISL"]}", new { cityName = "Islamabad", abbreviation = "ISB" });
        response2.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Route_endpoints_all_need_the_route_permission()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var noAccess = w.As("Nobody", 9);
        Assert.Equal(HttpStatusCode.Forbidden, (await noAccess.GetAsync("/api/routes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noAccess.PostAsJsonAsync("/api/routes", new { routeName = "X", stops = Array.Empty<object>() })).StatusCode);
    }
}
