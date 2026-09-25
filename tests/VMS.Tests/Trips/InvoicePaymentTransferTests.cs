using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VMS.Modules.Trips.Services;
using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-36: payment transfer on regeneration (§40, §40A L13, AC-42/43/45). 100% of every still-standing
/// credit on the old invoice — payments, applied advances, and (chained) transfers already carried onto it —
/// moves to the new invoice, uncapped.</summary>
[Collection(ApiCollection.Name)]
public sealed class InvoicePaymentTransferTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT,
             PermissionCodes.TRP_INVOICE_REGENERATE, PermissionCodes.TRP_INVOICE_CANCEL, PermissionCodes.TRP_PAYMENT_CREATE,
             PermissionCodes.TRP_PAYMENT_REVERSE, PermissionCodes.TRP_PAYMENT_ADVANCE, PermissionCodes.TRP_BANKACCOUNT_MANAGE]);
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

    private static async Task<Dictionary<string, int>> CityIdsByAbbrAsync(HttpClient admin)
    {
        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        return cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());
    }

    private static async Task<long> ReadyConfigAsync(HttpClient admin, int customerId, int vehicleId, decimal rateAmount, string routeName)
    {
        var byAbbr = await CityIdsByAbbrAsync(admin);
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

    private static async Task<long> CreateOpenTripAsync(HttpClient admin, VehicleWorld vehicles, int customerId, decimal tripAmount, string tripDate)
    {
        var cities = await CityIdsByAbbrAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var response = await admin.PostAsJsonAsync("/api/trips/open", new
        {
            customerId, from = new { locationType = "City", cityId = cities["LHR"] }, to = new { locationType = "City", cityId = cities["DGK"] },
            vehicleId = truck, driverId, tripAmount, tripDate
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var tripId = (await response.DataAsync()).GetProperty("tripId").GetInt64();
        await CompleteOpenAsync(admin, tripId);
        return tripId;
    }

    private static async Task CompleteOpenAsync(HttpClient admin, long tripId)
    {
        var trip = await (await admin.GetAsync($"/api/trips/{tripId}")).DataAsync();
        var planned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Planned", new { rowVersion = trip.GetProperty("rowVersion").GetString() })).DataAsync();
        var assigned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Assigned", new { rowVersion = planned.GetProperty("rowVersion").GetString() })).DataAsync();
        var started = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Started", new { rowVersion = assigned.GetProperty("rowVersion").GetString(), startOdometer = 100 })).DataAsync();
        var inTransit = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/InTransit", new { rowVersion = started.GetProperty("rowVersion").GetString() })).DataAsync();
        var atDelivery = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/AtDelivery", new { rowVersion = inTransit.GetProperty("rowVersion").GetString() })).DataAsync();
        var delivered = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Delivered", new { rowVersion = atDelivery.GetProperty("rowVersion").GetString(), endOdometer = 900 })).DataAsync();
        (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Completed", new { rowVersion = delivered.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();
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

    private static async Task<IReadOnlyList<VMS.Modules.Trips.Models.LedgerEntryModel>> LedgerForInvoiceAsync(ApiFactory factory, Guid tenantId, long invoiceId)
    {
        factory.CreateClient();
        IReadOnlyList<VMS.Modules.Trips.Models.LedgerEntryModel> entries = [];
        await BackgroundTenantScope.RunAsAsync(tenantId, async () =>
        {
            using var scope = factory.Services.CreateScope();
            entries = await scope.ServiceProvider.GetRequiredService<ICustomerLedgerPostingService>().ListForInvoiceAsync(invoiceId);
        });
        return entries;
    }

    [Fact]
    public async Task AC_42_100_percent_of_a_historical_payment_is_transferred()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 500000, "Transfer Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var oldInvoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var submitted = await (await SubmitAsync(admin, oldInvoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).DataAsync();

        var bankAccountId = await ReadyBankAccountAsync(admin);
        (await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/payments", new
        {
            receiptDate = Day(), amount = 200000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-TRF-1"
        })).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = "AC-42"
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();
        var transfersJson = result.GetProperty("transfers").EnumerateArray().ToList();
        var transfer = Assert.Single(transfersJson);
        Assert.Equal(200000, transfer.GetProperty("amountTransferred").GetDecimal());
        Assert.Equal("Payment", transfer.GetProperty("sourceKind").GetString());

        var newInvoice = result.GetProperty("invoice");
        Assert.Equal(200000, newInvoice.GetProperty("transferredInAmount").GetDecimal());
        Assert.Equal(300000, newInvoice.GetProperty("balanceAmount").GetDecimal());   // 500,000 − 200,000 transferred in

        // The original payment row is never deleted — it stays visible, only its own Status changes.
        var paymentStatus = factory.Scalar<string>("SELECT Status FROM trp.InvoicePayments WHERE InvoiceId = @id", ("@id", oldInvoiceId));
        Assert.Equal("Transferred", paymentStatus);
    }

    [Fact]
    public async Task AC_43_a_transfer_exceeding_the_new_invoice_leaves_a_negative_balance_shown_as_a_credit()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 500000, "Overpay Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var oldInvoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var submitted = await (await SubmitAsync(admin, oldInvoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).DataAsync();

        var bankAccountId = await ReadyBankAccountAsync(admin);
        (await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/payments", new
        {
            receiptDate = Day(), amount = 450000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-TRF-2"
        })).EnsureSuccessStatusCode();

        // Corrected down to 400,000 — smaller than the 450,000 already paid on the old invoice.
        factory.Execute("UPDATE trp.TripRates SET RateAmount = 400000 WHERE TripConfigurationId = @configId", ("@configId", configId));

        var response = await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, repriceTrips = true, regenerationReason = "AC-43"
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var newInvoice = (await response.DataAsync()).GetProperty("invoice");

        Assert.Equal(400000, newInvoice.GetProperty("netAmount").GetDecimal());
        Assert.Equal(-50000, newInvoice.GetProperty("balanceAmount").GetDecimal());
        Assert.Equal("Paid", newInvoice.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task AC_45_the_40A3_worked_example_reproduces_to_the_rupee()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 500000, "Worked Example Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var oldInvoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var submitted = await (await SubmitAsync(admin, oldInvoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).DataAsync();

        var bankAccountId = await ReadyBankAccountAsync(admin);
        (await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/payments", new
        {
            receiptDate = Day(), amount = 200000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-TRF-3"
        })).EnsureSuccessStatusCode();

        factory.Execute("UPDATE trp.TripRates SET RateAmount = 550000 WHERE TripConfigurationId = @configId", ("@configId", configId));
        var regenerated = await (await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, repriceTrips = true, regenerationReason = "AC-45"
        })).DataAsync();
        var newInvoice = regenerated.GetProperty("invoice");
        var newInvoiceId = newInvoice.GetProperty("invoiceId").GetInt64();
        var newSubmitted = await (await SubmitAsync(admin, newInvoiceId, new { rowVersion = newInvoice.GetProperty("rowVersion").GetString() })).DataAsync();

        // INV-00125 (old) nets to 0 — fully offset by its own L12 mirror plus the L13 transfer-out.
        var oldEntries = await LedgerForInvoiceAsync(factory, vehicles.Tenant, oldInvoiceId);
        Assert.Equal(oldEntries.Sum(e => e.DebitAmount), oldEntries.Sum(e => e.CreditAmount));

        // INV-00188 (new): 550,000 debit − 200,000 transferred-in credit = 350,000.
        var newEntries = await LedgerForInvoiceAsync(factory, vehicles.Tenant, newInvoiceId);
        Assert.Equal(350000, newEntries.Sum(e => e.DebitAmount) - newEntries.Sum(e => e.CreditAmount));

        var newReloaded = await (await admin.GetAsync($"/api/invoices/{newInvoiceId}")).DataAsync();
        Assert.Equal(350000, newReloaded.GetProperty("balanceAmount").GetDecimal());

        var customerBalance = factory.Scalar<decimal>("SELECT BalanceAmount FROM trp.CustomerBalances WHERE CustomerId = @id", ("@id", customerId));
        Assert.Equal(350000, customerBalance);
    }

    [Fact]
    public async Task An_applied_advance_transfers_to_the_new_invoice_with_the_payments()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var tripId = await CreateOpenTripAsync(admin, vehicles, customerId, 80000, "2026-07-10");
        var bankAccountId = await ReadyBankAccountAsync(admin);
        (await admin.PostAsJsonAsync($"/api/trips/{tripId}/advances", new
        {
            advanceDate = Day(), amount = 30000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-ADV-TRF"
        })).EnsureSuccessStatusCode();

        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var oldInvoiceId = invoice.GetProperty("invoiceId").GetInt64();
        (await SubmitAsync(admin, oldInvoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = "Advance transfer"
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();
        var transfer = Assert.Single(result.GetProperty("transfers").EnumerateArray());
        Assert.Equal("AdvanceApplication", transfer.GetProperty("sourceKind").GetString());
        Assert.Equal(30000, transfer.GetProperty("amountTransferred").GetDecimal());

        var newInvoice = result.GetProperty("invoice");
        Assert.Equal(30000, newInvoice.GetProperty("transferredInAmount").GetDecimal());
        Assert.Equal(50000, newInvoice.GetProperty("balanceAmount").GetDecimal());   // 80,000 − 30,000 transferred in

        // The advance itself stays Applied — it is not reopened, and Submitting the new invoice must not try to
        // auto-apply it a second time (AdvanceService.ApplyForTripAsync only ever touches Open advances).
        var advanceStatus = factory.Scalar<string>("SELECT Status FROM trp.CustomerAdvances WHERE TripId = @id", ("@id", tripId));
        Assert.Equal("Applied", advanceStatus);
    }

    [Fact]
    public async Task Chained_regeneration_carries_a_transfer_forward_as_its_own_source()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 100000, "Chained Transfer Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var v1 = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var v1Id = v1.GetProperty("invoiceId").GetInt64();
        (await SubmitAsync(admin, v1Id, new { rowVersion = v1.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();

        var bankAccountId = await ReadyBankAccountAsync(admin);
        (await admin.PostAsJsonAsync($"/api/invoices/{v1Id}/payments", new
        {
            receiptDate = Day(), amount = 40000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-CHAIN-1"
        })).EnsureSuccessStatusCode();

        var v2Response = await admin.PostAsJsonAsync($"/api/invoices/{v1Id}/regenerate", new { periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = "v1 to v2" });
        Assert.True(v2Response.IsSuccessStatusCode, await v2Response.Content.ReadAsStringAsync());
        var v2Result = await v2Response.DataAsync();
        var v1ToV2 = Assert.Single(v2Result.GetProperty("transfers").EnumerateArray());
        Assert.Equal("Payment", v1ToV2.GetProperty("sourceKind").GetString());
        var v2 = v2Result.GetProperty("invoice");
        var v2Id = v2.GetProperty("invoiceId").GetInt64();

        var v3Response = await admin.PostAsJsonAsync($"/api/invoices/{v2Id}/regenerate", new { periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = "v2 to v3" });
        Assert.True(v3Response.IsSuccessStatusCode, await v3Response.Content.ReadAsStringAsync());
        var v3Result = await v3Response.DataAsync();
        var v2ToV3 = Assert.Single(v3Result.GetProperty("transfers").EnumerateArray());
        Assert.Equal("PriorTransfer", v2ToV3.GetProperty("sourceKind").GetString());
        Assert.Equal(40000, v2ToV3.GetProperty("amountTransferred").GetDecimal());

        var v3 = v3Result.GetProperty("invoice");
        Assert.Equal(40000, v3.GetProperty("transferredInAmount").GetDecimal());
    }

    [Fact]
    public async Task A_transferred_payment_can_no_longer_be_reversed_directly()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 100000, "Transferred Reversal Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var oldInvoiceId = invoice.GetProperty("invoiceId").GetInt64();
        (await SubmitAsync(admin, oldInvoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();

        var bankAccountId = await ReadyBankAccountAsync(admin);
        var payment = await (await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/payments", new
        {
            receiptDate = Day(), amount = 50000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-REV-TRF"
        })).DataAsync();
        var paymentId = factory.Scalar<long>("SELECT InvoicePaymentId FROM trp.InvoicePayments WHERE InvoiceId = @id", ("@id", oldInvoiceId));

        (await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = "Force transferred status"
        })).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync($"/api/invoice-payments/{paymentId}/reverse", new { reason = "Trying anyway" });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("PAYMENT_TRANSFERRED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GET_payment_transfers_lists_the_transfer_from_both_the_old_and_new_invoice()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 100000, "Transfer List Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var oldInvoiceId = invoice.GetProperty("invoiceId").GetInt64();
        (await SubmitAsync(admin, oldInvoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();

        var bankAccountId = await ReadyBankAccountAsync(admin);
        (await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/payments", new
        {
            receiptDate = Day(), amount = 60000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-LIST-1"
        })).EnsureSuccessStatusCode();

        var regenerated = await (await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = "List check"
        })).DataAsync();
        var newInvoiceId = regenerated.GetProperty("invoice").GetProperty("invoiceId").GetInt64();

        var fromOld = await (await admin.GetAsync($"/api/invoices/{oldInvoiceId}/payment-transfers")).DataAsync();
        var fromNew = await (await admin.GetAsync($"/api/invoices/{newInvoiceId}/payment-transfers")).DataAsync();
        Assert.Single(fromOld.EnumerateArray());
        Assert.Single(fromNew.EnumerateArray());
        Assert.Equal(60000, fromOld.EnumerateArray().First().GetProperty("amountTransferred").GetDecimal());
    }
}
