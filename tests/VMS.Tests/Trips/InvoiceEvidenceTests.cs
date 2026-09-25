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

/// <summary>CC-28: invoice evidence, standard layout, vehicle pagination (§41, AC-33, AC-67). AC-34 (a
/// regenerated invoice's own old evidence staying unchanged) genuinely needs invoice regeneration, which is
/// CC-35's own job — not testable here; the underlying "snapshots only" mechanism this task relies on is instead
/// proven by determinism (re-deriving the same metadata twice from the same immutable InvoiceLine rows).</summary>
[Collection(ApiCollection.Name)]
public sealed class InvoiceEvidenceTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW,
             PermissionCodes.TRP_INVOICE_EVIDENCE_RETRY, PermissionCodes.TRP_INVOICE_EVIDENCE_RERENDER]);
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

    /// <summary>Runs the background job's own entry point directly, the same tenant-scoped-runner idiom
    /// <c>InvoiceEvidenceHostedService</c> uses in production — necessary since its own timer (2-minute
    /// interval, 10-second startup delay) is far too slow for a test to wait on.</summary>
    private static async Task<int> ProcessQueuedEvidenceAsync(ApiFactory factory, Guid tenantId)
    {
        factory.CreateClient();
        var processed = 0;
        await BackgroundTenantScope.RunAsAsync(tenantId, async () =>
        {
            using var scope = factory.Services.CreateScope();
            using var acting = scope.ServiceProvider.GetRequiredService<IAuditContext>().ActAsSystem("Test-triggered evidence job");
            processed = await scope.ServiceProvider.GetRequiredService<IInvoiceEvidenceGenerationService>().ProcessQueuedAsync();
        });
        return processed;
    }

    [Fact]
    public async Task Creating_an_invoice_queues_evidence_and_the_background_job_generates_it()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Evidence Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();

        var queued = await (await admin.GetAsync($"/api/invoices/{invoiceId}/evidence")).DataAsync();
        Assert.Equal(1, queued.GetArrayLength());
        Assert.Equal("Queued", queued[0].GetProperty("status").GetString());
        Assert.Equal(1, queued[0].GetProperty("evidenceVersion").GetInt32());

        Assert.Equal(1, await ProcessQueuedEvidenceAsync(factory, vehicles.Tenant));

        var generated = await (await admin.GetAsync($"/api/invoices/{invoiceId}/evidence")).DataAsync();
        var row = generated[0];
        Assert.Equal("Generated", row.GetProperty("status").GetString());
        Assert.Equal(1, row.GetProperty("pageCount").GetInt32());
        Assert.Equal(1, row.GetProperty("lineCount").GetInt32());
        var vehiclePages = row.GetProperty("vehiclePages").EnumerateArray().ToList();
        Assert.Single(vehiclePages);
        Assert.Equal(1, vehiclePages[0].GetProperty("firstPage").GetInt32());
        Assert.Equal(1, vehiclePages[0].GetProperty("lastPage").GetInt32());
    }

    [Fact]
    public async Task AC_33_a_vehicle_with_more_lines_than_page_size_continues_under_its_own_header()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truckA = VehicleWorld.Id(await vehicles.ActiveAsync());
        var truckB = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configA = await ReadyConfigAsync(admin, customerId, truckA, 10000, "Pagination Route A");
        var configB = await ReadyConfigAsync(admin, customerId, truckB, 12000, "Pagination Route B");

        var tripIds = new List<long>
        {
            await CompleteTripAsync(admin, customerId, configA, truckA, driverId, "2026-07-10"),
            await CompleteTripAsync(admin, customerId, configA, truckA, driverId, "2026-07-11"),
            await CompleteTripAsync(admin, customerId, configA, truckA, driverId, "2026-07-12"),
            await CompleteTripAsync(admin, customerId, configB, truckB, driverId, "2026-07-10")
        };

        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        // §13's own configuration floor (10) is a data-entry rule for the customer screen, not a database
        // constraint on the row itself — overriding it directly here (the same "stand-in via direct SQL" idiom
        // CC-17/CC-21 already used) lets a 3-vs-1-trip invoice demonstrate the same continuation rule the FSD's
        // own worked example shows at pageSize 50, without needing 11+ real trips through the full HTTP lifecycle.
        factory.Execute("UPDATE trp.InvoiceEvidences SET PageSize = 2 WHERE InvoiceId = @id", ("@id", invoiceId));
        await ProcessQueuedEvidenceAsync(factory, vehicles.Tenant);

        var evidence = (await (await admin.GetAsync($"/api/invoices/{invoiceId}/evidence")).DataAsync())[0];
        Assert.Equal(3, evidence.GetProperty("pageCount").GetInt32());
        Assert.Equal(4, evidence.GetProperty("lineCount").GetInt32());
        var vehiclePages = evidence.GetProperty("vehiclePages").EnumerateArray().ToDictionary(v => v.GetProperty("vehicleRegNo").GetString()!);
        var (regA, pagesA) = vehiclePages.First(kv => kv.Value.GetProperty("lineCount").GetInt32() == 3);
        Assert.Equal(1, pagesA.GetProperty("firstPage").GetInt32());
        Assert.Equal(2, pagesA.GetProperty("lastPage").GetInt32());
        var (regB, pagesB) = vehiclePages.First(kv => kv.Value.GetProperty("lineCount").GetInt32() == 1);
        Assert.NotEqual(regA, regB);
        Assert.Equal(3, pagesB.GetProperty("firstPage").GetInt32());
        Assert.Equal(3, pagesB.GetProperty("lastPage").GetInt32());
    }

    [Fact]
    public async Task AC_67_two_customers_with_different_templates_get_the_same_evidence_layout()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var truck1 = VehicleWorld.Id(await vehicles.ActiveAsync());
        var truck2 = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();

        var customer1 = await ReadyCustomerAsync(admin);
        var config1 = await ReadyConfigAsync(admin, customer1, truck1, 20000, "Layout Route 1");
        var trip1 = await CompleteTripAsync(admin, customer1, config1, truck1, driverId, "2026-07-10");
        var invoice1 = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId = customer1, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { trip1 } })).DataAsync();

        var customer2 = await ReadyCustomerAsync(admin);
        var config2 = await ReadyConfigAsync(admin, customer2, truck2, 20000, "Layout Route 2");
        var trip2 = await CompleteTripAsync(admin, customer2, config2, truck2, driverId, "2026-07-10");
        var invoice2 = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId = customer2, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { trip2 } })).DataAsync();

        await ProcessQueuedEvidenceAsync(factory, vehicles.Tenant);

        var e1 = (await (await admin.GetAsync($"/api/invoices/{invoice1.GetProperty("invoiceId").GetInt64()}/evidence")).DataAsync())[0];
        var e2 = (await (await admin.GetAsync($"/api/invoices/{invoice2.GetProperty("invoiceId").GetInt64()}/evidence")).DataAsync())[0];
        // §41: "No template" — evidence never reads the invoice's own template, so both customers (different
        // invoice templates) land on identical evidence structure regardless.
        Assert.Equal("Generated", e1.GetProperty("status").GetString());
        Assert.Equal("Generated", e2.GetProperty("status").GetString());
        Assert.Equal(e1.GetProperty("groupBy").GetString(), e2.GetProperty("groupBy").GetString());
        Assert.Equal(e1.GetProperty("pageSize").GetInt32(), e2.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Download_refuses_until_generated_then_returns_a_url()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Download Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var evidenceId = (await (await admin.GetAsync($"/api/invoices/{invoiceId}/evidence")).DataAsync())[0].GetProperty("invoiceEvidenceId").GetInt64();

        var early = await admin.GetAsync($"/api/invoices/{invoiceId}/evidence/{evidenceId}/download");
        Assert.Equal((HttpStatusCode)422, early.StatusCode);
        Assert.Contains("EVIDENCE_NOT_READY", await early.Content.ReadAsStringAsync());

        await ProcessQueuedEvidenceAsync(factory, vehicles.Tenant);
        var ready = await admin.GetAsync($"/api/invoices/{invoiceId}/evidence/{evidenceId}/download");
        Assert.True(ready.IsSuccessStatusCode, await ready.Content.ReadAsStringAsync());
        var download = await ready.DataAsync();
        Assert.False(string.IsNullOrWhiteSpace(download.GetProperty("url").GetString()));
    }

    [Fact]
    public async Task Retry_is_refused_unless_the_row_has_failed()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Retry Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var evidenceId = (await (await admin.GetAsync($"/api/invoices/{invoiceId}/evidence")).DataAsync())[0].GetProperty("invoiceEvidenceId").GetInt64();

        // Still Queued — not Failed — so a retry is refused.
        var response = await admin.PostAsync($"/api/invoices/{invoiceId}/evidence/{evidenceId}/retry", null);
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("EVIDENCE_NOT_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rerendering_creates_a_new_version_that_coexists_with_the_old_one()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Rerender Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        await ProcessQueuedEvidenceAsync(factory, vehicles.Tenant);

        var rerender = await admin.PostAsync($"/api/invoices/{invoiceId}/evidence/rerender", null);
        Assert.True(rerender.IsSuccessStatusCode, await rerender.Content.ReadAsStringAsync());
        var v2 = await rerender.DataAsync();
        Assert.Equal(2, v2.GetProperty("evidenceVersion").GetInt32());
        Assert.Equal("Generated", v2.GetProperty("status").GetString());

        var versions = await (await admin.GetAsync($"/api/invoices/{invoiceId}/evidence")).DataAsync();
        Assert.Equal(2, versions.GetArrayLength());
        Assert.Equal(2, versions[0].GetProperty("evidenceVersion").GetInt32());
        Assert.Equal(1, versions[1].GetProperty("evidenceVersion").GetInt32());
        Assert.Equal("Generated", versions[1].GetProperty("status").GetString());   // the old version is kept, not superseded/removed
    }

    [Fact]
    public async Task Retry_and_rerender_each_need_their_own_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, "Permission Route");
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        var invoiceId = invoice.GetProperty("invoiceId").GetInt64();
        var evidenceId = (await (await admin.GetAsync($"/api/invoices/{invoiceId}/evidence")).DataAsync())[0].GetProperty("invoiceEvidenceId").GetInt64();

        var viewOnly = vehicles.As("ViewOnly", 66, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_VIEW]);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.PostAsync($"/api/invoices/{invoiceId}/evidence/{evidenceId}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.PostAsync($"/api/invoices/{invoiceId}/evidence/rerender", null)).StatusCode);

        var financeOnly = vehicles.As("FinanceOnly", 67,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_EVIDENCE_RETRY]);
        Assert.Equal(HttpStatusCode.Forbidden, (await financeOnly.PostAsync($"/api/invoices/{invoiceId}/evidence/rerender", null)).StatusCode);
    }
}
