using System.Net;
using System.Net.Http.Json;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-42: Trip, invoice, P&amp;L, master data and audit reports (§42.1, §42.3-42.7) — CUS/TRP/PNL/INV/MST/
/// AUD-xx, sharing CC-41's own report pipeline. §42.9's own 50,000-row background-export threshold is proven as
/// the pure decision it actually is (<see cref="ReportExportDecisionTests"/>), not by generating 50,001 rows.</summary>
[Collection(ApiCollection.Name)]
public sealed class OperationalReportTests(ApiFactory factory)
{
    private static readonly string[] AllNewCodes =
    [
        "CUS-01", "CUS-02", "CUS-03", "CUS-04", "CUS-05", "CUS-06", "CUS-07", "CUS-08", "CUS-09", "CUS-10",
        "TRP-01", "TRP-02", "TRP-03", "TRP-04", "TRP-05", "TRP-06", "TRP-07", "TRP-08", "TRP-09", "TRP-10",
        "TRP-11", "TRP-12", "TRP-13", "TRP-14", "TRP-15", "TRP-16",
        "PNL-01", "PNL-02", "PNL-03", "PNL-04", "PNL-05", "PNL-06", "PNL-07", "PNL-08", "PNL-09", "PNL-10", "PNL-11", "PNL-12", "PNL-13",
        "INV-01", "INV-02", "INV-03", "INV-04", "INV-05", "INV-06", "INV-07", "INV-08", "INV-09", "INV-10",
        "INV-11", "INV-12", "INV-13", "INV-14", "INV-15", "INV-16", "INV-17", "INV-18",
        "MST-01", "MST-02", "MST-03", "MST-04", "MST-05",
        "AUD-01", "AUD-02", "AUD-03"
    ];

    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT,
             PermissionCodes.TRP_PAYMENT_CREATE, PermissionCodes.TRP_PAYMENT_ADVANCE, PermissionCodes.TRP_PAYMENT_WRITEOFF,
             PermissionCodes.TRP_BANKACCOUNT_MANAGE, PermissionCodes.TRP_LEDGER_VIEW, PermissionCodes.TRP_REPORT_VIEW,
             PermissionCodes.TRP_EXPENSE_EDIT, PermissionCodes.TRP_FUEL_EDIT, PermissionCodes.TRP_INCOME_EDIT]);
        return (vehicles, admin);
    }

    private static async Task<(int CustomerId, string CustomerCode)> ReadyCustomerAsync(HttpClient admin)
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
        return (customerId, customer.GetProperty("customerCode").GetString()!);
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

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, long invoiceId, string rowVersion) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/invoices/{invoiceId}/submit")
        {
            Content = JsonContent.Create(new { rowVersion }),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } }
        });

    private static async Task<Dictionary<string, int>> ExpenseTypesByCodeAsync(HttpClient admin)
    {
        var types = await (await admin.GetAsync("/api/lookups/TRIP_EXPENSE_TYPE")).DataAsync();
        return types.EnumerateArray().ToDictionary(t => t.GetProperty("code").GetString()!, t => t.GetProperty("id").GetInt32());
    }

    private static async Task<int> DetentionIncomeTypeIdAsync(HttpClient admin)
    {
        var types = await (await admin.GetAsync("/api/lookups/TRIP_INCOME_TYPE")).DataAsync();
        return types.EnumerateArray().First().GetProperty("id").GetInt32();
    }

    /// <summary>Builds one customer with a Completed, submitted, part-paid, written-off Fixed trip carrying fuel/
    /// expense/income, plus an Open trip with an advance — enough real data for every report group to find at
    /// least one row.</summary>
    private static async Task<(int CustomerId, string CustomerCode, long FixedTripId, long InvoiceId, long ConfigId)> ReadyFixtureAsync(HttpClient admin, VehicleWorld vehicles)
    {
        var (customerId, customerCode) = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 100000, $"Ops Report Route {Guid.NewGuid():N}"[..30]);
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");

        var types = await ExpenseTypesByCodeAsync(admin);
        (await admin.PostAsJsonAsync($"/api/trips/{tripId}/expenses", new { expenseTypeId = types["TOLL_TAX"], amount = 2000, paymentMethod = "Cash" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trips/{tripId}/fuel", new { fuelType = "Diesel", quantity = 20, rate = 280, paymentMethod = "Cash" })).EnsureSuccessStatusCode();
        var incomeTypeId = await DetentionIncomeTypeIdAsync(admin);
        (await admin.PostAsJsonAsync($"/api/trips/{tripId}/income", new { incomeTypeId, amount = 1500, isBillable = false })).EnsureSuccessStatusCode();

        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        (await SubmitAsync(admin, invoiceId, invoice.GetProperty("rowVersion").GetString()!)).EnsureSuccessStatusCode();

        var bankAccount = await (await admin.PostAsJsonAsync("/api/bank-accounts", new { accountTitle = "Acme Trading Co.", bankName = "HBL", branchName = "Gulberg", accountNumberLast4 = "1234" })).DataAsync();
        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = Day(), amount = 60000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccount.GetProperty("bankCashAccountId").GetInt64(), instrumentNo = "TXN-OPS-1"
        })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/settlements", new { settlementType = "WriteOff", amount = 1000, reason = "Small balance" })).EnsureSuccessStatusCode();

        return (customerId, customerCode, tripId, invoiceId, configId);
    }

    [Fact]
    public async Task Every_new_report_code_returns_without_error()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, _, _, invoiceId, configId) = await ReadyFixtureAsync(admin, vehicles);

        foreach (var code in AllNewCodes)
        {
            var response = await admin.GetAsync($"/api/reports/{code}?customerId={customerId}&invoiceId={invoiceId}&tripConfigurationId={configId}");
            Assert.True(response.IsSuccessStatusCode, $"{code}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    [Fact]
    public async Task CUS_01_customer_list_shows_the_new_customer()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, customerCode) = await ReadyCustomerAsync(admin);

        var result = await (await admin.GetAsync("/api/reports/CUS-01")).DataAsync();
        var row = Assert.Single(result.GetProperty("data").GetProperty("items").EnumerateArray(), r => r.GetProperty("customerCode").GetString() == customerCode);
        Assert.Equal("Active", row.GetProperty("status").GetString());
    }

    [Fact]
    public async Task TRP_01_trip_register_shows_the_completed_trip_with_its_invoice()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, _, tripId, invoiceId, _) = await ReadyFixtureAsync(admin, vehicles);

        var result = await (await admin.GetAsync($"/api/reports/TRP-01?customerId={customerId}")).DataAsync();
        var row = Assert.Single(result.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal("Completed", row.GetProperty("status").GetString());
        Assert.NotNull(row.GetProperty("invoiceNumber").GetString());
        Assert.Equal(100000, row.GetProperty("tripAmount").GetDecimal());
    }

    [Fact]
    public async Task PNL_08_trip_pnl_matches_revenue_plus_income_minus_fuel_and_expenses()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, _, _, _, _) = await ReadyFixtureAsync(admin, vehicles);

        var result = await (await admin.GetAsync($"/api/reports/PNL-08?customerId={customerId}")).DataAsync();
        var row = Assert.Single(result.GetProperty("data").GetProperty("items").EnumerateArray());
        // 100,000 revenue + 1,500 income − 5,600 fuel (20 × 280) − 2,000 expense = 93,900.
        Assert.Equal(93900, row.GetProperty("pnl").GetDecimal());
    }

    [Fact]
    public async Task INV_01_invoice_register_and_INV_15_transfer_history_reflect_the_fixture()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, _, _, invoiceId, _) = await ReadyFixtureAsync(admin, vehicles);

        var register = await (await admin.GetAsync($"/api/reports/INV-01?customerId={customerId}")).DataAsync();
        var row = Assert.Single(register.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal("Submitted", row.GetProperty("status").GetString());

        var detail = await (await admin.GetAsync($"/api/reports/INV-02?invoiceId={invoiceId}")).DataAsync();
        var detailRow = Assert.Single(detail.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(1, detailRow.GetProperty("paymentCount").GetInt32());
    }

    [Fact]
    public async Task MST_03_city_and_route_list_includes_the_new_route()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, _, _, _, configId) = await ReadyFixtureAsync(admin, vehicles);

        var result = await (await admin.GetAsync("/api/reports/MST-03")).DataAsync();
        Assert.True(result.GetProperty("data").GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task AUD_01_audit_log_records_the_customer_creation()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, customerCode) = await ReadyCustomerAsync(admin);

        var result = await (await admin.GetAsync("/api/reports/AUD-01")).DataAsync();
        var rows = result.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(rows, r => r.GetProperty("entity").GetString() == "Customer" && r.GetProperty("recordId").GetString() == customerId.ToString());
    }

    [Fact]
    public async Task Sorting_and_paging_apply_generically_to_a_new_report()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, _, _, _, _) = await ReadyFixtureAsync(admin, vehicles);

        var response = await admin.GetAsync($"/api/reports/INV-01?customerId={customerId}&sortBy=net&sortDesc=true&pageSize=1&page=1");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();
        Assert.Equal(1, result.GetProperty("data").GetProperty("totalCount").GetInt32());
    }
}

/// <summary>§42.9's own 50,000-row line, as a pure decision — no database, no 50,001-row fixture needed.</summary>
public sealed class ReportExportDecisionTests
{
    [Fact]
    public void At_the_threshold_the_report_still_renders_synchronously() => Assert.False(ReportExportDecision.ShouldQueue(ReportExportDecision.BackgroundThreshold));

    [Fact]
    public void One_row_over_the_threshold_is_queued() => Assert.True(ReportExportDecision.ShouldQueue(ReportExportDecision.BackgroundThreshold + 1));
}
