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

/// <summary>CC-30: Customer Ledger core and submit postings (§40A, AC-37, AC-38, AC-47, AC-58, 40A's own
/// acceptance block). Only L1-L3 (submission) and L11 (cancellation mirror) are reachable in this task — every
/// other posting rule (payments, advances, regeneration, carry-forward/refund, opening balance) is CC-31..40's
/// own job.</summary>
[Collection(ApiCollection.Name)]
public sealed class CustomerLedgerTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT, PermissionCodes.TRP_INVOICE_CANCEL]);
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

        // Evidence Required defaults to true (§13) — off here so submit isn't blocked by an unrelated gate.
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

    /// <summary>Resolves the module-internal posting service directly via DI — there is no read API yet
    /// (CC-38's own job); the same "resolve a public interface of an internal service directly" idiom
    /// InvoiceEvidenceTests.cs already uses for the background job.</summary>
    private static async Task<System.Collections.Generic.IReadOnlyList<VMS.Modules.Trips.Models.LedgerEntryModel>> LedgerEntriesAsync(ApiFactory factory, Guid tenantId, long invoiceId)
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
    public async Task AC_37_and_38_submission_posts_L1_to_L3_dated_the_submission_date_and_nothing_posts_before()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 500000, "Ledger Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules", new
        {
            taxName = "Withholding Tax", taxCode = "WHT-236", taxType = "Percentage", taxPercentage = 2.0, applicable = true, calculationBasis = "InvoiceSubtotal"
        })).EnsureSuccessStatusCode();

        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new
        {
            customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId },
            adjustments = new[] { new { adjustmentMonth = "August 2026", amount = 20000, note = "Rate difference for August trips" } }
        })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();

        // AC-38: no ledger entries before submit.
        Assert.Empty(await LedgerEntriesAsync(factory, vehicles.Tenant, invoiceId));

        var submitResponse = await SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() });
        Assert.True(submitResponse.IsSuccessStatusCode, await submitResponse.Content.ReadAsStringAsync());
        var submitted = await submitResponse.DataAsync();
        var submittedOn = DateOnly.Parse(submitted.GetProperty("submittedOn").GetString()!);

        var entries = await LedgerEntriesAsync(factory, vehicles.Tenant, invoiceId);
        Assert.Equal(3, entries.Count);

        var invoiceEntry = Assert.Single(entries, e => e.EntryType == "INVOICE");
        Assert.Equal(500000, invoiceEntry.DebitAmount);
        Assert.Equal(0, invoiceEntry.CreditAmount);
        Assert.Equal(submittedOn, invoiceEntry.EntryDate);
        // §40A.2: "Invoice entries include the Invoice Date" — the narration names both the invoice number
        // and its date, formatted the same way the FSD's own worked example shows ("dated 31-Aug-2026").
        var invoiceDate = DateOnly.Parse(invoice.GetProperty("invoiceDate").GetString()!);
        Assert.Contains(invoice.GetProperty("invoiceNumber").GetString()!, invoiceEntry.Narration);
        Assert.Contains(invoiceDate.ToString("dd-MMM-yyyy"), invoiceEntry.Narration);

        var adjustmentEntry = Assert.Single(entries, e => e.EntryType == "ADJUSTMENT");
        Assert.Equal(20000, adjustmentEntry.DebitAmount);   // positive adjustment ⇒ Debit
        Assert.Equal(submittedOn, adjustmentEntry.EntryDate);

        var deductionEntry = Assert.Single(entries, e => e.EntryType == "DEDUCTION");
        Assert.Equal(0, deductionEntry.DebitAmount);
        Assert.True(deductionEntry.CreditAmount > 0);   // 2% of gross ⇒ Credit
        Assert.Equal(submittedOn, deductionEntry.EntryDate);

        // Every entry carries its own distinct entry number and monotonic per-customer sequence.
        Assert.Equal(3, entries.Select(e => e.EntryNumber).Distinct().Count());
        Assert.All(entries, e => Assert.StartsWith("LED-", e.EntryNumber));
    }

    [Fact]
    public async Task AC_58_a_negative_adjustment_posts_as_a_credit()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Negative Adjustment Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");

        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new
        {
            customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId },
            adjustments = new[] { new { adjustmentMonth = "July 2026", amount = -15000, note = "Overbilling correction" } }
        })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();

        (await SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();

        var adjustmentEntry = Assert.Single(await LedgerEntriesAsync(factory, vehicles.Tenant, invoiceId), e => e.EntryType == "ADJUSTMENT");
        Assert.Equal(0, adjustmentEntry.DebitAmount);
        Assert.Equal(15000, adjustmentEntry.CreditAmount);
    }

    [Fact]
    public async Task Forty_a_double_submit_posts_only_one_set_of_entries()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Double Submit Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var rowVersion = invoice.GetProperty("rowVersion").GetString();

        var results = await Task.WhenAll(SubmitAsync(admin, invoiceId, new { rowVersion }), SubmitAsync(admin, invoiceId, new { rowVersion }));
        Assert.Single(results, r => r.IsSuccessStatusCode);

        var entries = await LedgerEntriesAsync(factory, vehicles.Tenant, invoiceId);
        // Only one INVOICE entry exists — the losing request never got far enough to post a second time, and
        // even if it had, the unique (SourceType, SourceId, EntryType) index would have refused the duplicate.
        Assert.Single(entries, e => e.EntryType == "INVOICE");
    }

    [Fact]
    public async Task L11_cancelling_a_submitted_invoice_posts_a_mirror_of_each_entry()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 500000, "Cancel Mirror Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new
        {
            customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId },
            adjustments = new[] { new { adjustmentMonth = "August 2026", amount = 20000, note = "Rate difference" } }
        })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var submitted = await (await SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).DataAsync();

        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/cancel", new { rowVersion = submitted.GetProperty("rowVersion").GetString(), reason = "Test cancel" })).EnsureSuccessStatusCode();

        var entries = await LedgerEntriesAsync(factory, vehicles.Tenant, invoiceId);
        // L1 INVOICE + L2 ADJUSTMENT (no deduction here) each get their own INVOICE_CANCEL mirror.
        Assert.Equal(4, entries.Count);
        var mirrors = entries.Where(e => e.EntryType == "INVOICE_CANCEL").ToList();
        Assert.Equal(2, mirrors.Count);

        var invoiceMirror = mirrors.Single(m => m.ReversesEntryId == entries.Single(e => e.EntryType == "INVOICE").CustomerLedgerEntryId);
        Assert.Equal(500000, invoiceMirror.CreditAmount);   // opposite of the original Debit 500,000
        Assert.Equal(0, invoiceMirror.DebitAmount);

        var adjustmentMirror = mirrors.Single(m => m.ReversesEntryId == entries.Single(e => e.EntryType == "ADJUSTMENT").CustomerLedgerEntryId);
        Assert.Equal(20000, adjustmentMirror.CreditAmount);
    }

    [Fact]
    public async Task Cancelling_a_never_submitted_invoice_posts_no_mirror()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Never Submitted Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();

        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/cancel", new { rowVersion = invoice.GetProperty("rowVersion").GetString(), reason = "Wrong customer" })).EnsureSuccessStatusCode();

        Assert.Empty(await LedgerEntriesAsync(factory, vehicles.Tenant, invoiceId));
    }

    [Fact]
    public async Task AC_47_ledger_entries_are_immutable()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Immutable Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        (await SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();
        var entryId = (await LedgerEntriesAsync(factory, vehicles.Tenant, invoiceId))[0].CustomerLedgerEntryId;

        var ex = Assert.Throws<Microsoft.Data.SqlClient.SqlException>(() =>
            factory.Execute("UPDATE trp.CustomerLedgerEntries SET Narration = 'Tampered' WHERE CustomerLedgerEntryId = @id", ("@id", entryId)));
        Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
