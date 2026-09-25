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

/// <summary>CC-29: invoice submit and cancel, no approval step (§36, §47.3, AC-35, AC-55). Ledger posting/reversal
/// (§40A) is deliberately not exercised here — CustomerLedgerEntry doesn't exist yet (CC-30's own job).</summary>
[Collection(ApiCollection.Name)]
public sealed class InvoiceSubmissionTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT, PermissionCodes.TRP_INVOICE_CANCEL,
             PermissionCodes.TRP_INCOME_EDIT]);
        return (vehicles, admin);
    }

    private static async Task<int> ReadyCustomerAsync(HttpClient admin, bool evidenceRequired = false)
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

        // Evidence Required defaults to true (§13) — most of this task's own tests are about submit/cancel
        // itself, not the evidence gate, so they turn it off; the evidence-gate tests turn it back on explicitly.
        // No prior GET here: that would lazily create today's default row, and a same-day PUT right after
        // would then collide with its own "effective from must be after the current version" rule.
        (await admin.PutAsJsonAsync($"/api/customers/{customerId}/billing-configuration", new { evidenceRequired })).EnsureSuccessStatusCode();
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

    /// <summary>The same direct-DI job trigger InvoiceEvidenceTests.cs uses — the background job's own interval
    /// is far too slow to wait on in a test.</summary>
    private static async Task ProcessQueuedEvidenceAsync(ApiFactory factory, Guid tenantId)
    {
        factory.CreateClient();
        await BackgroundTenantScope.RunAsAsync(tenantId, async () =>
        {
            using var scope = factory.Services.CreateScope();
            using var acting = scope.ServiceProvider.GetRequiredService<IAuditContext>().ActAsSystem("Test-triggered evidence job");
            await scope.ServiceProvider.GetRequiredService<IInvoiceEvidenceGenerationService>().ProcessQueuedAsync();
        });
    }

    /// <summary>Submit carries the new <c>[Idempotent]</c> gate (§47.3: "If-Match + Idempotency-Key") — every
    /// call needs the header; a fresh key per call keeps each one a genuinely new attempt, not a replay.</summary>
    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, long invoiceId, object body, string? idempotencyKey = null) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/invoices/{invoiceId}/submit")
        {
            Content = JsonContent.Create(body),
            Headers = { { "Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString() } }
        });

    private static async Task<System.Text.Json.JsonElement> ReadyInvoiceAsync(VehicleWorld vehicles, HttpClient admin, bool evidenceRequired = false)
    {
        var customerId = await ReadyCustomerAsync(admin, evidenceRequired);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, $"Submit Route {Guid.NewGuid():N}"[..22]);
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        return await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
    }

    [Fact]
    public async Task AC_55_a_generated_invoice_is_submitted_directly_with_no_approval_step()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var invoice = await ReadyInvoiceAsync(vehicles, admin);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();

        var response = await SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString(), submissionChannel = "Email" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var submitted = await response.DataAsync();
        Assert.Equal("Submitted", submitted.GetProperty("status").GetString());
        Assert.Equal("Email", submitted.GetProperty("submissionChannel").GetString());
        Assert.False(string.IsNullOrWhiteSpace(submitted.GetProperty("submittedOn").GetString()));

        // AC-35: status and payment status are separate fields — Submitted here says nothing about payment yet.
        Assert.Equal("Unpaid", submitted.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task Submitting_a_non_generated_invoice_is_refused()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var invoice = await ReadyInvoiceAsync(vehicles, admin);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var first = await (await SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).DataAsync();

        // Already Submitted — a second submit is refused.
        var again = await SubmitAsync(admin, invoiceId, new { rowVersion = first.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode)422, again.StatusCode);
        Assert.Contains("INVALID_STATUS", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Submit_is_refused_until_evidence_is_generated_when_the_customer_requires_it()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var invoice = await ReadyInvoiceAsync(vehicles, admin, evidenceRequired: true);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();

        var early = await SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode)422, early.StatusCode);
        Assert.Contains("EVIDENCE_NOT_READY", await early.Content.ReadAsStringAsync());

        await ProcessQueuedEvidenceAsync(factory, vehicles.Tenant);
        var ready = await SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() });
        Assert.True(ready.IsSuccessStatusCode, await ready.Content.ReadAsStringAsync());
        Assert.Equal("Submitted", (await ready.DataAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_replayed_submit_with_the_same_idempotency_key_returns_the_first_response_unrun()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var invoice = await ReadyInvoiceAsync(vehicles, admin);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var key = Guid.NewGuid().ToString();
        Task<HttpResponseMessage> Send() => SubmitAsync(admin, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() }, key);

        var first = await Send();
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var firstInvoice = (await first.DataAsync()).GetProperty("invoiceId").GetInt64();

        var second = await Send();
        Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
        var secondInvoice = (await second.DataAsync()).GetProperty("invoiceId").GetInt64();
        // Same data both times — the second call never ran SubmitAsync again (a genuine re-run would either
        // 500 on the already-consumed rowVersion or 422 INVALID_STATUS, neither of which happened here).
        Assert.Equal(firstInvoice, secondInvoice);

        var reloaded = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();
        Assert.Equal("Submitted", reloaded.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancelling_releases_the_trips_so_they_can_be_invoiced_again()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Cancel Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();

        var cancel = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/cancel", new { rowVersion = invoice.GetProperty("rowVersion").GetString(), reason = "Wrong customer selected" });
        Assert.True(cancel.IsSuccessStatusCode, await cancel.Content.ReadAsStringAsync());
        var cancelled = await cancel.DataAsync();
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.False(cancelled.GetProperty("isActive").GetBoolean());
        Assert.Equal("Wrong customer selected", cancelled.GetProperty("cancelReason").GetString());

        // §32.1: the trip's own billing lock is released — the same period can now be invoiced again.
        var reinvoice = await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } });
        Assert.True(reinvoice.IsSuccessStatusCode, await reinvoice.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cancelling_releases_a_billed_incomes_lock_too()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Income Release Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");

        var incomeTypes = await (await admin.GetAsync("/api/lookups/TRIP_INCOME_TYPE")).DataAsync();
        var detentionId = incomeTypes.EnumerateArray().First(t => t.GetProperty("code").GetString() == "DETENTION").GetProperty("id").GetInt32();
        var income = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/income", new { incomeTypeId = detentionId, amount = 2000, isBillable = true })).DataAsync();
        var incomeId = income.GetProperty("tripIncomeId").GetInt64();

        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();

        // Locked while billed (CC-21/CC-25's own guard).
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/trip-income/{incomeId}/void", new { reason = "Trying anyway" })).StatusCode);

        (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/cancel", new { rowVersion = invoice.GetProperty("rowVersion").GetString(), reason = "Test cancel" })).EnsureSuccessStatusCode();

        // Unlocked now that its invoice is cancelled.
        var voided = await admin.PostAsJsonAsync($"/api/trip-income/{incomeId}/void", new { reason = "Now allowed" });
        Assert.True(voided.IsSuccessStatusCode, await voided.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cancelling_needs_a_reason()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var invoice = await ReadyInvoiceAsync(vehicles, admin);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var response = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/cancel", new { rowVersion = invoice.GetProperty("rowVersion").GetString(), reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cancelling_an_already_cancelled_invoice_is_refused()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var invoice = await ReadyInvoiceAsync(vehicles, admin);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var first = await (await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/cancel", new { rowVersion = invoice.GetProperty("rowVersion").GetString(), reason = "First cancel" })).DataAsync();

        var again = await admin.PostAsJsonAsync($"/api/invoices/{invoiceId}/cancel", new { rowVersion = first.GetProperty("rowVersion").GetString(), reason = "Second cancel" });
        Assert.Equal((HttpStatusCode)422, again.StatusCode);
        Assert.Contains("INVALID_STATUS", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Submit_and_cancel_each_need_their_own_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var invoice = await ReadyInvoiceAsync(vehicles, admin);
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var viewOnly = vehicles.As("ViewOnly", 55, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_VIEW]);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await SubmitAsync(viewOnly, invoiceId, new { rowVersion = invoice.GetProperty("rowVersion").GetString() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewOnly.PostAsJsonAsync($"/api/invoices/{invoiceId}/cancel", new { rowVersion = invoice.GetProperty("rowVersion").GetString(), reason = "No permission" })).StatusCode);
    }
}
