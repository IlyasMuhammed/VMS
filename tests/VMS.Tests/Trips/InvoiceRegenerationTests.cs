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

/// <summary>CC-35: invoice regeneration and overlap replacement (§38, §39, AC-41, AC-44). Payment transfer
/// (§40, L13) is CC-36's own job — not exercised here.</summary>
[Collection(ApiCollection.Name)]
public sealed class InvoiceRegenerationTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT,
             PermissionCodes.TRP_INVOICE_REGENERATE, PermissionCodes.TRP_INVOICE_CANCEL, PermissionCodes.TRP_PAYMENT_CREATE, PermissionCodes.TRP_BANKACCOUNT_MANAGE]);
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
    public async Task AC_44_regenerating_with_a_wider_period_sets_the_old_invoice_inactive_and_links_both()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Regen Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var oldInvoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var oldInvoiceId = oldInvoice.GetProperty("invoiceId").GetInt64();

        var response = await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(45), tripIds = new[] { tripId }, regenerationReason = "Extended billing period"
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();
        Assert.Equal(oldInvoiceId, result.GetProperty("previousInvoiceId").GetInt64());
        var newInvoice = result.GetProperty("invoice");
        Assert.Equal(2, newInvoice.GetProperty("version").GetInt32());
        Assert.NotEqual(oldInvoice.GetProperty("invoiceNumber").GetString(), newInvoice.GetProperty("invoiceNumber").GetString());

        var oldReloaded = await (await admin.GetAsync($"/api/invoices/{oldInvoiceId}")).DataAsync();
        Assert.Equal("Inactive", oldReloaded.GetProperty("status").GetString());
        Assert.False(oldReloaded.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task AC_41_a_fully_paid_invoice_cannot_be_regenerated()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Fully Paid Regen Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var submitted = await (await SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).DataAsync();

        var bankAccountId = (await (await admin.PostAsJsonAsync("/api/bank-accounts", new
        {
            accountTitle = "Acme Trading Co.", bankName = "HBL", branchName = "Gulberg", accountNumberLast4 = "1234"
        })).DataAsync()).GetProperty("bankCashAccountId").GetInt64();
        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            receiptDate = Day(), amount = 25000, paymentMethod = "DirectToAccount", bankCashAccountId = bankAccountId, instrumentNo = "TXN-REGEN-1"
        })).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = "Trying anyway"
        });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("INVOICE_FULLY_PAID", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_trip_left_out_of_the_new_selection_is_released_with_a_warning()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Release Route");
        var tripId1 = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var tripId2 = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-11");
        var oldInvoice = await (await admin.PostAsJsonAsync("/api/invoices",
            new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId1, tripId2 } })).DataAsync();
        var oldInvoiceId = oldInvoice.GetProperty("invoiceId").GetInt64();

        var response = await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId1 }, regenerationReason = "Trip 2 disputed, removed"
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();
        Assert.Single(result.GetProperty("releasedTripNumbers").EnumerateArray());
        Assert.Contains(result.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("uninvoiced"));

        // The released trip is no longer linked to any active invoice and can be picked up by a brand-new one —
        // using a non-overlapping period, since the regenerated invoice (INV-…-00002) is itself still active for
        // Day(-30)..Day(30) and the overlap check (§38/39) is period-based per customer, not trip-based.
        var reinvoice = await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(31), periodTo = Day(60), tripIds = new[] { tripId2 } });
        Assert.True(reinvoice.IsSuccessStatusCode, await reinvoice.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Regeneration_keeps_the_version_chain_and_root_invoice_id()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Chain Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var v1 = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var v1Id = v1.GetProperty("invoiceId").GetInt64();

        var v2Response = await admin.PostAsJsonAsync($"/api/invoices/{v1Id}/regenerate", new { periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = "First correction" });
        Assert.True(v2Response.IsSuccessStatusCode, await v2Response.Content.ReadAsStringAsync());
        var v2 = (await v2Response.DataAsync()).GetProperty("invoice");
        var v2Id = v2.GetProperty("invoiceId").GetInt64();

        var v3Response = await admin.PostAsJsonAsync($"/api/invoices/{v2Id}/regenerate", new { periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = "Second correction" });
        Assert.True(v3Response.IsSuccessStatusCode, await v3Response.Content.ReadAsStringAsync());
        var v3 = (await v3Response.DataAsync()).GetProperty("invoice");

        Assert.Equal(2, v2.GetProperty("version").GetInt32());
        Assert.Equal(3, v3.GetProperty("version").GetInt32());
        // The middle version (v2) is now itself Inactive, replaced by v3.
        var v2Reloaded = await (await admin.GetAsync($"/api/invoices/{v2Id}")).DataAsync();
        Assert.Equal("Inactive", v2Reloaded.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Regenerating_a_submitted_invoice_mirrors_its_ledger_entries()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 500000, "Mirror Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var submitted = await (await SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).DataAsync();

        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = "Correction after submission"
        })).EnsureSuccessStatusCode();

        var entries = await LedgerForInvoiceAsync(factory, vehicles.Tenant, invoiceId);
        var mirror = Assert.Single(entries, e => e.EntryType == "INVOICE_SUPERSEDED");
        Assert.Equal(500000, mirror.CreditAmount);   // opposite of the original L1 Debit 500,000
        Assert.Equal(0, mirror.DebitAmount);
    }

    [Fact]
    public async Task Regenerating_a_never_submitted_invoice_posts_no_ledger_mirror()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "No Mirror Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();

        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = "Never submitted, just editing"
        })).EnsureSuccessStatusCode();

        Assert.Empty(await LedgerForInvoiceAsync(factory, vehicles.Tenant, invoiceId));
    }

    [Fact]
    public async Task Historical_integrity_the_old_invoice_line_keeps_its_own_rate_after_a_repriced_regeneration()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Reprice Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var oldInvoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var oldInvoiceId = oldInvoice.GetProperty("invoiceId").GetInt64();
        Assert.Equal(25000, oldInvoice.GetProperty("totalTripAmount").GetDecimal());

        // §38's own "historical integrity example": the rate is corrected after the fact.
        factory.Execute("UPDATE trp.TripRates SET RateAmount = 30000 WHERE TripConfigurationId = @configId", ("@configId", configId));

        var response = await admin.PostAsJsonAsync($"/api/invoices/{oldInvoiceId}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, repriceTrips = true, regenerationReason = "Rate correction"
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var newInvoice = (await response.DataAsync()).GetProperty("invoice");
        Assert.Equal(30000, newInvoice.GetProperty("totalTripAmount").GetDecimal());

        // The OLD invoice's own line is append-only and keeps showing the rate that was actually in force then.
        var oldReloaded = await (await admin.GetAsync($"/api/invoices/{oldInvoiceId}")).DataAsync();
        Assert.Equal(25000, oldReloaded.GetProperty("totalTripAmount").GetDecimal());
    }

    [Fact]
    public async Task Regeneration_needs_a_reason()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Reason Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoice.GetProperty("invoiceId").GetInt64()}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Regenerating_a_cancelled_invoice_is_refused()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Cancelled Regen Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/cancel", new { rowVersion = invoice.GetProperty("rowVersion").GetString(), reason = "Wrong customer" })).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = "Trying anyway"
        });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("INVALID_STATUS", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Regenerating_with_a_genuinely_separate_overlap_is_still_refused()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Separate Overlap Route");
        var tripId1 = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var oldInvoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(-1), tripIds = new[] { tripId1 } })).DataAsync();

        var tripId2 = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-11");
        (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(10), periodTo = Day(60), tripIds = new[] { tripId2 } })).EnsureSuccessStatusCode();

        // Regenerating the FIRST invoice into a period that now overlaps the SECOND, unrelated, active invoice.
        var response = await admin.PostAsJsonAsync($"/api/invoices/{oldInvoice.GetProperty("invoiceId").GetInt64()}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId1 }, regenerationReason = "Widening period"
        });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("OVERLAP_DETECTED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Regenerating_needs_its_own_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Permission Regen Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var noPermission = vehicles.As("NoRegen", 87, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_VIEW]);

        var response = await noPermission.PostAsJsonAsync($"/api/invoices/{invoice.GetProperty("invoiceId").GetInt64()}/regenerate", new
        {
            periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId }, regenerationReason = "No permission"
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
