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

/// <summary>CC-34: advances on Open trips (§37.4, §40A L8-L10/L15, AC-59, AC-60).</summary>
[Collection(ApiCollection.Name)]
public sealed class CustomerAdvanceTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT,
             PermissionCodes.TRP_PAYMENT_ADVANCE, PermissionCodes.TRP_BANKACCOUNT_MANAGE]);
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

    private static async Task<long> CreateOpenTripAsync(HttpClient admin, VehicleWorld vehicles, int customerId, decimal tripAmount)
    {
        var cities = await CityIdsByAbbrAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var response = await admin.PostAsJsonAsync("/api/trips/open", new
        {
            customerId, from = new { locationType = "City", cityId = cities["LHR"] }, to = new { locationType = "City", cityId = cities["DGK"] },
            vehicleId = truck, driverId, tripAmount, tripDate = "2026-09-01"
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.DataAsync()).GetProperty("tripId").GetInt64();
    }

    private static async Task CompleteAsync(HttpClient admin, long tripId)
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
            accountTitle = "Acme Trading Co.", bankName = "Habib Bank Limited", branchName = "Gulberg", accountNumberLast4 = "4521"
        })).DataAsync();
        return account.GetProperty("bankCashAccountId").GetInt64();
    }

    private static async Task<System.Collections.Generic.IReadOnlyList<VMS.Modules.Trips.Models.LedgerEntryModel>> LedgerForTripAsync(ApiFactory factory, Guid tenantId, long tripId)
    {
        factory.CreateClient();
        System.Collections.Generic.IReadOnlyList<VMS.Modules.Trips.Models.LedgerEntryModel> entries = [];
        await BackgroundTenantScope.RunAsAsync(tenantId, async () =>
        {
            using var scope = factory.Services.CreateScope();
            entries = await scope.ServiceProvider.GetRequiredService<ICustomerLedgerPostingService>().ListForTripAsync(tripId);
        });
        return entries;
    }

    private static async Task<System.Collections.Generic.IReadOnlyList<VMS.Modules.Trips.Models.LedgerEntryModel>> LedgerForInvoiceAsync(ApiFactory factory, Guid tenantId, long invoiceId)
    {
        factory.CreateClient();
        System.Collections.Generic.IReadOnlyList<VMS.Modules.Trips.Models.LedgerEntryModel> entries = [];
        await BackgroundTenantScope.RunAsAsync(tenantId, async () =>
        {
            using var scope = factory.Services.CreateScope();
            entries = await scope.ServiceProvider.GetRequiredService<ICustomerLedgerPostingService>().ListForInvoiceAsync(invoiceId);
        });
        return entries;
    }

    [Fact]
    public async Task AC_59_and_the_40A_advance_example()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var tripId = await CreateOpenTripAsync(admin, vehicles, customerId, 80000);
        var bankAccountId = await ReadyBankAccountAsync(admin);

        var advanceResponse = await admin.PostAsJsonAsync($"/api/trips/{tripId}/advances", new
        {
            advanceDate = Day(), amount = 30000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-ADV-1"
        });
        Assert.True(advanceResponse.IsSuccessStatusCode, await advanceResponse.Content.ReadAsStringAsync());
        var advance = await advanceResponse.DataAsync();
        Assert.StartsWith("ADV-", advance.GetProperty("advanceNumber").GetString());
        Assert.Equal("Open", advance.GetProperty("status").GetString());

        var beforeSubmit = await LedgerForTripAsync(factory, vehicles.Tenant, tripId);
        var advanceEntry = Assert.Single(beforeSubmit, e => e.EntryType == "ADVANCE");
        Assert.Equal(30000, advanceEntry.CreditAmount);
        Assert.Equal(0, advanceEntry.DebitAmount);
        Assert.Null(advanceEntry.InvoiceId);
        Assert.Equal(tripId, advanceEntry.TripId);

        await CompleteAsync(admin, tripId);
        var invoiceResponse = await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } });
        Assert.True(invoiceResponse.IsSuccessStatusCode, await invoiceResponse.Content.ReadAsStringAsync());
        var invoice = await invoiceResponse.DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();

        var submitResponse = await SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() });
        Assert.True(submitResponse.IsSuccessStatusCode, await submitResponse.Content.ReadAsStringAsync());
        var submitted = await submitResponse.DataAsync();
        // §40A's own worked example: INVOICE Dr 80,000, then ADVANCE_APPLY_OUT Dr 30,000 / ADVANCE_APPLY_IN Cr
        // 30,000 — invoice balance falls to 50,000.
        Assert.Equal(50000, submitted.GetProperty("balanceAmount").GetDecimal());

        var afterSubmit = await LedgerForTripAsync(factory, vehicles.Tenant, tripId);
        var applyOut = Assert.Single(afterSubmit, e => e.EntryType == "ADVANCE_APPLY_OUT");
        Assert.Equal(30000, applyOut.DebitAmount);
        Assert.Null(applyOut.InvoiceId);

        var invoiceEntries = await LedgerForInvoiceAsync(factory, vehicles.Tenant, invoiceId);
        Assert.Single(invoiceEntries, e => e.EntryType == "ADVANCE_APPLY_IN" && e.CreditAmount == 30000);

        var advances = await (await admin.GetAsync($"/api/customer-advances?customerId={customerId}")).DataAsync();
        var updatedAdvance = advances.EnumerateArray().Single();
        Assert.Equal("Applied", updatedAdvance.GetProperty("status").GetString());
        Assert.Equal(30000, updatedAdvance.GetProperty("appliedAmount").GetDecimal());
    }

    [Fact]
    public async Task AC_60_an_advance_on_a_fixed_trip_is_rejected()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var cities = await CityIdsByAbbrAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var route = await (await admin.PostAsJsonAsync("/api/routes", new
        {
            routeName = "Advance Fixed Route", stops = new[] { new { cityId = cities["LHR"], stopType = "Origin" }, new { cityId = cities["FSD"], stopType = "Destination" } }
        })).DataAsync();
        var config = await (await admin.PostAsJsonAsync("/api/trip-configurations",
            new { customerId, name = "Advance Fixed Config", routeId = route.GetProperty("routeId").GetInt32(), directionType = "OneWay" })).DataAsync();
        var configId = config.GetProperty("tripConfigurationId").GetInt64();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId = truck, effectiveFrom = "2020-01-01" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/activate", new { rowVersion = config.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount = 25000m })).EnsureSuccessStatusCode();
        var trip = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId = truck, driverId, tripDate = "2026-07-10" })).DataAsync();
        var bankAccountId = await ReadyBankAccountAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/trips/{trip.GetProperty("tripId").GetInt64()}/advances", new
        {
            advanceDate = Day(), amount = 5000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-ADV-2"
        });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Advances can only be recorded against Open trips.", body);
    }

    [Fact]
    public async Task An_uncapped_advance_leaves_the_invoice_with_a_credit()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var tripId = await CreateOpenTripAsync(admin, vehicles, customerId, 20000);
        var bankAccountId = await ReadyBankAccountAsync(admin);

        (await admin.PostAsJsonAsync($"/api/trips/{tripId}/advances", new
        {
            advanceDate = Day(), amount = 30000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-ADV-3"
        })).EnsureSuccessStatusCode();

        await CompleteAsync(admin, tripId);
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var submitted = await (await SubmitAsync(admin, invoice.GetProperty("invoiceId").GetInt64(), new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).DataAsync();
        Assert.Equal(-10000, submitted.GetProperty("balanceAmount").GetDecimal());
        Assert.Equal("Paid", submitted.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task Moving_an_advance_to_another_open_trip_needs_a_reason()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var tripId = await CreateOpenTripAsync(admin, vehicles, customerId, 80000);
        var otherTripId = await CreateOpenTripAsync(admin, vehicles, customerId, 40000);
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var advance = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/advances", new
        {
            advanceDate = Day(), amount = 10000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-ADV-4"
        })).DataAsync();
        var advanceId = advance.GetProperty("customerAdvanceId").GetInt64();

        var missingReason = await admin.PostAsJsonAsync($"/api/customer-advances/{advanceId}/move", new { toTripId = otherTripId, reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        var moved = await admin.PostAsJsonAsync($"/api/customer-advances/{advanceId}/move", new { toTripId = otherTripId, reason = "Original trip cancelled" });
        Assert.True(moved.IsSuccessStatusCode, await moved.Content.ReadAsStringAsync());
        Assert.Equal(otherTripId, (await moved.DataAsync()).GetProperty("tripId").GetInt64());
    }

    [Fact]
    public async Task Refunding_an_applied_advance_is_refused_but_an_open_one_can_be_refunded()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var tripId = await CreateOpenTripAsync(admin, vehicles, customerId, 80000);
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var advance = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/advances", new
        {
            advanceDate = Day(), amount = 10000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-ADV-5"
        })).DataAsync();
        var advanceId = advance.GetProperty("customerAdvanceId").GetInt64();

        var refund = await admin.PostAsJsonAsync($"/api/customer-advances/{advanceId}/refund", new { reason = "Trip will not proceed" });
        Assert.True(refund.IsSuccessStatusCode, await refund.Content.ReadAsStringAsync());
        var model = await refund.DataAsync();
        Assert.Equal("Refunded", model.GetProperty("status").GetString());
        Assert.Equal(10000, model.GetProperty("refundedAmount").GetDecimal());

        var again = await admin.PostAsJsonAsync($"/api/customer-advances/{advanceId}/refund", new { reason = "Second attempt" });
        Assert.Equal((HttpStatusCode)422, again.StatusCode);
        Assert.Contains("ADVANCE_NOT_OPEN", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reversing_an_open_advance_succeeds()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var tripId = await CreateOpenTripAsync(admin, vehicles, customerId, 80000);
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var advance = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/advances", new
        {
            advanceDate = Day(), amount = 10000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-ADV-6"
        })).DataAsync();
        var advanceId = advance.GetProperty("customerAdvanceId").GetInt64();

        var reversal = await admin.PostAsJsonAsync($"/api/customer-advances/{advanceId}/reverse", new { reason = "Cheque bounced" });
        Assert.True(reversal.IsSuccessStatusCode, await reversal.Content.ReadAsStringAsync());
        Assert.Equal("Reversed", (await reversal.DataAsync()).GetProperty("status").GetString());

        var entries = await LedgerForTripAsync(factory, vehicles.Tenant, tripId);
        Assert.Single(entries, e => e.EntryType == "ADVANCE_REVERSAL" && e.DebitAmount == 10000);
    }

    [Fact]
    public async Task Recording_an_advance_needs_its_own_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var tripId = await CreateOpenTripAsync(admin, vehicles, customerId, 80000);
        var bankAccountId = await ReadyBankAccountAsync(admin);
        var noPermission = vehicles.As("NoAdvance", 91, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_VIEW]);

        var response = await noPermission.PostAsJsonAsync($"/api/trips/{tripId}/advances", new
        {
            advanceDate = Day(), amount = 5000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-ADV-7"
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
