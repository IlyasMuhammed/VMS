using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-37: carry forward and refund of customer credit (§40, §40A L14-L15, AC-62). The un-applied-advance
/// half of L15 is CC-34's own job (<c>AdvanceService.RefundAsync</c>) — not exercised here.</summary>
[Collection(ApiCollection.Name)]
public sealed class CustomerCreditTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT,
             PermissionCodes.TRP_PAYMENT_CREATE, PermissionCodes.TRP_PAYMENT_CARRYFORWARD, PermissionCodes.TRP_PAYMENT_REFUND, PermissionCodes.TRP_BANKACCOUNT_MANAGE]);
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

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, long invoiceId, object body, string? idempotencyKey = null) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/invoices/{invoiceId}/submit")
        {
            Content = JsonContent.Create(body),
            Headers = { { "Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString() } }
        });

    private static async Task<long> ReadyBankAccountAsync(HttpClient admin)
    {
        var account = await (await admin.PostAsJsonAsync("/api/bank-accounts", new
        {
            accountTitle = "Acme Trading Co.", bankName = "HBL", branchName = "Gulberg", accountNumberLast4 = "1234"
        })).DataAsync();
        return account.GetProperty("bankCashAccountId").GetInt64();
    }

    /// <summary>Submits a Fixed-rate invoice for <paramref name="tripAmount"/> and pays it exactly
    /// <paramref name="paidAmount"/> (confirming overpayment when it exceeds the net), leaving whatever balance
    /// that implies.</summary>
    private static async Task<(long InvoiceId, string InvoiceNumber)> SubmittedInvoiceAsync(
        HttpClient admin, VehicleWorld vehicles, int customerId, long bankAccountId, decimal tripAmount, decimal paidAmount, string routeName, string instrumentNo, int periodFromOffset, int periodToOffset)
    {
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, tripAmount, routeName);
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoiceResponse = await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(periodFromOffset), periodTo = Day(periodToOffset), tripIds = new[] { tripId } });
        var invoiceBody = await invoiceResponse.Content.ReadAsStringAsync();
        Assert.True(invoiceResponse.IsSuccessStatusCode, invoiceBody);
        var invoice = await invoiceResponse.DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var submitResponse = await SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() });
        Assert.True(submitResponse.IsSuccessStatusCode, await submitResponse.Content.ReadAsStringAsync());
        var submitted = await submitResponse.DataAsync();

        if (paidAmount > 0)
        {
            (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
            {
                receiptDate = Day(), amount = paidAmount, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo, confirmOverpayment = true
            })).EnsureSuccessStatusCode();
        }
        return (invoiceId, submitted.GetProperty("invoiceNumber").GetString()!);
    }

    [Fact]
    public async Task AC_62_carry_forward_reproduces_the_worked_example()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var (inv1Id, _) = await SubmittedInvoiceAsync(admin, vehicles, customerId, bankAccountId, 100000, 150000, "CF Route 1", "TXN-CF-1", -30, -1);   // credit 50,000
        var (inv2Id, _) = await SubmittedInvoiceAsync(admin, vehicles, customerId, bankAccountId, 200000, 0, "CF Route 2", "TXN-CF-2", 10, 60);        // balance 200,000

        var before = await (await admin.GetAsync($"/api/invoices/{inv1Id}")).DataAsync();
        Assert.Equal(-50000, before.GetProperty("balanceAmount").GetDecimal());

        var response = await admin.PostAsJsonAsync($"/api/invoices/{inv1Id}/carry-forward", new { targetInvoiceId = inv2Id, amount = 50000, reason = "Move credit to INV-2" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();
        Assert.Equal(0, result.GetProperty("sourceInvoiceBalance").GetDecimal());
        Assert.Equal(150000, result.GetProperty("targetInvoiceBalance").GetDecimal());

        var inv1Reloaded = await (await admin.GetAsync($"/api/invoices/{inv1Id}")).DataAsync();
        var inv2Reloaded = await (await admin.GetAsync($"/api/invoices/{inv2Id}")).DataAsync();
        Assert.Equal(0, inv1Reloaded.GetProperty("balanceAmount").GetDecimal());
        Assert.Equal(150000, inv2Reloaded.GetProperty("balanceAmount").GetDecimal());
    }

    [Fact]
    public async Task Carry_forward_cannot_exceed_the_available_credit()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var (inv1Id, _) = await SubmittedInvoiceAsync(admin, vehicles, customerId, bankAccountId, 100000, 120000, "CF Cap Route 1", "TXN-CF-3", -30, -1);   // credit 20,000
        var (inv2Id, _) = await SubmittedInvoiceAsync(admin, vehicles, customerId, bankAccountId, 200000, 0, "CF Cap Route 2", "TXN-CF-4", 10, 60);

        var response = await admin.PostAsJsonAsync($"/api/invoices/{inv1Id}/carry-forward", new { targetInvoiceId = inv2Id, amount = 20001, reason = "Too much" });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("AMOUNT_EXCEEDS_CREDIT", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Carry_forward_target_must_be_an_open_submitted_invoice()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var (inv1Id, _) = await SubmittedInvoiceAsync(admin, vehicles, customerId, bankAccountId, 100000, 120000, "CF NotSubmitted Route 1", "TXN-CF-5", -30, -1);

        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 50000, "CF NotSubmitted Route 2");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-11");
        var draftInvoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(10), periodTo = Day(60), tripIds = new[] { tripId } })).DataAsync();
        var draftInvoiceId = draftInvoice.GetProperty("invoiceId").GetInt64();   // Generated, never Submitted

        var response = await admin.PostAsJsonAsync($"/api/invoices/{inv1Id}/carry-forward", new { targetInvoiceId = draftInvoiceId, amount = 10000, reason = "Wrong target status" });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("INVOICE_NOT_SUBMITTED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Refund_posts_a_debit_and_reduces_the_credit()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var (invoiceId, _) = await SubmittedInvoiceAsync(admin, vehicles, customerId, bankAccountId, 100000, 150000, "Refund Route", "TXN-RF-1", -30, 30);   // credit 50,000

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/refund", new
        {
            amount = 20000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, reference = "REF-001", reason = "Partial refund of credit"
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();
        Assert.Equal(-30000, result.GetProperty("invoiceBalance").GetDecimal());

        var reloaded = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();
        Assert.Equal(-30000, reloaded.GetProperty("balanceAmount").GetDecimal());
        Assert.Equal(20000, reloaded.GetProperty("refundedAmount").GetDecimal());
    }

    [Fact]
    public async Task Refund_cannot_exceed_the_available_credit()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var (invoiceId, _) = await SubmittedInvoiceAsync(admin, vehicles, customerId, bankAccountId, 100000, 110000, "Refund Cap Route", "TXN-RF-2", -30, 30);   // credit 10,000

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/refund", new
        {
            amount = 10001, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, reason = "Too much"
        });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("AMOUNT_EXCEEDS_CREDIT", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Carry_forward_and_refund_both_need_their_own_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var (inv1Id, _) = await SubmittedInvoiceAsync(admin, vehicles, customerId, bankAccountId, 100000, 150000, "CF Perm Route 1", "TXN-CF-6", -30, -1);
        var (inv2Id, _) = await SubmittedInvoiceAsync(admin, vehicles, customerId, bankAccountId, 200000, 0, "CF Perm Route 2", "TXN-CF-7", 10, 60);

        var noPermission = vehicles.As("No Permission", 2,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT, PermissionCodes.TRP_PAYMENT_CREATE, PermissionCodes.TRP_BANKACCOUNT_MANAGE]);

        var carryForwardResponse = await noPermission.PostAsJsonAsync($"/api/invoices/{inv1Id}/carry-forward", new { targetInvoiceId = inv2Id, amount = 1000, reason = "No permission" });
        Assert.Equal(HttpStatusCode.Forbidden, carryForwardResponse.StatusCode);

        var refundResponse = await noPermission.PostAsJsonAsync($"/api/invoices/{inv1Id}/refund", new { amount = 1000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, reason = "No permission" });
        Assert.Equal(HttpStatusCode.Forbidden, refundResponse.StatusCode);
    }
}
