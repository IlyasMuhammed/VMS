using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-32: payment reversal (§37 BR-P5, §40A L5, AC-40).</summary>
[Collection(ApiCollection.Name)]
public sealed class PaymentReversalTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT,
             PermissionCodes.TRP_PAYMENT_CREATE, PermissionCodes.TRP_PAYMENT_REVERSE, PermissionCodes.TRP_BANKACCOUNT_MANAGE]);
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

    private static async Task<System.Text.Json.JsonElement> ReadySubmittedInvoiceAsync(VehicleWorld vehicles, HttpClient admin, int customerId, decimal rateAmount = 25000)
    {
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, rateAmount, $"Reversal Route {Guid.NewGuid():N}"[..22]);
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoiceResponse = await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } });
        Assert.True(invoiceResponse.IsSuccessStatusCode, await invoiceResponse.Content.ReadAsStringAsync());
        var invoice = await invoiceResponse.DataAsync();
        var submitResponse = await SubmitAsync(admin, invoice.GetProperty("invoiceId").GetInt64(), new { rowVersion = invoice.GetProperty("rowVersion").GetString() });
        Assert.True(submitResponse.IsSuccessStatusCode, await submitResponse.Content.ReadAsStringAsync());
        return await submitResponse.DataAsync();
    }

    private static async Task<(long InvoicePaymentId, long InvoiceId)> ReadyPaymentAsync(HttpClient admin, long invoiceId, long bankAccountId, decimal amount, string instrumentNo)
    {
        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = Day(), amount, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var allocation = (await response.DataAsync()).GetProperty("allocations")[0];
        return (allocation.GetProperty("invoicePaymentId").GetInt64(), invoiceId);
    }

    [Fact]
    public async Task AC_40_reversing_a_payment_posts_a_debit_and_the_original_stays_visible()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId, rateAmount: 500000);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var (paymentId, _) = await ReadyPaymentAsync(admin, invoiceId, bankAccountId, 200000, "TXN-REV-1");

        var beforeReversal = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();
        Assert.Equal(300000, beforeReversal.GetProperty("balanceAmount").GetDecimal());

        var reversal = await admin.PostAsJsonAsync($"/api/invoice-payments/{paymentId}/reverse", new { reason = "Cheque bounced" });
        Assert.True(reversal.IsSuccessStatusCode, await reversal.Content.ReadAsStringAsync());
        var model = await reversal.DataAsync();
        Assert.Equal("Reversed", model.GetProperty("status").GetString());
        Assert.Equal(500000, model.GetProperty("invoiceBalance").GetDecimal());   // balance increases by 200,000 back to Net
        Assert.Equal("Unpaid", model.GetProperty("invoicePaymentStatus").GetString());

        var afterReversal = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();
        Assert.Equal(500000, afterReversal.GetProperty("balanceAmount").GetDecimal());
        Assert.Equal("Unpaid", afterReversal.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task Reversing_an_already_reversed_payment_is_refused()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId, rateAmount: 100000);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var (paymentId, _) = await ReadyPaymentAsync(admin, invoiceId, bankAccountId, 50000, "TXN-REV-2");

        (await admin.PostAsJsonAsync($"/api/invoice-payments/{paymentId}/reverse", new { reason = "First reversal" })).EnsureSuccessStatusCode();
        var again = await admin.PostAsJsonAsync($"/api/invoice-payments/{paymentId}/reverse", new { reason = "Second attempt" });
        Assert.Equal((HttpStatusCode)422, again.StatusCode);
        Assert.Contains("PAYMENT_ALREADY_REVERSED", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reversing_a_transferred_payment_points_to_the_active_invoice()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId, rateAmount: 100000);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var (paymentId, _) = await ReadyPaymentAsync(admin, invoiceId, bankAccountId, 50000, "TXN-REV-3");

        // §37: "Transferred = moved to a regenerated invoice" — only CC-36 (not built) ever sets this for real;
        // simulated directly here, the same "stand-in via SQL" idiom CC-17/21/29/31 already used.
        factory.Execute("UPDATE trp.InvoicePayments SET Status = 'Transferred' WHERE InvoicePaymentId = @id", ("@id", paymentId));

        var response = await admin.PostAsJsonAsync($"/api/invoice-payments/{paymentId}/reverse", new { reason = "Trying anyway" });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("PAYMENT_TRANSFERRED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reversing_needs_a_reason()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var (paymentId, _) = await ReadyPaymentAsync(admin, invoiceId, bankAccountId, 5000, "TXN-REV-4");

        var response = await admin.PostAsJsonAsync($"/api/invoice-payments/{paymentId}/reverse", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_reversal_on_a_fully_paid_invoice_moves_it_back_to_partially_paid()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId, rateAmount: 100000);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var bankAccountId = await ReadyBankAccountAsync(admin);
        // Two partial payments that together fully pay the invoice.
        var (payment1, _) = await ReadyPaymentAsync(admin, invoiceId, bankAccountId, 60000, "TXN-REV-5A");
        await ReadyPaymentAsync(admin, invoiceId, bankAccountId, 40000, "TXN-REV-5B");
        var fullyPaid = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();
        Assert.Equal("Paid", fullyPaid.GetProperty("paymentStatus").GetString());

        var reversal = await admin.PostAsJsonAsync($"/api/invoice-payments/{payment1}/reverse", new { reason = "Cheque bounced" });
        Assert.True(reversal.IsSuccessStatusCode, await reversal.Content.ReadAsStringAsync());
        var model = await reversal.DataAsync();
        Assert.Equal("PartiallyPaid", model.GetProperty("invoicePaymentStatus").GetString());
        Assert.Equal(60000, model.GetProperty("invoiceBalance").GetDecimal());
    }

    [Fact]
    public async Task Reversing_needs_its_own_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(vehicles, admin, customerId);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var (paymentId, _) = await ReadyPaymentAsync(admin, invoiceId, bankAccountId, 5000, "TXN-REV-6");
        var noPermission = vehicles.As("NoReverse", 88,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_PAYMENT_CREATE]);

        var response = await noPermission.PostAsJsonAsync($"/api/invoice-payments/{paymentId}/reverse", new { reason = "No permission" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
