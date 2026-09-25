using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using VMS.Modules.Trips.Services;
using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-39: ledger reconciliation job and period lock (§40A.5 LR-4/LR-7, AC-49).</summary>
[Collection(ApiCollection.Name)]
public sealed class LedgerReconciliationAndPeriodLockTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything,
             PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT,
             PermissionCodes.TRP_PAYMENT_CREATE, PermissionCodes.TRP_BANKACCOUNT_MANAGE, PermissionCodes.TRP_LEDGER_VIEW, PermissionCodes.TRP_LEDGER_PERIODLOCK]);
        return (vehicles, admin);
    }

    /// <summary>A token for the same tenant/user as <paramref name="admin"/>'s own world, but with
    /// <c>is_super_admin=true</c> — <c>TestTokens</c> itself always hard-codes <c>false</c>, so this is built
    /// locally rather than changing shared test infrastructure for one test file's own need.</summary>
    private static HttpClient AsTenantSuperAdmin(ApiFactory factory, VehicleWorld vehicles)
    {
        var claims = new List<Claim>
        {
            new("sub", "999"), new("tenantId", vehicles.Tenant.ToString()), new("is_super_admin", "true"), new("user_name", "Tenant Super Admin")
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.Secret));
        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return factory.CreateClient().WithToken(new JwtSecurityTokenHandler().WriteToken(token));
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

    private static async Task<long> CreateInvoiceAsync(HttpClient admin, VehicleWorld vehicles, int customerId, decimal tripAmount, string routeName)
    {
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, customerId, truck, tripAmount, routeName);
        var tripId = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        return invoice.GetProperty("invoiceId").GetInt64();
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, long invoiceId, string rowVersion, string? submittedOn = null) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/invoices/{invoiceId}/submit")
        {
            Content = JsonContent.Create(new { rowVersion, submittedOn }),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } }
        });

    private static async Task<long> InvoiceIdAndSubmitAsync(HttpClient admin, VehicleWorld vehicles, int customerId, decimal tripAmount, string routeName)
    {
        var invoiceId = await CreateInvoiceAsync(admin, vehicles, customerId, tripAmount, routeName);
        var invoice = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();
        (await SubmitAsync(admin, invoiceId, invoice.GetProperty("rowVersion").GetString()!)).EnsureSuccessStatusCode();
        return invoiceId;
    }

    private static async Task<VMS.Modules.Trips.Models.LedgerReconciliationRunModel> RunJobAsync(ApiFactory factory, Guid tenantId)
    {
        factory.CreateClient();
        VMS.Modules.Trips.Models.LedgerReconciliationRunModel result = null!;
        await BackgroundTenantScope.RunAsAsync(tenantId, async () =>
        {
            using var scope = factory.Services.CreateScope();
            using var acting = scope.ServiceProvider.GetRequiredService<IAuditContext>().ActAsSystem("Test reconciliation run");
            result = await scope.ServiceProvider.GetRequiredService<ILedgerReconciliationJob>().RunAsync();
        });
        return result;
    }

    [Fact]
    public async Task AC_49_reconciliation_finds_no_mismatch_when_the_ledger_agrees_with_the_invoice()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        await InvoiceIdAndSubmitAsync(admin, vehicles, customerId, 50000, "Reconcile OK Route");

        var run = await RunJobAsync(factory, vehicles.Tenant);
        Assert.True(run.InvoicesChecked >= 1);
        Assert.Equal(0, run.MismatchCount);
        Assert.Empty(run.Mismatches);
    }

    [Fact]
    public async Task A_ledger_invoice_mismatch_is_found_and_reported()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoiceId = await InvoiceIdAndSubmitAsync(admin, vehicles, customerId, 50000, "Reconcile Mismatch Route");

        // Simulate a silent ledger/header drift the API itself can never produce — the same "direct-SQL stand-in
        // for an otherwise-unreachable scenario" idiom this register uses repeatedly.
        factory.Execute("UPDATE trp.Invoices SET BalanceAmount = 49000 WHERE InvoiceId = @id", ("@id", invoiceId));

        var run = await RunJobAsync(factory, vehicles.Tenant);
        Assert.Equal(1, run.MismatchCount);
        var mismatch = Assert.Single(run.Mismatches);
        Assert.Equal(invoiceId, mismatch.InvoiceId);
        Assert.Equal(50000, mismatch.LedgerBalance);
        Assert.Equal(49000, mismatch.InvoiceBalance);
        Assert.Equal(1000, mismatch.Difference);

        var latest = await (await admin.GetAsync("/api/ledger-reconciliation/latest")).DataAsync();
        Assert.Equal(1, latest.GetProperty("mismatchCount").GetInt32());
    }

    [Fact]
    public async Task Closed_period_posting_is_rejected()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoiceId = await CreateInvoiceAsync(admin, vehicles, customerId, 30000, "Closed Period Route");
        var invoice = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();

        var yearMonth = "202608";
        (await admin.PostAsJsonAsync($"/api/ledger-periods/{yearMonth}/close", new { reason = "August closed for review" })).EnsureSuccessStatusCode();

        var response = await SubmitAsync(admin, invoiceId, invoice.GetProperty("rowVersion").GetString()!, "2026-08-15");
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("PERIOD_CLOSED", await response.Content.ReadAsStringAsync());

        // A date outside the closed month still posts normally.
        var normalResponse = await SubmitAsync(admin, invoiceId, invoice.GetProperty("rowVersion").GetString()!, Day());
        Assert.True(normalResponse.IsSuccessStatusCode, await normalResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_super_admin_can_post_into_a_closed_period()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoiceId = await CreateInvoiceAsync(admin, vehicles, customerId, 30000, "Bypass Route");
        var invoice = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();

        var yearMonth = "202608";
        (await admin.PostAsJsonAsync($"/api/ledger-periods/{yearMonth}/close", new { reason = "August closed for review" })).EnsureSuccessStatusCode();

        var superAdmin = AsTenantSuperAdmin(factory, vehicles);
        var response = await SubmitAsync(superAdmin, invoiceId, invoice.GetProperty("rowVersion").GetString()!, "2026-08-20");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reopening_a_closed_period_allows_posting_again()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var invoiceId = await CreateInvoiceAsync(admin, vehicles, customerId, 30000, "Reopen Route");
        var invoice = await (await admin.GetAsync($"/api/invoices/{invoiceId}")).DataAsync();

        var yearMonth = "202608";
        (await admin.PostAsJsonAsync($"/api/ledger-periods/{yearMonth}/close", new { reason = "August closed" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/ledger-periods/{yearMonth}/reopen", new { reason = "Correction needed" })).EnsureSuccessStatusCode();

        var response = await SubmitAsync(admin, invoiceId, invoice.GetProperty("rowVersion").GetString()!, "2026-08-20");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Closing_the_same_period_twice_is_refused()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        await ReadyCustomerAsync(admin);
        var yearMonth = "202607";
        (await admin.PostAsJsonAsync($"/api/ledger-periods/{yearMonth}/close", new { reason = "First close" })).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync($"/api/ledger-periods/{yearMonth}/close", new { reason = "Second close" });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("PERIOD_ALREADY_CLOSED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Period_lock_needs_its_own_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        await ReadyCustomerAsync(admin);

        var noPermission = vehicles.As("No Permission", 2,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_LEDGER_VIEW]);

        var response = await noPermission.PostAsJsonAsync("/api/ledger-periods/202609/close", new { reason = "No permission" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
