using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-33: write-off and discount (§37.5, §40A L6-L7, AC-61).</summary>
[Collection(ApiCollection.Name)]
public sealed class InvoiceSettlementTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT,
             PermissionCodes.TRP_PAYMENT_CREATE, PermissionCodes.TRP_BANKACCOUNT_MANAGE,
             PermissionCodes.TRP_PAYMENT_WRITEOFF, PermissionCodes.TRP_PAYMENT_DISCOUNT]);
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
            accountTitle = "Acme Trading Co.", bankName = "Habib Bank Limited", branchName = "Gulberg", accountNumberLast4 = "4521"
        })).DataAsync();
        return account.GetProperty("bankCashAccountId").GetInt64();
    }

    private static async Task<System.Text.Json.JsonElement> ReadySubmittedInvoiceAsync(VehicleWorld vehicles, HttpClient admin, int customerId, decimal rateAmount = 100000)
    {
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, rateAmount, $"Settlement Route {Guid.NewGuid():N}"[..22]);
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoiceResponse = await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } });
        Assert.True(invoiceResponse.IsSuccessStatusCode, await invoiceResponse.Content.ReadAsStringAsync());
        var invoice = await invoiceResponse.DataAsync();
        var submitResponse = await SubmitAsync(admin, invoice.GetProperty("invoiceId").GetInt64(), new { rowVersion = invoice.GetProperty("rowVersion").GetString() });
        Assert.True(submitResponse.IsSuccessStatusCode, await submitResponse.Content.ReadAsStringAsync());
        return await submitResponse.DataAsync();
    }

    [Fact]
    public async Task AC_61_a_discount_posts_a_credit_and_reduces_the_balance()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId, rateAmount: 100000);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/settlements", new { settlementType = "Discount", amount = 5000, reason = "Volume discount August" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var model = await response.DataAsync();
        Assert.StartsWith("STL-", model.GetProperty("settlementNumber").GetString());
        Assert.Equal(95000, model.GetProperty("invoiceBalance").GetDecimal());
        Assert.Equal("PartiallyPaid", model.GetProperty("invoicePaymentStatus").GetString());

        var reloaded = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();
        Assert.Equal(95000, reloaded.GetProperty("balanceAmount").GetDecimal());
    }

    [Fact]
    public async Task Forty_a_write_off_example_a_partial_payment_plus_write_off_pays_the_invoice()
    {
        // §40A.3's own worked example: balance 50,000; customer pays 49,500 and Finance writes off 500.
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId, rateAmount: 50000);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var bankAccountId = await ReadyBankAccountAsync(admin);

        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = Day(), amount = 49500, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-STL-1"
        })).EnsureSuccessStatusCode();

        var settlement = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/settlements", new { settlementType = "WriteOff", amount = 500, reason = "Short payment accepted" });
        Assert.True(settlement.IsSuccessStatusCode, await settlement.Content.ReadAsStringAsync());
        var model = await settlement.DataAsync();
        Assert.Equal(0, model.GetProperty("invoiceBalance").GetDecimal());
        Assert.Equal("Paid", model.GetProperty("invoicePaymentStatus").GetString());
    }

    [Fact]
    public async Task Settle_remaining_balance_on_record_payment_writes_off_what_is_left()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId, rateAmount: 50000);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var bankAccountId = await ReadyBankAccountAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = Day(), amount = 49500, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-STL-2",
            settleRemaining = new { settlementType = "WriteOff", reason = "Short payment accepted" }   // amount omitted ⇒ defaults to the remaining 500
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var receipt = await response.DataAsync();
        Assert.Equal(500, receipt.GetProperty("settlement").GetProperty("amount").GetDecimal());
        Assert.Equal(0, receipt.GetProperty("allocations")[0].GetProperty("invoiceBalance").GetDecimal());

        var reloaded = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();
        Assert.Equal("Paid", reloaded.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task Settle_remaining_balance_needs_the_settlement_permission_even_with_payment_create()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId, rateAmount: 50000);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var noSettlement = vehicles.As("NoSettlement", 77,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_PAYMENT_CREATE]);

        var response = await noSettlement.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = Day(), amount = 49500, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-STL-3",
            settleRemaining = new { settlementType = "WriteOff", reason = "Trying anyway" }
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Amount_cannot_exceed_the_invoice_balance()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId, rateAmount: 10000);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/settlements", new { settlementType = "WriteOff", amount = 15000, reason = "Too much" });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("AMOUNT_EXCEEDS_BALANCE", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_settlement_needs_a_reason()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/settlements", new { settlementType = "Discount", amount = 1000, reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_settlement_on_a_not_submitted_invoice_is_refused()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 10000, "Not Submitted Settlement Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoice.GetProperty("invoiceId").GetInt64()}/settlements", new { settlementType = "Discount", amount = 1000, reason = "Test" });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("INVOICE_NOT_SUBMITTED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reversing_a_settlement_posts_a_debit_and_the_original_stays_visible()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId, rateAmount: 100000);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var created = await (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/settlements", new { settlementType = "Discount", amount = 5000, reason = "Volume discount" })).DataAsync();
        var settlementId = created.GetProperty("invoiceSettlementId").GetInt64();

        var reversal = await admin.PostAsJsonAsync($"/api/invoice-settlements/{settlementId}/reverse", new { reason = "Discount granted in error" });
        Assert.True(reversal.IsSuccessStatusCode, await reversal.Content.ReadAsStringAsync());
        var model = await reversal.DataAsync();
        Assert.Equal("Reversed", model.GetProperty("status").GetString());
        Assert.Equal(100000, model.GetProperty("invoiceBalance").GetDecimal());   // back up to Net

        var again = await admin.PostAsJsonAsync($"/api/invoice-settlements/{settlementId}/reverse", new { reason = "Second attempt" });
        Assert.Equal((HttpStatusCode)422, again.StatusCode);
        Assert.Contains("SETTLEMENT_ALREADY_REVERSED", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Creating_and_reversing_settlements_each_need_their_own_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId, rateAmount: 50000);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var discountOnly = vehicles.As("DiscountOnly", 66,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_PAYMENT_DISCOUNT]);

        // Discount-only cannot create a Write-off.
        Assert.Equal(HttpStatusCode.Forbidden, (await discountOnly.PostAsJsonAsync($"/api/invoices/{invoiceId}/settlements", new { settlementType = "WriteOff", amount = 1000, reason = "No permission" })).StatusCode);

        // But can create a Discount, and can reverse it (holding either permission is enough to reverse).
        var discount = await discountOnly.PostAsJsonAsync($"/api/invoices/{invoiceId}/settlements", new { settlementType = "Discount", amount = 1000, reason = "Allowed" });
        Assert.True(discount.IsSuccessStatusCode, await discount.Content.ReadAsStringAsync());
        var settlementId = (await discount.DataAsync()).GetProperty("invoiceSettlementId").GetInt64();
        Assert.True((await discountOnly.PostAsJsonAsync($"/api/invoice-settlements/{settlementId}/reverse", new { reason = "Allowed too" })).IsSuccessStatusCode);

        var noPermission = vehicles.As("NoSettlementAtAll", 65, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_VIEW]);
        Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.PostAsJsonAsync($"/api/invoices/{invoiceId}/settlements", new { settlementType = "Discount", amount = 1000, reason = "No permission" })).StatusCode);
    }
}
