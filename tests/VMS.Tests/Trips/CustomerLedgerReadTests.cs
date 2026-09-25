using System.Net;
using System.Net.Http.Json;
using System.Text;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-38: Customer Ledger statement, Invoice Ledger tab, Customer Balances (§40A.4, §47.2, AC-48).
/// Opening balance/period lock (LR-4/LR-7) is CC-39's own job — not exercised here.</summary>
[Collection(ApiCollection.Name)]
public sealed class CustomerLedgerReadTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT,
             PermissionCodes.TRP_PAYMENT_CREATE, PermissionCodes.TRP_BANKACCOUNT_MANAGE, PermissionCodes.TRP_LEDGER_VIEW]);
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

    /// <summary>Creates and submits one invoice for <paramref name="tripAmount"/> dated <paramref name="submittedOn"/>
    /// (the ledger's own L1 entry date, freely chosen independent of the trip's real completion date, which is
    /// always "today" — <c>TripLifecycleService</c>'s own Completed transition stamps it, the request body's
    /// <c>tripDate</c> is only ever the trip's own scheduling field).</summary>
    private static async Task<long> SubmittedInvoiceAsync(HttpClient admin, VehicleWorld vehicles, int customerId, decimal tripAmount, string routeName, string submittedOn)
    {
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, tripAmount, routeName);
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var submitResponse = await admin.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/invoices/{invoiceId}/submit")
        {
            Content = JsonContent.Create(new { rowVersion = invoice.GetProperty("rowVersion").GetString(), submittedOn }),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } }
        });
        Assert.True(submitResponse.IsSuccessStatusCode, await submitResponse.Content.ReadAsStringAsync());
        return invoiceId;
    }

    [Fact]
    public async Task AC_48_statement_computes_opening_period_and_closing_balance()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoiceId = await SubmittedInvoiceAsync(admin, vehicles, customerId, 100000, "Statement Route", "2026-08-15");   // opening: 100,000 Dr

        var bankAccountId = await ReadyBankAccountAsync(admin);
        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = "2026-09-10", amount = 20000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-STMT-1"
        })).EnsureSuccessStatusCode();

        var response = await admin.GetAsync($"/api/customers/{customerId}/ledger?from=2026-09-01&to=2026-09-30");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var statement = await response.DataAsync();

        Assert.Equal(100000, statement.GetProperty("openingBalance").GetDecimal());
        Assert.Equal(0, statement.GetProperty("periodDebits").GetDecimal());
        Assert.Equal(20000, statement.GetProperty("periodCredits").GetDecimal());
        Assert.Equal(80000, statement.GetProperty("closingBalance").GetDecimal());

        var rows = statement.GetProperty("rows").EnumerateArray().ToList();
        var row = Assert.Single(rows);
        Assert.Equal("PAYMENT", row.GetProperty("entryType").GetString());
        Assert.Equal(80000, row.GetProperty("runningBalance").GetDecimal());   // opening 100,000 − 20,000
    }

    [Fact]
    public async Task Invoice_ledger_reconciles_with_the_invoice_balance()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoiceId = await SubmittedInvoiceAsync(admin, vehicles, customerId, 80000, "Reconcile Route", Day());

        var bankAccountId = await ReadyBankAccountAsync(admin);
        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = Day(), amount = 30000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-RECON-1"
        })).EnsureSuccessStatusCode();

        var response = await admin.GetAsync($"/api/invoices/{invoiceId}/ledger");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();

        Assert.Equal(50000, result.GetProperty("invoiceBalance").GetDecimal());
        Assert.Equal(50000, result.GetProperty("ledgerBalance").GetDecimal());
        Assert.True(result.GetProperty("reconciled").GetBoolean());
        Assert.Equal(2, result.GetProperty("entries").GetArrayLength());   // L1 invoice debit + L4 payment credit
    }

    [Fact]
    public async Task Customer_balance_and_the_all_customer_list_reflect_the_ledger()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        await SubmittedInvoiceAsync(admin, vehicles, customerId, 60000, "Balance Route", Day());

        var single = await (await admin.GetAsync($"/api/customers/{customerId}/balance")).DataAsync();
        var row = Assert.Single(single.EnumerateArray());
        Assert.Equal(60000, row.GetProperty("balanceAmount").GetDecimal());

        var all = await (await admin.GetAsync("/api/customer-balances")).DataAsync();
        var summary = Assert.Single(all.EnumerateArray(), b => b.GetProperty("customerId").GetInt32() == customerId);
        Assert.Equal(60000, summary.GetProperty("balanceAmount").GetDecimal());
        Assert.Equal(0, summary.GetProperty("creditAmount").GetDecimal());
    }

    [Fact]
    public async Task Overdue_amount_reflects_an_invoice_past_its_due_date()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoiceId = await SubmittedInvoiceAsync(admin, vehicles, customerId, 40000, "Overdue Route", Day());

        // §40A.4's own "overdue amount" needs a due date in the past — stand in for a real one via direct SQL,
        // the same idiom CC-17/21/29/31 already use for a scenario the API itself does not need to produce.
        factory.Execute("UPDATE trp.Invoices SET DueDate = @due WHERE InvoiceId = @id", ("@due", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5)), ("@id", invoiceId));

        var statement = await (await admin.GetAsync($"/api/customers/{customerId}/ledger")).DataAsync();
        Assert.Equal(40000, statement.GetProperty("overdueAmount").GetDecimal());
    }

    [Fact]
    public async Task Statement_pdf_export_returns_a_pdf()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        await SubmittedInvoiceAsync(admin, vehicles, customerId, 25000, "PDF Route", Day());

        var response = await admin.GetAsync($"/api/customers/{customerId}/ledger/statement.pdf");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task Ledger_reads_need_their_own_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);

        var noPermission = vehicles.As("No Permission", 2,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_VIEW]);

        var response = await noPermission.GetAsync($"/api/customers/{customerId}/ledger");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
