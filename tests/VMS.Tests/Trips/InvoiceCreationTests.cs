using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-25: invoice creation — lines, adjustments, deductions, concurrency (§32.2–32.4, §33, §35, §52,
/// AC-25, AC-27–AC-32).</summary>
[Collection(ApiCollection.Name)]
public sealed class InvoiceCreationTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_INVOICE_GENERATE, PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INCOME_EDIT]);
        return (vehicles, admin);
    }

    /// <summary>An Active customer with a default billing address and an active invoice template — ready for
    /// generation, not just for the search (CC-24's own tests didn't need either).</summary>
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

    private static async Task<long> ReadyConfigAsync(HttpClient admin, VehicleWorld vehicles, int customerId, int vehicleId, decimal rateAmount, string routeName)
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

    [Fact]
    public async Task AC_28_multiple_vehicles_and_routes_become_one_invoice()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truckA = VehicleWorld.Id(await vehicles.ActiveAsync());
        var truckB = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configA = await ReadyConfigAsync(admin, vehicles, customerId, truckA, 25000, "Route A");
        var configB = await ReadyConfigAsync(admin, vehicles, customerId, truckB, 30000, "Route B");
        var tripA = await CompleteTripAsync(admin, customerId, configA, truckA, driverId, "2026-07-10");
        var tripB = await CompleteTripAsync(admin, customerId, configB, truckB, driverId, "2026-07-11");

        var response = await admin.PostAsJsonAsync("/api/invoices", new
        {
            customerId, periodFrom = Day(-30), periodTo = Day(30),
            tripIds = new[] { tripA.GetProperty("tripId").GetInt64(), tripB.GetProperty("tripId").GetInt64() }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var invoice = await response.DataAsync();
        Assert.Equal("Generated", invoice.GetProperty("status").GetString());
        Assert.Equal(2, invoice.GetProperty("lines").GetArrayLength());
        Assert.Equal(55000, invoice.GetProperty("totalTripAmount").GetDecimal());
        Assert.Equal(55000, invoice.GetProperty("netAmount").GetDecimal());
        Assert.StartsWith("INV-", invoice.GetProperty("invoiceNumber").GetString());
    }

    [Fact]
    public async Task No_adjustment_only_invoices_are_allowed()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var response = await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = Array.Empty<long>() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC_27_a_missing_rate_trip_blocks_the_whole_invoice()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();

        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        var byAbbr = cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());
        var route = await (await admin.PostAsJsonAsync("/api/routes", new
        {
            routeName = "No Rate Route", stops = new[] { new { cityId = byAbbr["LHR"], stopType = "Origin" }, new { cityId = byAbbr["FSD"], stopType = "Destination" } }
        })).DataAsync();
        var config = await (await admin.PostAsJsonAsync("/api/trip-configurations",
            new { customerId, name = "No Rate", routeId = route.GetProperty("routeId").GetInt32(), directionType = "OneWay" })).DataAsync();
        var configId = config.GetProperty("tripConfigurationId").GetInt64();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId = truck, effectiveFrom = "2020-01-01" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/activate", new { rowVersion = config.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();
        // No rate created — the trip will save with RateMissing = true (AC-16).
        var trip = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");

        var response = await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { trip.GetProperty("tripId").GetInt64() } });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RATE_MISSING", body);
    }

    [Fact]
    public async Task AC_29_the_invoice_line_keeps_its_own_driver_name_snapshot()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, vehicles, customerId, truck, 25000, "Snapshot Route");
        var trip = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");

        var invoice = await (await admin.PostAsJsonAsync("/api/invoices",
            new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { trip.GetProperty("tripId").GetInt64() } })).DataAsync();
        var originalName = invoice.GetProperty("lines")[0].GetProperty("driverName").GetString();
        Assert.False(string.IsNullOrWhiteSpace(originalName));

        // The driver's master record changes name after the invoice exists.
        var driver = await vehicles.Partners.GetAsync(driverId);
        driver["legalName"] = "Changed Name " + Guid.NewGuid().ToString("N")[..6];
        (await vehicles.Partners.PutAsync(driver)).EnsureSuccessStatusCode();

        var reloaded = await (await admin.GetAsync($"/api/invoices/{invoice.GetProperty("invoiceId").GetInt64()}")).DataAsync();
        Assert.Equal(originalName, reloaded.GetProperty("lines")[0].GetProperty("driverName").GetString());
    }

    [Fact]
    public async Task AC_30_adjustments_are_summed_into_gross_and_both_rows_are_stored()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, vehicles, customerId, truck, 500000, "Adjustment Route");
        var trip = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");

        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new
        {
            customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { trip.GetProperty("tripId").GetInt64() },
            adjustments = new[]
            {
                new { adjustmentMonth = "August 2026", amount = 20000, note = "Rate difference for August trips" },
                new { adjustmentMonth = "July 2026", amount = -15000, note = "Previous overbilling adjustment" }
            }
        })).DataAsync();

        Assert.Equal(505000, invoice.GetProperty("grossAmount").GetDecimal());
        var adjustments = invoice.GetProperty("adjustments").EnumerateArray().ToList();
        Assert.Equal(2, adjustments.Count);
        Assert.Contains(adjustments, a => a.GetProperty("adjustmentMonth").GetString() == "August 2026" && a.GetProperty("adjustmentAmount").GetDecimal() == 20000);
        Assert.Contains(adjustments, a => a.GetProperty("adjustmentMonth").GetString() == "July 2026" && a.GetProperty("adjustmentAmount").GetDecimal() == -15000);
    }

    [Fact]
    public async Task AC_31_an_invoice_level_deduction_is_computed_and_snapshotted()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, vehicles, customerId, truck, 500000, "Deduction Route");
        var trip = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules", new
        {
            taxName = "Withholding Tax", taxCode = "WHT-236", taxType = "Percentage", taxPercentage = 2.0, applicable = true, calculationBasis = "InvoiceSubtotal"
        })).EnsureSuccessStatusCode();

        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new
        {
            customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { trip.GetProperty("tripId").GetInt64() },
            adjustments = new[] { new { adjustmentMonth = "August 2026", amount = 20000, note = "Rate difference" } }
        })).DataAsync();

        Assert.Equal(520000, invoice.GetProperty("grossAmount").GetDecimal());
        Assert.Equal(10400, invoice.GetProperty("totalDeduction").GetDecimal());
        Assert.Equal(509600, invoice.GetProperty("netAmount").GetDecimal());
        var taxLine = invoice.GetProperty("taxLines").EnumerateArray().First();
        Assert.Equal("WHT-236", taxLine.GetProperty("taxCode").GetString());
        Assert.True(taxLine.GetProperty("applicable").GetBoolean());
        Assert.Equal(10400, taxLine.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Billable_income_is_added_as_its_own_line_and_locked_afterwards()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, vehicles, customerId, truck, 25000, "Income Route");
        var trip = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var tripId = trip.GetProperty("tripId").GetInt64();

        var incomeTypes = await (await admin.GetAsync("/api/lookups/TRIP_INCOME_TYPE")).DataAsync();
        var detentionId = incomeTypes.EnumerateArray().First(t => t.GetProperty("code").GetString() == "DETENTION").GetProperty("id").GetInt32();
        var income = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/income", new { incomeTypeId = detentionId, amount = 2000, isBillable = true })).DataAsync();

        var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } })).DataAsync();
        Assert.Equal(2, invoice.GetProperty("lines").GetArrayLength());
        Assert.Contains(invoice.GetProperty("lines").EnumerateArray(), l => l.GetProperty("lineType").GetString() == "Income" && l.GetProperty("amount").GetDecimal() == 2000);
        Assert.Equal(27000, invoice.GetProperty("totalTripAmount").GetDecimal());

        // §30: a billed income row is locked — voiding it is now refused.
        var voidAttempt = await admin.PostAsJsonAsync($"/api/trip-income/{income.GetProperty("tripIncomeId").GetInt64()}/void", new { reason = "Trying anyway" });
        Assert.Equal(HttpStatusCode.Conflict, voidAttempt.StatusCode);
    }

    [Fact]
    public async Task An_overlapping_active_invoice_refuses_generation()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, vehicles, customerId, truck, 25000, "Overlap Route");
        var trip1 = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        (await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { trip1.GetProperty("tripId").GetInt64() } })).EnsureSuccessStatusCode();

        var trip2 = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-11");
        var response = await admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-10), periodTo = Day(60), tripIds = new[] { trip2.GetProperty("tripId").GetInt64() } });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("OVERLAP_DETECTED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AC_32_only_one_of_two_concurrent_requests_for_the_same_trip_succeeds()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var truck = VehicleWorld.Id(await vehicles.ActiveAsync());
        var driverId = await vehicles.DriverAsync();
        var configId = await ReadyConfigAsync(admin, vehicles, customerId, truck, 25000, "Concurrency Route");
        var trip = await CompleteTripAsync(admin, customerId, configId, truck, driverId, "2026-07-10");
        var tripId = trip.GetProperty("tripId").GetInt64();

        Task<HttpResponseMessage> Attempt() => admin.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { tripId } });
        var results = await Task.WhenAll(Attempt(), Attempt());

        Assert.Single(results, r => r.IsSuccessStatusCode);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Generating_needs_the_invoice_generate_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var customerId = await ReadyCustomerAsync(admin);
        var noPermission = vehicles.As("NoInvoice", 44, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        var response = await noPermission.PostAsJsonAsync("/api/invoices", new { customerId, periodFrom = Day(-30), periodTo = Day(30), tripIds = new[] { 1L } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
