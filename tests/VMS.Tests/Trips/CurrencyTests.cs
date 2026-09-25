using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-02: currency master, tenant currency settings, exchange rates (FSD §13A).</summary>
[Collection(ApiCollection.Name)]
public sealed class CurrencyTests(ApiFactory factory)
{
    [Fact]
    public async Task A_new_tenant_is_seeded_with_pkr_as_the_base_currency_and_multi_currency_off()
    {
        var w = await TripsWorld.CreateAsync(factory);

        var settings = await (await w.Admin.GetAsync("/api/tenant/currency-settings")).DataAsync();
        Assert.Equal("PKR", settings.GetProperty("baseCurrencyCode").GetString());
        Assert.False(settings.GetProperty("multiCurrencyEnabled").GetBoolean());

        var currencies = await (await w.Admin.GetAsync("/api/currencies")).DataAsync();
        var pkr = currencies.EnumerateArray().Single(c => c.GetProperty("currencyCode").GetString() == "PKR");
        Assert.True(pkr.GetProperty("isBase").GetBoolean());
        Assert.Equal("Active", pkr.GetProperty("status").GetString());
        Assert.Equal(2, pkr.GetProperty("decimalPlaces").GetInt32());
    }

    [Fact]
    public async Task AC_64_multi_currency_off_means_pkr_only()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var settings = await (await w.Admin.GetAsync("/api/tenant/currency-settings")).DataAsync();
        Assert.False(settings.GetProperty("multiCurrencyEnabled").GetBoolean());
        Assert.Equal("PKR", settings.GetProperty("baseCurrencyCode").GetString());
    }

    [Fact]
    public async Task A_currency_can_be_added_and_a_duplicate_code_is_refused()
    {
        var w = await TripsWorld.CreateAsync(factory);

        var created = await w.Admin.PostAsJsonAsync("/api/currencies", new { currencyCode = "usd", currencyName = "US Dollar", symbol = "$", decimalPlaces = 2, status = "Active" });
        created.EnsureSuccessStatusCode();
        var data = await created.DataAsync();
        Assert.Equal("USD", data.GetProperty("currencyCode").GetString());   // stored upper-cased
        Assert.False(data.GetProperty("isBase").GetBoolean());

        var duplicate = await w.Admin.PostAsJsonAsync("/api/currencies", new { currencyCode = "USD", currencyName = "US Dollar (again)" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        var doc = await duplicate.Content.ReadAsStringAsync();
        Assert.Contains("VAL-TRP-001", doc);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("DOLLAR")]
    [InlineData("")]
    public async Task A_currency_code_must_be_three_letters(string code)
    {
        var w = await TripsWorld.CreateAsync(factory);
        var response = await w.Admin.PostAsJsonAsync("/api/currencies", new { currencyCode = code, currencyName = "Something" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("VAL-TRP-002", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_base_currency_cannot_be_deactivated()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var response = await w.Admin.PutAsJsonAsync("/api/currencies/PKR", new { currencyName = "Pakistani Rupee", status = "Inactive" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("VAL-TRP-004", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Switching_the_base_currency_moves_the_is_base_flag_and_is_rejected_for_an_unknown_or_inactive_code()
    {
        var w = await TripsWorld.CreateAsync(factory);
        (await w.Admin.PostAsJsonAsync("/api/currencies", new { currencyCode = "USD", currencyName = "US Dollar" })).EnsureSuccessStatusCode();
        (await w.Admin.PostAsJsonAsync("/api/currencies", new { currencyCode = "AED", currencyName = "UAE Dirham", status = "Inactive" })).EnsureSuccessStatusCode();

        var unknown = await w.Admin.PutAsJsonAsync("/api/tenant/currency-settings", new { baseCurrencyCode = "EUR", multiCurrencyEnabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("VAL-TRP-003", await unknown.Content.ReadAsStringAsync());

        var inactive = await w.Admin.PutAsJsonAsync("/api/tenant/currency-settings", new { baseCurrencyCode = "AED", multiCurrencyEnabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Contains("VAL-TRP-004", await inactive.Content.ReadAsStringAsync());

        var switched = await w.Admin.PutAsJsonAsync("/api/tenant/currency-settings", new { baseCurrencyCode = "USD", multiCurrencyEnabled = true });
        switched.EnsureSuccessStatusCode();
        var settings = await switched.DataAsync();
        Assert.Equal("USD", settings.GetProperty("baseCurrencyCode").GetString());
        Assert.True(settings.GetProperty("multiCurrencyEnabled").GetBoolean());

        var currencies = await (await w.Admin.GetAsync("/api/currencies")).DataAsync();
        Assert.True(currencies.EnumerateArray().Single(c => c.GetProperty("currencyCode").GetString() == "USD").GetProperty("isBase").GetBoolean());
        Assert.False(currencies.EnumerateArray().Single(c => c.GetProperty("currencyCode").GetString() == "PKR").GetProperty("isBase").GetBoolean());
    }

    [Fact]
    public async Task An_exchange_rate_converts_a_foreign_currency_into_the_current_base_and_rejects_the_base_itself()
    {
        var w = await TripsWorld.CreateAsync(factory);
        (await w.Admin.PostAsJsonAsync("/api/currencies", new { currencyCode = "USD", currencyName = "US Dollar" })).EnsureSuccessStatusCode();

        var created = await w.Admin.PostAsJsonAsync("/api/exchange-rates", new { fromCurrencyCode = "USD", rateDate = "2026-09-25", rate = 278.50m });
        created.EnsureSuccessStatusCode();
        var data = await created.DataAsync();
        Assert.Equal("USD", data.GetProperty("fromCurrencyCode").GetString());
        Assert.Equal("PKR", data.GetProperty("toCurrencyCode").GetString());   // server-assigned: always the current base
        Assert.Equal(278.50m, data.GetProperty("rate").GetDecimal());

        var duplicate = await w.Admin.PostAsJsonAsync("/api/exchange-rates", new { fromCurrencyCode = "USD", rateDate = "2026-09-25", rate = 279.00m });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("VAL-TRP-009", await duplicate.Content.ReadAsStringAsync());

        var unknown = await w.Admin.PostAsJsonAsync("/api/exchange-rates", new { fromCurrencyCode = "EUR", rateDate = "2026-09-25", rate = 300m });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("VAL-TRP-007", await unknown.Content.ReadAsStringAsync());

        var self = await w.Admin.PostAsJsonAsync("/api/exchange-rates", new { fromCurrencyCode = "PKR", rateDate = "2026-09-25", rate = 1m });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Contains("VAL-TRP-008", await self.Content.ReadAsStringAsync());

        var listed = await (await w.Admin.GetAsync("/api/exchange-rates")).DataAsync();
        Assert.Single(listed.EnumerateArray());
    }

    [Fact]
    public async Task Exchange_rate_maintenance_can_be_granted_without_the_full_currency_manage_permission()
    {
        var w = await TripsWorld.CreateAsync(factory);
        (await w.Admin.PostAsJsonAsync("/api/currencies", new { currencyCode = "USD", currencyName = "US Dollar" })).EnsureSuccessStatusCode();

        var financeUser = w.As("Finance", 42, PermissionCodes.TRP_EXCHANGERATE_MANAGE);   // granted rates, but not currency setup
        var rateCall = await financeUser.PostAsJsonAsync("/api/exchange-rates", new { fromCurrencyCode = "USD", rateDate = "2026-09-25", rate = 278m });
        rateCall.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await financeUser.GetAsync("/api/currencies")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await financeUser.GetAsync("/api/tenant/currency-settings")).StatusCode);
    }

    [Fact]
    public async Task Without_any_permission_every_currency_endpoint_is_refused()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var noAccess = w.As("Nobody", 9);
        Assert.Equal(HttpStatusCode.Forbidden, (await noAccess.GetAsync("/api/currencies")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noAccess.GetAsync("/api/tenant/currency-settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noAccess.GetAsync("/api/exchange-rates")).StatusCode);
    }
}
