using System.Net;
using System.Net.Http.Json;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-08: the city master (§16, AC-09).</summary>
[Collection(ApiCollection.Name)]
public sealed class CityTests(ApiFactory factory)
{
    [Fact]
    public async Task A_new_tenant_is_seeded_with_the_fsds_own_seven_cities()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var cities = await (await w.Admin.GetAsync("/api/cities")).DataAsync();
        var byAbbr = cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityName").GetString());

        Assert.Equal("Lahore", byAbbr["LHR"]);
        Assert.Equal("Islamabad", byAbbr["ISL"]);
        Assert.Equal("Faisalabad", byAbbr["FSD"]);
        Assert.Equal("Sheikhupura", byAbbr["SKP"]);
        Assert.Equal("Multan", byAbbr["MUL"]);
        Assert.Equal("Karachi", byAbbr["KHI"]);
        Assert.Equal("Dera Ghazi Khan", byAbbr["DGK"]);
        Assert.Equal(7, cities.GetArrayLength());
    }

    [Fact]
    public async Task AC_09_a_duplicate_abbreviation_is_rejected()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var response = await w.Admin.PostAsJsonAsync("/api/cities", new { cityName = "Lahore Cantt", abbreviation = "LHR" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("L")]
    [InlineData("LAHORE1")]
    [InlineData("lhr")]
    public async Task An_abbreviation_must_be_two_to_five_letters(string abbreviation)
    {
        var w = await TripsWorld.CreateAsync(factory);
        var response = await w.Admin.PostAsJsonAsync("/api/cities", new { cityName = "Somewhere", abbreviation });
        // "lhr" lower-case is uppercased first, then collides with the seeded LHR — still a 400 either way,
        // but for a different reason; the two purely-format cases below are the real target of this test.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_new_city_defaults_to_pakistan_and_the_display_property_matches_the_fsds_own_format()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var created = await (await w.Admin.PostAsJsonAsync("/api/cities", new { cityName = "Gujranwala", abbreviation = "GRW" })).DataAsync();
        Assert.True(created.GetProperty("countryId").GetInt32() > 0);
        Assert.Equal("Gujranwala (GRW)", created.GetProperty("display").GetString());
    }

    [Fact]
    public async Task The_same_city_name_can_exist_in_different_provinces_but_not_twice_in_the_same_one()
    {
        var w = await TripsWorld.CreateAsync(factory);
        // Abbreviations must be letters only (§16) — SPA/SPB/SPC, not SP1/SP2/SP3.
        var first = await w.Admin.PostAsJsonAsync("/api/cities", new { cityName = "Springfield", abbreviation = "SPA", provinceState = "Punjab" });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var differentProvince = await w.Admin.PostAsJsonAsync("/api/cities", new { cityName = "Springfield", abbreviation = "SPB", provinceState = "Sindh" });
        Assert.True(differentProvince.IsSuccessStatusCode, await differentProvince.Content.ReadAsStringAsync());

        var sameProvince = await w.Admin.PostAsJsonAsync("/api/cities", new { cityName = "Springfield", abbreviation = "SPC", provinceState = "Punjab" });
        Assert.Equal(HttpStatusCode.BadRequest, sameProvince.StatusCode);
    }

    [Fact]
    public async Task Viewing_needs_only_a_sign_in_editing_needs_the_city_permission()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var noPermissions = w.As("Anyone", 9);   // no Trips permissions at all
        Assert.Equal(HttpStatusCode.OK, (await noPermissions.GetAsync("/api/cities")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noPermissions.PostAsJsonAsync("/api/cities", new { cityName = "X", abbreviation = "XX" })).StatusCode);
    }
}
