using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-40: opening balances (§40A L16, LR-11) — buildable half only (schema, posting, importer); loading
/// real go-live data stays blocked on Q4.</summary>
[Collection(ApiCollection.Name)]
public sealed class OpeningBalanceTests(ApiFactory factory)
{
    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_LEDGER_VIEW, PermissionCodes.TRP_LEDGER_OPENINGBALANCE]);
        return (vehicles, admin);
    }

    private static async Task<(int CustomerId, string CustomerCode)> ReadyCustomerAsync(HttpClient admin)
    {
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "Line 1" })).DataAsync();
        var customerId = customer.GetProperty("customerId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();
        return (customerId, customer.GetProperty("customerCode").GetString()!);
    }

    [Fact]
    public async Task A_debit_opening_balance_posts_and_creates_the_pseudo_invoice()
    {
        var (_, admin) = await WorldAsync(factory);
        var (customerId, customerCode) = await ReadyCustomerAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/customers/{customerId}/opening-balance", new { amount = 50000, asOfDate = "2026-01-01", reason = "Go-live migration" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();
        Assert.Equal(50000, result.GetProperty("amount").GetDecimal());
        Assert.Equal($"OB-{customerCode}", result.GetProperty("pseudoInvoiceNumber").GetString());

        var balance = await (await admin.GetAsync($"/api/customers/{customerId}/balance")).DataAsync();
        var row = Assert.Single(balance.EnumerateArray());
        Assert.Equal(50000, row.GetProperty("balanceAmount").GetDecimal());
    }

    [Fact]
    public async Task A_credit_opening_balance_posts_as_a_negative_balance()
    {
        var (_, admin) = await WorldAsync(factory);
        var (customerId, _) = await ReadyCustomerAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/customers/{customerId}/opening-balance", new { amount = -15000, asOfDate = "2026-01-01" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var balance = await (await admin.GetAsync($"/api/customers/{customerId}/balance")).DataAsync();
        Assert.Equal(-15000, balance.EnumerateArray().First().GetProperty("balanceAmount").GetDecimal());
    }

    [Fact]
    public async Task Reposting_the_same_customer_and_currency_is_rejected()
    {
        var (_, admin) = await WorldAsync(factory);
        var (customerId, _) = await ReadyCustomerAsync(admin);
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/opening-balance", new { amount = 20000, asOfDate = "2026-01-01" })).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync($"/api/customers/{customerId}/opening-balance", new { amount = 5000, asOfDate = "2026-01-02" });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("OPENING_BALANCE_ALREADY_POSTED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_zero_amount_is_rejected()
    {
        var (_, admin) = await WorldAsync(factory);
        var (customerId, _) = await ReadyCustomerAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/customers/{customerId}/opening-balance", new { amount = 0, asOfDate = "2026-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Import_processes_each_row_independently()
    {
        var (_, admin) = await WorldAsync(factory);
        var (_, code1) = await ReadyCustomerAsync(admin);

        var response = await admin.PostAsJsonAsync("/api/opening-balances/import", new
        {
            rows = new object[]
            {
                new { customerCode = code1, amount = 10000, asOfDate = "2026-01-01" },
                new { customerCode = "NO-SUCH-CODE", amount = 5000, asOfDate = "2026-01-01" }
            }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();
        Assert.Equal(2, result.GetProperty("totalRows").GetInt32());
        Assert.Equal(1, result.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        var rows = result.GetProperty("results").EnumerateArray().ToList();
        Assert.Contains(rows, r => r.GetProperty("customerCode").GetString() == code1 && r.GetProperty("success").GetBoolean());
        Assert.Contains(rows, r => r.GetProperty("customerCode").GetString() == "NO-SUCH-CODE" && !r.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Opening_balance_posting_needs_its_own_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, _) = await ReadyCustomerAsync(admin);

        var noPermission = vehicles.As("No Permission", 2,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_LEDGER_VIEW]);

        var response = await noPermission.PostAsJsonAsync($"/api/customers/{customerId}/opening-balance", new { amount = 1000, asOfDate = "2026-01-01" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
