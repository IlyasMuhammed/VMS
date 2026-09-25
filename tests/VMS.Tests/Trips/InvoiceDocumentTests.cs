using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-27: invoice PDF rendering, SystemStandard layout (§15, §34). "Re-print returns the stored file;
/// 'Re-render' is Admin-only." Customer-specific layouts are the still-blocked half (Q2) and aren't tested here.</summary>
[Collection(ApiCollection.Name)]
public sealed class InvoiceDocumentTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_RERENDER]);
        return (vehicles, admin);
    }

    /// <summary>An Active customer with a default billing address and an active invoice template — same shape as
    /// InvoiceCreationTests' own ReadyCustomerAsync.</summary>
    private static async Task<int> ReadyCustomerAsync(HttpClient admin)
    {
        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "Line 1" })).DataAsync();
        var customerId = customer.GetProperty("customerId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();

        var cities = await (await admin.GetAsync("/api/lookups/CITY")).DataAsync();
        var cityId = cities.EnumerateArray().First().GetProperty("id").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/billing-addresses",
            new { addressName = "Head Office", addressLine1 = "Mall Road", cityId, isDefault = true })).EnsureSuccessStatusCode();

        var template = await (await admin.PostAsJsonAsync($"/api/customers/{customerId}/invoice-templates", new { templateName = "Standard" })).DataAsync();
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

    private static async Task<System.Text.Json.JsonElement> CompleteTripAsync(HttpClient admin, int customerId, long configId, int vehicleId, int driverId, string tripDate)
    {
        var trip = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate })).DataAsync();
        var tripId = trip.GetProperty("tripId").GetInt64();
        var planned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Planned", new { rowVersion = trip.GetProperty("rowVersion").GetString() })).DataAsync();
        var assigned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Assigned", new { rowVersion = planned.GetProperty("rowVersion").GetString() })).DataAsync();
        var started = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Started", new { rowVersion = assigned.GetProperty("rowVersion").GetString(), startOdometer = 100 })).DataAsync();
        var inTransit = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/InTransit", new { rowVersion = started.GetProperty("rowVersion").GetString() })).DataAsync();
        var atDelivery = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/AtDelivery", new { rowVersion = inTransit.GetProperty("rowVersion").GetString() })).DataAsync();
        var delivered = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Delivered", new { rowVersion = atDelivery.GetProperty("rowVersion").GetString(), endOdometer = 900 })).DataAsync();
        return await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Completed", new { rowVersion = delivered.GetProperty("rowVersion").GetString() })).DataAsync();
    }

    private static async Task<long> ReadyInvoiceAsync(VehicleWorld vehicles, HttpClient admin)
    {
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, 25000, $"Doc Route {Guid.NewGuid():N}"[..24]);
        var trip = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");

        var invoice = await (await admin.PostAsJsonAsync("/api/invoices",
            new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { trip.GetProperty("tripId").GetInt64() } })).DataAsync();
        return invoice.GetProperty("invoiceId").GetInt64();
    }

    [Fact]
    public async Task Printing_generates_the_first_version_and_reprinting_returns_the_same_file()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var invoiceId = await ReadyInvoiceAsync(vehicles, admin);

        var firstResponse = await admin.PostAsync($"/api/invoices/{invoiceId}/document/print", null);
        Assert.True(firstResponse.IsSuccessStatusCode, await firstResponse.Content.ReadAsStringAsync());
        var first = await firstResponse.DataAsync();
        Assert.Equal(1, first.GetProperty("document").GetProperty("version").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("url").GetString()));
        var firstHash = first.GetProperty("document").GetProperty("sha256").GetString();
        var firstDocumentId = first.GetProperty("document").GetProperty("invoiceDocumentId").GetInt64();

        var second = await (await admin.PostAsync($"/api/invoices/{invoiceId}/document/print", null)).DataAsync();
        Assert.Equal(1, second.GetProperty("document").GetProperty("version").GetInt32());
        Assert.Equal(firstHash, second.GetProperty("document").GetProperty("sha256").GetString());
        Assert.Equal(firstDocumentId, second.GetProperty("document").GetProperty("invoiceDocumentId").GetInt64());
    }

    [Fact]
    public async Task Rerendering_creates_a_new_version_without_altering_the_invoice()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var invoiceId = await ReadyInvoiceAsync(vehicles, admin);
        var before = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();

        (await admin.PostAsync($"/api/invoices/{invoiceId}/document/print", null)).EnsureSuccessStatusCode();
        var rerenderResponse = await admin.PostAsync($"/api/invoices/{invoiceId}/document/rerender", null);
        Assert.True(rerenderResponse.IsSuccessStatusCode, await rerenderResponse.Content.ReadAsStringAsync());
        var rerendered = await rerenderResponse.DataAsync();
        Assert.Equal(2, rerendered.GetProperty("document").GetProperty("version").GetInt32());

        // A further print now returns the new (v2) current document, not the original v1.
        var printed = await (await admin.PostAsync($"/api/invoices/{invoiceId}/document/print", null)).DataAsync();
        Assert.Equal(2, printed.GetProperty("document").GetProperty("version").GetInt32());
        Assert.Equal(rerendered.GetProperty("document").GetProperty("invoiceDocumentId").GetInt64(), printed.GetProperty("document").GetProperty("invoiceDocumentId").GetInt64());

        var after = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();
        Assert.Equal(before.GetProperty("netAmount").GetDecimal(), after.GetProperty("netAmount").GetDecimal());
        Assert.Equal(before.GetProperty("rowVersion").GetString(), after.GetProperty("rowVersion").GetString());
    }

    [Fact]
    public async Task Rerendering_needs_its_own_permission_separate_from_view()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var invoiceId = await ReadyInvoiceAsync(vehicles, admin);
        var viewOnly = vehicles.As("ViewOnly", 77, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_VIEW]);

        var print = await viewOnly.PostAsync($"/api/invoices/{invoiceId}/document/print", null);
        Assert.True(print.IsSuccessStatusCode, await print.Content.ReadAsStringAsync());

        var rerender = await viewOnly.PostAsync($"/api/invoices/{invoiceId}/document/rerender", null);
        Assert.Equal(HttpStatusCode.Forbidden, rerender.StatusCode);
    }

    [Fact]
    public async Task Printing_needs_the_invoice_view_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var invoiceId = await ReadyInvoiceAsync(vehicles, admin);
        var noPermission = vehicles.As("NoInvoice", 88, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.PostAsync($"/api/invoices/{invoiceId}/document/print", null)).StatusCode);
    }
}
