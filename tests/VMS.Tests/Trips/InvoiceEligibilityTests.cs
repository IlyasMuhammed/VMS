using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-24: eligible-trip search and overlap check (§32.1, §39, §47.3, AC-26, AC-57).</summary>
[Collection(ApiCollection.Name)]
public sealed class InvoiceEligibilityTests(ApiFactory factory)
{
    // A UtcNow-based "today", the same known-flaky-by-at-most-a-day idiom this test suite already accepts
    // elsewhere (the tenant's own clock is Asia/Karachi) — the ±30 day margin comfortably absorbs it while
    // staying well under the search's own 366-day max span.
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_GENERATE]);
        return (vehicles, admin);
    }

    /// <summary>An Active customer, ready configuration and one Completed trip — CompletionDate lands on whatever
    /// "today" the tenant's own clock computes, matching every other date-sensitive test in this register's own
    /// "never hardcode a date relative to real time" discipline.</summary>
    private static async Task<(int CustomerId, System.Text.Json.JsonElement Trip)> CompletedTripAsync(
        VehicleWorld vehicles, HttpClient admin, decimal? rateAmount = 25000, string tripDate = "2026-07-10")
    {
        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        var byAbbr = cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());

        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "Line 1" })).DataAsync();
        var customerId = customer.GetProperty("customerId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();

        var route = await (await admin.PostAsJsonAsync("/api/routes", new
        {
            routeName = "Lahore - Faisalabad",
            stops = new[] { new { cityId = byAbbr["LHR"], stopType = "Origin" }, new { cityId = byAbbr["FSD"], stopType = "Destination" } }
        })).DataAsync();
        var config = await (await admin.PostAsJsonAsync("/api/trip-configurations",
            new { customerId, name = "Daily", routeId = route.GetProperty("routeId").GetInt32(), directionType = "OneWay" })).DataAsync();
        var configId = config.GetProperty("tripConfigurationId").GetInt64();

        var truck = await vehicles.ActiveAsync();
        var vehicleId = VehicleWorld.Id(truck);
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId, effectiveFrom = "2020-01-01" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/activate", new { rowVersion = config.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();
        if (rateAmount is not null)
            (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount })).EnsureSuccessStatusCode();

        var driverId = await vehicles.DriverAsync();
        var trip = await (await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate })).DataAsync();
        var tripId = trip.GetProperty("tripId").GetInt64();

        var planned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Planned", new { rowVersion = trip.GetProperty("rowVersion").GetString() })).DataAsync();
        var assigned = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Assigned", new { rowVersion = planned.GetProperty("rowVersion").GetString() })).DataAsync();
        var started = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Started", new { rowVersion = assigned.GetProperty("rowVersion").GetString(), startOdometer = 100 })).DataAsync();
        var inTransit = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/InTransit", new { rowVersion = started.GetProperty("rowVersion").GetString() })).DataAsync();
        var atDelivery = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/AtDelivery", new { rowVersion = inTransit.GetProperty("rowVersion").GetString() })).DataAsync();
        var delivered = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Delivered", new { rowVersion = atDelivery.GetProperty("rowVersion").GetString(), endOdometer = 900 })).DataAsync();
        var completed = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Completed", new { rowVersion = delivered.GetProperty("rowVersion").GetString() })).DataAsync();
        return (customerId, completed);
    }

    [Fact]
    public async Task A_completed_trip_in_period_is_available_and_totalled()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, trip) = await CompletedTripAsync(vehicles, admin, rateAmount: 25000);

        var response = await admin.PostAsJsonAsync("/api/invoices/search-eligible-trips",
            new { customerId, periodFrom = Day(-30), periodTo = Day(30) });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.DataAsync();

        Assert.Equal(1, result.GetProperty("summary").GetProperty("completedTrips").GetInt32());
        Assert.Equal(1, result.GetProperty("summary").GetProperty("available").GetInt32());
        Assert.Equal(25000, result.GetProperty("summary").GetProperty("availableAmount").GetDecimal());
        var row = result.GetProperty("trips").EnumerateArray().First();
        Assert.Equal(trip.GetProperty("tripId").GetInt64(), row.GetProperty("tripId").GetInt64());
        Assert.Equal("Available", row.GetProperty("category").GetString());
        Assert.False(string.IsNullOrEmpty(row.GetProperty("route").GetString()));
    }

    [Fact]
    public async Task AC_26_an_inactive_trip_is_not_returned_as_available()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, trip) = await CompletedTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        await admin.PostAsJsonAsync($"/api/trips/{tripId}/inactivate", new { reason = "Duplicate", rowVersion = trip.GetProperty("rowVersion").GetString() });

        var result = await (await admin.PostAsJsonAsync("/api/invoices/search-eligible-trips", new { customerId, periodFrom = Day(-30), periodTo = Day(30) })).DataAsync();
        Assert.Equal(0, result.GetProperty("summary").GetProperty("available").GetInt32());
        Assert.Equal(1, result.GetProperty("summary").GetProperty("blocked").GetInt32());
        Assert.Contains(result.GetProperty("blockingErrors").EnumerateArray(), e => e.GetProperty("code").GetString() == "INACTIVE" && e.GetProperty("tripId").GetInt64() == tripId);
    }

    [Fact]
    public async Task A_trip_with_a_missing_rate_is_blocked_with_a_reason()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, trip) = await CompletedTripAsync(vehicles, admin, rateAmount: null);

        var result = await (await admin.PostAsJsonAsync("/api/invoices/search-eligible-trips", new { customerId, periodFrom = Day(-30), periodTo = Day(30) })).DataAsync();
        Assert.Equal(0, result.GetProperty("summary").GetProperty("available").GetInt32());
        Assert.Contains(result.GetProperty("blockingErrors").EnumerateArray(), e => e.GetProperty("code").GetString() == "RATE_MISSING");
    }

    [Fact]
    public async Task A_trip_already_on_an_active_invoice_is_categorized_as_already_invoiced()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, trip) = await CompletedTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        // No invoicing module exists yet — insert the schema rows directly, the same stand-in this register has
        // used before for a not-yet-built dependency.
        var tenantId = vehicles.Tenant;
        var invoiceId = factory.Scalar<long>(@"INSERT INTO trp.Invoices
            (TenantId, InvoiceNumber, Version, RootInvoiceId, CustomerId, PeriodFrom, PeriodTo, InvoiceDate,
             CustomerCode, CustomerName, CurrencyCode, PaymentTermsDays, Status, PaymentStatus, IsActive,
             TotalTripAmount, TotalAdjustment, GrossAmount, TotalDeduction, NetAmount,
             PaidAmount, AdvanceAppliedAmount, WriteOffAmount, DiscountAmount,
             TransferredInAmount, TransferredOutAmount, CarryForwardInAmount, CarryForwardOutAmount, RefundedAmount, BalanceAmount)
            OUTPUT INSERTED.InvoiceId
            VALUES (@t, @n, 1, 0, @c, '2020-01-01', '2030-01-01', '2026-08-01',
                    'CUS-00001', 'Acme', 'PKR', 30, 'Generated', 'Unpaid', 1,
                    0, 0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 0, 0, 0)",
            ("@t", tenantId), ("@n", $"INV-T-{Guid.NewGuid():N}"[..20]), ("@c", customerId));
        var lineId = factory.Scalar<long>(@"INSERT INTO trp.InvoiceLines
            (TenantId, InvoiceId, [LineNo], TripId, LineType, Description, Quantity, Rate, Amount, CustomerId, CustomerCode, CustomerName)
            OUTPUT INSERTED.InvoiceLineId
            VALUES (@t, @inv, 1, @trip, 'Trip', 'Test line', 1, 25000, 25000, @c, 'CUS-00001', 'Acme')",
            ("@t", tenantId), ("@inv", invoiceId), ("@trip", tripId), ("@c", customerId));
        factory.Execute(@"INSERT INTO trp.InvoiceTripLinks (TenantId, InvoiceId, TripId, InvoiceLineId, IsActive, LinkedOn)
            VALUES (@t, @inv, @trip, @line, 1, SYSUTCDATETIME())",
            ("@t", tenantId), ("@inv", invoiceId), ("@trip", tripId), ("@line", lineId));

        var result = await (await admin.PostAsJsonAsync("/api/invoices/search-eligible-trips", new { customerId, periodFrom = Day(-30), periodTo = Day(30) })).DataAsync();
        Assert.Equal(1, result.GetProperty("summary").GetProperty("alreadyInvoiced").GetInt32());
        Assert.Equal(0, result.GetProperty("summary").GetProperty("available").GetInt32());

        // Regenerating that same invoice treats its own link as available again.
        var regenerating = await (await admin.PostAsJsonAsync("/api/invoices/search-eligible-trips",
            new { customerId, periodFrom = Day(-30), periodTo = Day(30), regeneratingInvoiceId = invoiceId })).DataAsync();
        Assert.Equal(1, regenerating.GetProperty("summary").GetProperty("available").GetInt32());
        Assert.Equal(0, regenerating.GetProperty("overlaps").GetArrayLength());
    }

    [Fact]
    public async Task An_overlapping_active_invoice_is_listed_in_the_overlap_check()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, _) = await CompletedTripAsync(vehicles, admin);
        var tenantId = vehicles.Tenant;
        var invoiceNumber = $"INV-T-{Guid.NewGuid():N}"[..20];
        factory.Execute(@"INSERT INTO trp.Invoices
            (TenantId, InvoiceNumber, Version, RootInvoiceId, CustomerId, PeriodFrom, PeriodTo, InvoiceDate,
             CustomerCode, CustomerName, CurrencyCode, PaymentTermsDays, Status, PaymentStatus, IsActive,
             TotalTripAmount, TotalAdjustment, GrossAmount, TotalDeduction, NetAmount,
             PaidAmount, AdvanceAppliedAmount, WriteOffAmount, DiscountAmount,
             TransferredInAmount, TransferredOutAmount, CarryForwardInAmount, CarryForwardOutAmount, RefundedAmount, BalanceAmount)
            VALUES (@t, @n, 1, 0, @c, '2020-06-01', '2020-06-30', '2020-07-01',
                    'CUS-00001', 'Acme', 'PKR', 30, 'Generated', 'Unpaid', 1,
                    0, 0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 0, 0, 0)",
            ("@t", tenantId), ("@n", invoiceNumber), ("@c", customerId));

        var result = await (await admin.PostAsJsonAsync("/api/invoices/search-eligible-trips",
            new { customerId, periodFrom = "2020-06-15", periodTo = "2020-07-15" })).DataAsync();
        Assert.Equal(1, result.GetProperty("overlaps").GetArrayLength());
        Assert.Equal(invoiceNumber, result.GetProperty("overlaps")[0].GetProperty("invoiceNumber").GetString());
    }

    [Fact]
    public async Task The_response_exposes_resolved_template_address_and_tax_options()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, _) = await CompletedTripAsync(vehicles, admin);

        var cities = await (await admin.GetAsync("/api/lookups/CITY")).DataAsync();
        var cityId = cities.EnumerateArray().First().GetProperty("id").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/billing-addresses",
            new { addressName = "Head Office", addressLine1 = "Mall Road", cityId, isDefault = true })).EnsureSuccessStatusCode();

        var template = await (await admin.PostAsJsonAsync($"/api/customers/{customerId}/invoice-templates", new { templateName = "Standard" })).DataAsync();
        (await admin.PostAsJsonAsync($"/api/customer-invoice-templates/{template.GetProperty("customerInvoiceTemplateId").GetInt64()}/activate", new { })).EnsureSuccessStatusCode();

        var result = await (await admin.PostAsJsonAsync("/api/invoices/search-eligible-trips", new { customerId, periodFrom = Day(-30), periodTo = Day(30) })).DataAsync();
        var resolved = result.GetProperty("resolved");
        Assert.Equal(1, resolved.GetProperty("templateOptions").GetArrayLength());
        Assert.Equal(1, resolved.GetProperty("billingAddressOptions").GetArrayLength());
    }

    [Fact]
    public async Task Searching_needs_the_invoice_generate_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (customerId, _) = await CompletedTripAsync(vehicles, admin);
        var noPermission = vehicles.As("NoInvoice", 33, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        var response = await noPermission.PostAsJsonAsync("/api/invoices/search-eligible-trips", new { customerId, periodFrom = Day(-30), periodTo = Day(30) });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
