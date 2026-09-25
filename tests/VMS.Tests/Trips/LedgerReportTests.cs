using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-41: Customer Ledger & receivables reports LED-01..15 (§42.2, §42.9, AC-51/AC-52). Background
/// export above 50,000 rows is CC-42's own acceptance item — not exercised here.</summary>
[Collection(ApiCollection.Name)]
public sealed class LedgerReportTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT,
             PermissionCodes.TRP_PAYMENT_CREATE, PermissionCodes.TRP_PAYMENT_ADVANCE, PermissionCodes.TRP_PAYMENT_CARRYFORWARD, PermissionCodes.TRP_PAYMENT_REFUND,
             PermissionCodes.TRP_PAYMENT_WRITEOFF, PermissionCodes.TRP_BANKACCOUNT_MANAGE, PermissionCodes.TRP_LEDGER_VIEW, PermissionCodes.TRP_REPORT_VIEW]);
        return (vehicles, admin);
    }

    private static async Task<int> ReadyCustomerAsync(HttpClient admin)
    {
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "Line 1" })).DataAsync();
        var customerId = customer.GetProperty("customerId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();

        var cities = await (await admin.GetAsync("/api/lookups/CITY")).DataAsync();
        var cityId = cities.EnumerateArray().First().GetProperty("id").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/billing-addresses",
            new { addressName = "Head Office", addressLine1 = "Mall Road", cityId, isDefault = true })).EnsureSuccessStatusCode();

        var template = await (await admin.PostAsJsonAsync($"/api/customers/{customerId}/invoice-templates", new { templateName = $"Standard {Guid.NewGuid():N}"[..24] })).DataAsync();
        (await admin.PostAsJsonAsync($"/api/customer-invoice-templates/{template.GetProperty("customerInvoiceTemplateId").GetInt64()}/activate", new { })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/customers/{customerId}/billing-configuration", new { evidenceRequired = false })).EnsureSuccessStatusCode();
        return customerId;
    }

    private static async Task<long> ReadyConfigAsync(HttpClient admin, int customerId, int vehicleId, decimal rateAmount, string routeName)
    {
        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        var byAbbr = cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());
        var route = await (await admin.PostAsJsonAsync("/api/routes", new
        {
            routeName, stops = new[] { new { cityId = byAbbr["LHR"], stopType = "Origin" }, new { cityId = byAbbr["FSD"], stopType = "Destination" } }
        })).DataAsync();
        var config = await (await admin.PostAsJsonAsync("/api/trip-configurations",
            new { customerId, name = routeName, routeId = route.GetProperty("routeId").GetInt32(), directionType = "OneWay" })).DataAsync();
        var configId = config.GetProperty("tripConfigurationId").GetInt64();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId, effectiveFrom = "2020-01-01" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/activate", new { rowVersion = config.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount })).EnsureSuccessStatusCode();
        return configId;
    }

    private static async Task<long> CompleteTripAsync(HttpClient admin, int customerId, long configId, int vehicleId, int driverId, string tripDate)
    {
        var trip = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate })).DataAsync();
        var tripId = trip.GetProperty("tripId").GetInt64();
        var planned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Planned", new { rowVersion = trip.GetProperty("rowVersion").GetString() })).DataAsync();
        var assigned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Assigned", new { rowVersion = planned.GetProperty("rowVersion").GetString() })).DataAsync();
        var started = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Started", new { rowVersion = assigned.GetProperty("rowVersion").GetString(), startOdometer = 100 })).DataAsync();
        var inTransit = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/InTransit", new { rowVersion = started.GetProperty("rowVersion").GetString() })).DataAsync();
        var atDelivery = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/AtDelivery", new { rowVersion = inTransit.GetProperty("rowVersion").GetString() })).DataAsync();
        var delivered = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Delivered", new { rowVersion = atDelivery.GetProperty("rowVersion").GetString(), endOdometer = 900 })).DataAsync();
        await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Completed", new { rowVersion = delivered.GetProperty("rowVersion").GetString() });
        return tripId;
    }

    private static async Task<long> ReadyBankAccountAsync(HttpClient admin)
    {
        var account = await (await admin.PostAsJsonAsync("/api/bank-accounts", new
        {
            accountTitle = "Acme Trading Co.", bankName = "HBL", branchName = "Gulberg", accountNumberLast4 = "1234"
        })).DataAsync();
        return account.GetProperty("bankCashAccountId").GetInt64();
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, long invoiceId, string rowVersion, string? submittedOn = null) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/invoices/{invoiceId}/submit")
        {
            Content = JsonContent.Create(new { rowVersion, submittedOn }),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } }
        });

    private static async Task<(long InvoiceId, string InvoiceNumber)> ReadySubmittedInvoiceAsync(
        HttpClient admin, VehicleWorld vehicles, int customerId, decimal tripAmount, string routeName, int periodFromOffset, int periodToOffset, string? submittedOn = null)
    {
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, tripAmount, routeName);
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(periodFromOffset), periodTo = Day(periodToOffset), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var response = await SubmitAsync(admin, invoiceId, invoice.GetProperty("rowVersion").GetString()!, submittedOn);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var submitted = await response.DataAsync();
        return (invoiceId, submitted.GetProperty("invoiceNumber").GetString()!);
    }

    [Fact]
    public async Task AC_51_aging_places_a_31_to_60_day_overdue_balance_in_the_right_bucket()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var (invoiceId, _) = await ReadySubmittedInvoiceAsync(admin, vehicles, customerId, 100000, "Aging Route", -30, 30);
        factory.Execute("UPDATE trp.Invoices SET DueDate = @due WHERE InvoiceId = @id", ("@due", new DateOnly(2026, 8, 1)), ("@id", invoiceId));

        var response = await admin.GetAsync($"/api/reports/LED-05?customerId={customerId}&asOf=2026-09-15");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();
        var row = Assert.Single(result.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal("31-60", row.GetProperty("bucket").GetString());
        Assert.Equal(100000, row.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task AC_52_export_returns_a_csv_with_the_header_metadata_and_the_same_rows()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        await ReadySubmittedInvoiceAsync(admin, vehicles, customerId, 40000, "Export Route", -30, 30);

        var response = await admin.PostAsync($"/api/reports/LED-03/export?customerId={customerId}", null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("# Report: LED-03", content);
        Assert.Contains("# Run by:", content);
        Assert.Contains("customerCode", content);
        Assert.Contains("40000.00", content);
    }

    [Fact]
    public async Task LED_01_customer_ledger_statement_matches_the_read_service()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        await ReadySubmittedInvoiceAsync(admin, vehicles, customerId, 60000, "LED01 Route", -30, 30);

        var response = await admin.GetAsync($"/api/reports/LED-01?customerId={customerId}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();
        var row = Assert.Single(result.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal("INVOICE", row.GetProperty("entryType").GetString());
        Assert.Equal(60000, row.GetProperty("runningBalance").GetDecimal());
    }

    [Fact]
    public async Task LED_09_customer_credit_balances_finds_an_overpaid_invoice()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var (invoiceId, invoiceNumber) = await ReadySubmittedInvoiceAsync(admin, vehicles, customerId, 50000, "Credit Route", -30, 30);
        var bankAccountId = await ReadyBankAccountAsync(admin);
        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = Day(), amount = 70000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-LED09", confirmOverpayment = true
        })).EnsureSuccessStatusCode();

        var response = await admin.GetAsync($"/api/reports/LED-09?customerId={customerId}");
        var result = await response.DataAsync();
        var row = Assert.Single(result.GetProperty("data").GetProperty("items").EnumerateArray(), r => r.GetProperty("invoiceNumber").GetString() == invoiceNumber);
        Assert.Equal(20000, row.GetProperty("creditAmount").GetDecimal());
        Assert.Equal("Overpayment", row.GetProperty("origin").GetString());
    }

    [Fact]
    public async Task LED_12_reconciliation_exceptions_surfaces_the_latest_run()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var (invoiceId, invoiceNumber) = await ReadySubmittedInvoiceAsync(admin, vehicles, customerId, 30000, "Recon Report Route", -30, 30);
        factory.Execute("UPDATE trp.Invoices SET BalanceAmount = 29000 WHERE InvoiceId = @id", ("@id", invoiceId));

        factory.CreateClient();
        await VMS.Shared.Common.BackgroundTenantScope.RunAsAsync(vehicles.Tenant, async () =>
        {
            using var scope = factory.Services.CreateScope();
            using var acting = scope.ServiceProvider.GetRequiredService<VMS.Shared.Auditing.IAuditContext>().ActAsSystem("Test");
            await scope.ServiceProvider.GetRequiredService<VMS.Modules.Trips.Services.ILedgerReconciliationJob>().RunAsync();
        });

        var response = await admin.GetAsync("/api/reports/LED-12");
        var result = await response.DataAsync();
        var row = Assert.Single(result.GetProperty("data").GetProperty("items").EnumerateArray(), r => r.GetProperty("invoiceNumber").GetString() == invoiceNumber);
        Assert.Equal(1000, row.GetProperty("difference").GetDecimal());
    }

    [Fact]
    public async Task LED_15_write_offs_and_discounts_lists_a_settlement()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var (invoiceId, invoiceNumber) = await ReadySubmittedInvoiceAsync(admin, vehicles, customerId, 20000, "Writeoff Route", -30, 30);
        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/settlements", new { settlementType = "WriteOff", amount = 2000, reason = "Small balance write-off" })).EnsureSuccessStatusCode();

        var response = await admin.GetAsync($"/api/reports/LED-15?customerId={customerId}");
        var result = await response.DataAsync();
        var row = Assert.Single(result.GetProperty("data").GetProperty("items").EnumerateArray(), r => r.GetProperty("invoiceNumber").GetString() == invoiceNumber);
        Assert.Equal("WriteOff", row.GetProperty("type").GetString());
        Assert.Equal(2000, row.GetProperty("amount").GetDecimal());
        Assert.False(row.GetProperty("reversed").GetBoolean());
    }

    [Fact]
    public async Task Sorting_and_paging_apply_generically()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        await ReadySubmittedInvoiceAsync(admin, vehicles, customerId, 10000, "Sort Route 1", -60, -31);
        await ReadySubmittedInvoiceAsync(admin, vehicles, customerId, 90000, "Sort Route 2", -30, -1);

        var response = await admin.GetAsync($"/api/reports/LED-02?customerId={customerId}&sortBy=invoiceBalance&sortDesc=true&pageSize=1&page=1");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();
        var data = result.GetProperty("data");
        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
        var items = data.GetProperty("items").EnumerateArray().ToList();
        var single = Assert.Single(items);
        Assert.Equal(90000, single.GetProperty("invoiceBalance").GetDecimal());
    }

    [Fact]
    public async Task An_unknown_report_code_is_not_found()
    {
        var (_, admin) = await WorldAsync(factory);
        var response = await admin.GetAsync("/api/reports/LED-99");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reports_need_their_own_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var noPermission = vehicles.As("No Permission", 2,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_LEDGER_VIEW]);

        var response = await noPermission.GetAsync("/api/reports/LED-01");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
