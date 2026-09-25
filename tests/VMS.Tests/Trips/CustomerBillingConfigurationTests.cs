using System.Net;
using System.Net.Http.Json;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-05: customer billing configuration (§13) — effective-dated, one open row per customer.</summary>
[Collection(ApiCollection.Name)]
public sealed class CustomerBillingConfigurationTests(ApiFactory factory)
{
    private static async Task<int> NewCustomerAsync(HttpClient admin) =>
        (await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "12 Mall Road" })).DataAsync())
        .GetProperty("customerId").GetInt32();

    [Fact]
    public async Task The_first_get_creates_the_default_configuration()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);

        var config = await (await w.Admin.GetAsync($"/api/customers/{customerId}/billing-configuration")).DataAsync();
        Assert.Equal(30, config.GetProperty("paymentTermsDays").GetInt32());
        Assert.Equal("PKR", config.GetProperty("currencyCode").GetString());
        Assert.Equal("INV", config.GetProperty("invoiceNumberPrefix").GetString());
        Assert.False(config.GetProperty("podRequired").GetBoolean());
        Assert.True(config.GetProperty("evidenceRequired").GetBoolean());
        Assert.Equal(50, config.GetProperty("evidencePageSize").GetInt32());
        Assert.Equal("Warn", config.GetProperty("duplicateReferenceBehaviour").GetString());
        Assert.Null(config.GetProperty("effectiveTo").GetString());
    }

    [Fact]
    public async Task Saving_a_new_version_closes_the_previous_one()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var first = await (await w.Admin.GetAsync($"/api/customers/{customerId}/billing-configuration")).DataAsync();
        var firstFrom = first.GetProperty("effectiveFrom").GetString();

        var laterDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10).ToString("yyyy-MM-dd");
        var saved = await w.Admin.PutAsJsonAsync($"/api/customers/{customerId}/billing-configuration",
            new { paymentTermsDays = 45, invoiceNumberPrefix = "ACM", evidencePageSize = 100, effectiveFrom = laterDate });
        saved.EnsureSuccessStatusCode();
        var next = await saved.DataAsync();
        Assert.Equal(45, next.GetProperty("paymentTermsDays").GetInt32());
        Assert.Equal("ACM", next.GetProperty("invoiceNumberPrefix").GetString());
        Assert.Null(next.GetProperty("effectiveTo").GetString());

        var current = await (await w.Admin.GetAsync($"/api/customers/{customerId}/billing-configuration")).DataAsync();
        Assert.Equal(45, current.GetProperty("paymentTermsDays").GetInt32());

        var asOfOld = await (await w.Admin.GetAsync($"/api/customers/{customerId}/billing-configuration?asOf={firstFrom}")).DataAsync();
        Assert.Equal(30, asOfOld.GetProperty("paymentTermsDays").GetInt32());
    }

    [Fact]
    public async Task A_new_version_cannot_start_on_or_before_the_current_ones_start()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var current = await (await w.Admin.GetAsync($"/api/customers/{customerId}/billing-configuration")).DataAsync();
        var effectiveFrom = current.GetProperty("effectiveFrom").GetString();

        var response = await w.Admin.PutAsJsonAsync($"/api/customers/{customerId}/billing-configuration", new { effectiveFrom });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_currency_is_forced_to_base_while_multi_currency_is_off()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        (await w.Admin.PostAsJsonAsync("/api/currencies", new { currencyCode = "USD", currencyName = "US Dollar" })).EnsureSuccessStatusCode();

        var saved = await (await w.Admin.PutAsJsonAsync($"/api/customers/{customerId}/billing-configuration", new { currencyCode = "USD" })).DataAsync();
        Assert.Equal("PKR", saved.GetProperty("currencyCode").GetString());   // multi-currency is off, so USD is ignored

        await w.Admin.PutAsJsonAsync("/api/tenant/currency-settings", new { multiCurrencyEnabled = true });
        var laterDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5).ToString("yyyy-MM-dd");
        var withMulti = await (await w.Admin.PutAsJsonAsync($"/api/customers/{customerId}/billing-configuration", new { currencyCode = "USD", effectiveFrom = laterDate })).DataAsync();
        Assert.Equal("USD", withMulti.GetProperty("currencyCode").GetString());
    }

    [Fact]
    public async Task Evidence_page_size_is_bounded_and_the_duplicate_reference_behaviour_must_be_a_known_value()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PutAsJsonAsync($"/api/customers/{customerId}/billing-configuration", new { evidencePageSize = 500 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PutAsJsonAsync($"/api/customers/{customerId}/billing-configuration", new { duplicateReferenceBehaviour = "Ignore" })).StatusCode);
    }

    [Fact]
    public async Task The_default_billing_address_and_statement_contact_must_belong_to_the_same_customer()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var otherCustomerId = await NewCustomerAsync(w.Admin);
        var cities = await (await w.Admin.GetAsync("/api/lookups/CITY")).DataAsync();
        var cityId = cities.EnumerateArray().First().GetProperty("id").GetInt32();

        var otherAddress = await (await w.Admin.PostAsJsonAsync($"/api/customers/{otherCustomerId}/billing-addresses",
            new { addressName = "HO", addressLine1 = "Line 1", cityId })).DataAsync();
        var addressId = otherAddress.GetProperty("customerBillingAddressId").GetInt64();

        var response = await w.Admin.PutAsJsonAsync($"/api/customers/{customerId}/billing-configuration", new { defaultBillingAddressId = addressId });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
