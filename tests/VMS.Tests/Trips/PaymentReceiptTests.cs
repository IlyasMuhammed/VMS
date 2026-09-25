using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-31: bank accounts and payment receipts (§37, AC-36, AC-39, AC-46, AC-63). Reversal (BR-P5) is
/// CC-32's own job — none of these tests exercise it.</summary>
[Collection(ApiCollection.Name)]
public sealed class PaymentReceiptTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT,
             PermissionCodes.TRP_PAYMENT_CREATE, PermissionCodes.TRP_BANKACCOUNT_MANAGE]);
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

    /// <summary>Submitted invoice with the given Net amount, ready for a payment. A second (or later) call for
    /// the SAME customer needs its own, non-overlapping billing period — <paramref name="periodFromOffset"/>/
    /// <paramref name="periodToOffset"/> pick one, and the trip's own CompletionDate is nudged into it directly
    /// by SQL (the eligibility rule is period-vs-CompletionDate, not period-vs-tripDate, and a live trip's
    /// CompletionDate is always "now" — the same "stand-in via direct SQL" idiom used elsewhere this register).</summary>
    private static async Task<System.Text.Json.JsonElement> ReadySubmittedInvoiceAsync(
        ApiFactory factory, VehicleWorld vehicles, HttpClient admin, int customerId, decimal rateAmount = 25000, int periodFromOffset = -30, int periodToOffset = 30)
    {
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, rateAmount, $"Payment Route {Guid.NewGuid():N}"[..22]);
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        if (periodFromOffset != -30 || periodToOffset != 30)
            factory.Execute("UPDATE trp.Trips SET CompletionDate = @date WHERE TripId = @id", ("@date", DateOnly.Parse(Day(periodFromOffset + 1))), ("@id", tripId));

        var invoiceResponse = await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(periodFromOffset), periodTo = Day(periodToOffset), tripIds = new[] { tripId } });
        Assert.True(invoiceResponse.IsSuccessStatusCode, await invoiceResponse.Content.ReadAsStringAsync());
        var invoice = await invoiceResponse.DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var submitResponse = await SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() });
        Assert.True(submitResponse.IsSuccessStatusCode, await submitResponse.Content.ReadAsStringAsync());
        return await submitResponse.DataAsync();
    }

    [Fact]
    public async Task Bank_accounts_can_be_created_listed_and_updated()
    {
        var (_, admin) = await WorldAsync(factory);
        var create = await admin.PostAsJsonAsync("/api/bank-accounts", new { accountTitle = "Acme Trading Co.", bankName = "MCB", branchName = "Main", accountNumberLast4 = "1234" });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        var account = await create.DataAsync();
        Assert.Equal("1234", account.GetProperty("accountNumberLast4").GetString());

        var list = await (await admin.GetAsync("/api/bank-accounts")).DataAsync();
        Assert.Contains(list.EnumerateArray(), a => a.GetProperty("bankCashAccountId").GetInt64() == account.GetProperty("bankCashAccountId").GetInt64());

        var update = await admin.PutAsJsonAsync($"/api/bank-accounts/{account.GetProperty("bankCashAccountId").GetInt64()}", new
        {
            accountTitle = "Acme Trading Co.", bankName = "MCB", branchName = "Main", accountNumberLast4 = "1234", isActive = false, rowVersion = account.GetProperty("rowVersion").GetString()
        });
        Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());
        Assert.False((await update.DataAsync()).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task AC_63_only_direct_to_account_and_bank_cheque_are_accepted()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(factory, vehicles, admin, customerId);
        var bankAccountId = await ReadyBankAccountAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoice.GetProperty("invoiceId").GetInt64()}/payments", new
        {
            receiptDate = Day(), amount = 1000, paymentMethod = "Cash", bankCashAccountId = bankAccountId, instrumentNo = "X1"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC_36_a_partial_payment_posts_one_credit_and_updates_the_balance()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(factory, vehicles, admin, customerId, rateAmount: 500000);   // Net 500,000 with no adjustments/deductions
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var bankAccountId = await ReadyBankAccountAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = Day(), amount = 200000, paymentMethod = "BankCheque", bankCashAccountId = bankAccountId,
            instrumentNo = "004512", instrumentDate = Day(-2), drawnOnBank = "HBL"
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var receipt = await response.DataAsync();
        Assert.StartsWith("RCPT-", receipt.GetProperty("receiptNumber").GetString());
        var allocation = receipt.GetProperty("allocations")[0];
        Assert.Equal(300000, allocation.GetProperty("invoiceBalance").GetDecimal());
        Assert.Equal("PartiallyPaid", allocation.GetProperty("invoicePaymentStatus").GetString());
        Assert.True(allocation.GetProperty("ledgerEntryId").GetInt64() > 0);

        var reloaded = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();
        Assert.Equal(300000, reloaded.GetProperty("balanceAmount").GetDecimal());
        Assert.Equal("PartiallyPaid", reloaded.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task A_full_payment_marks_the_invoice_paid()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(factory, vehicles, admin, customerId, rateAmount: 25000);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var bankAccountId = await ReadyBankAccountAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = Day(), amount = 25000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-9981"
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var allocation = (await response.DataAsync()).GetProperty("allocations")[0];
        Assert.Equal(0, allocation.GetProperty("invoiceBalance").GetDecimal());
        Assert.Equal("Paid", allocation.GetProperty("invoicePaymentStatus").GetString());
    }

    [Fact]
    public async Task AC_39_a_multi_invoice_receipt_posts_one_credit_per_invoice()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice1 = await ReadySubmittedInvoiceAsync(factory, vehicles, admin, customerId, rateAmount: 100000);
        var invoice2 = await ReadySubmittedInvoiceAsync(factory, vehicles, admin, customerId, rateAmount: 200000, periodFromOffset: -100, periodToOffset: -31);
        var bankAccountId = await ReadyBankAccountAsync(admin);

        var response = await admin.PostAsJsonAsync("/api/customer-receipts", new
        {
            customerId, receiptDate = Day(), receiptAmount = 300000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-7781",
            allocations = new[]
            {
                new { invoiceId = invoice1.GetProperty("invoiceId").GetInt64(), amount = 100000 },
                new { invoiceId = invoice2.GetProperty("invoiceId").GetInt64(), amount = 200000 }
            }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var receipt = await response.DataAsync();
        var allocations = receipt.GetProperty("allocations").EnumerateArray().ToList();
        Assert.Equal(2, allocations.Count);
        Assert.Equal(2, allocations.Select(a => a.GetProperty("ledgerEntryId").GetInt64()).Distinct().Count());
        Assert.All(allocations, a => Assert.Equal("Paid", a.GetProperty("invoicePaymentStatus").GetString()));
    }

    [Fact]
    public async Task Allocations_must_sum_to_the_receipt_amount()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(factory, vehicles, admin, customerId);
        var bankAccountId = await ReadyBankAccountAsync(admin);

        var response = await admin.PostAsJsonAsync("/api/customer-receipts", new
        {
            customerId, receiptDate = Day(), receiptAmount = 300000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-1",
            allocations = new[] { new { invoiceId = invoice.GetProperty("invoiceId").GetInt64(), amount = 100000 } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bank_cheque_requires_instrument_date_and_drawn_on_bank()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(factory, vehicles, admin, customerId);
        var bankAccountId = await ReadyBankAccountAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoice.GetProperty("invoiceId").GetInt64()}/payments", new
        {
            receiptDate = Day(), amount = 5000, paymentMethod = "BankCheque", bankCashAccountId = bankAccountId, instrumentNo = "004512"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Payment_on_a_not_submitted_invoice_is_refused()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Not Submitted Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var bankAccountId = await ReadyBankAccountAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoice.GetProperty("invoiceId").GetInt64()}/payments", new
        {
            receiptDate = Day(), amount = 1000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-2"
        });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("INVOICE_NOT_SUBMITTED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BR_P2_overpayment_needs_confirmation_then_succeeds()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(factory, vehicles, admin, customerId, rateAmount: 25000);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var bankAccountId = await ReadyBankAccountAsync(admin);

        var refused = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = Day(), amount = 30000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-3"
        });
        Assert.Equal((HttpStatusCode)422, refused.StatusCode);
        Assert.Contains("OVERPAYMENT_CONFIRMATION_REQUIRED", await refused.Content.ReadAsStringAsync());

        var confirmed = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = Day(), amount = 30000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-3", confirmOverpayment = true
        });
        Assert.True(confirmed.IsSuccessStatusCode, await confirmed.Content.ReadAsStringAsync());
        var allocation = (await confirmed.DataAsync()).GetProperty("allocations")[0];
        Assert.Equal(-5000, allocation.GetProperty("invoiceBalance").GetDecimal());
        Assert.Equal("Paid", allocation.GetProperty("invoicePaymentStatus").GetString());
    }

    [Fact]
    public async Task BR_P4_a_duplicate_instrument_needs_confirmation_then_succeeds()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice1 = await ReadySubmittedInvoiceAsync(factory, vehicles, admin, customerId, rateAmount: 100000);
        var invoice2 = await ReadySubmittedInvoiceAsync(factory, vehicles, admin, customerId, rateAmount: 100000, periodFromOffset: -100, periodToOffset: -31);
        var bankAccountId = await ReadyBankAccountAsync(admin);

        var first = await admin.PostAsJsonAsync($"/api/invoices/{invoice1.GetProperty("invoiceId").GetInt64()}/payments", new
        {
            receiptDate = Day(), amount = 50000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-DUP"
        });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

        var refused = await admin.PostAsJsonAsync($"/api/invoices/{invoice2.GetProperty("invoiceId").GetInt64()}/payments", new
        {
            receiptDate = Day(), amount = 50000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-DUP"
        });
        Assert.Equal((HttpStatusCode)422, refused.StatusCode);
        Assert.Contains("DUPLICATE_INSTRUMENT", await refused.Content.ReadAsStringAsync());

        var confirmed = await admin.PostAsJsonAsync($"/api/invoices/{invoice2.GetProperty("invoiceId").GetInt64()}/payments", new
        {
            receiptDate = Day(), amount = 50000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-DUP", confirmDuplicate = true
        });
        Assert.True(confirmed.IsSuccessStatusCode, await confirmed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AC_46_payment_on_a_replaced_invoice_points_to_the_replacement()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(factory, vehicles, admin, customerId);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var replacement = await ReadySubmittedInvoiceAsync(factory, vehicles, admin, await ReadyCustomerAsync(admin));
        var bankAccountId = await ReadyBankAccountAsync(admin);

        // §36's "Inactive" status only exists once regeneration (CC-35) is built — simulated directly here, the
        // same "stand-in via direct SQL for a scenario a later task reaches for real" idiom CC-17/21/29 already used.
        factory.Execute("UPDATE trp.Invoices SET Status = 'Inactive', ReplacedByInvoiceId = @replacementId WHERE InvoiceId = @id",
            ("@replacementId", replacement.GetProperty("invoiceId").GetInt64()), ("@id", invoiceId));

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = Day(), amount = 1000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-4"
        });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVOICE_INACTIVE", body);
        Assert.Contains(replacement.GetProperty("invoiceNumber").GetString()!, body);
    }

    [Fact]
    public async Task Recording_a_payment_and_managing_bank_accounts_each_need_their_own_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoice = await ReadySubmittedInvoiceAsync(factory, vehicles, admin, customerId);
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var noPermission = vehicles.As("NoPayment", 99, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_VIEW]);

        Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.PostAsJsonAsync($"/api/invoices/{invoice.GetProperty("invoiceId").GetInt64()}/payments", new
        {
            receiptDate = Day(), amount = 1000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-5"
        })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.PostAsJsonAsync("/api/bank-accounts", new
        {
            accountTitle = "X", bankName = "X", branchName = "X", accountNumberLast4 = "0000"
        })).StatusCode);
    }
}
