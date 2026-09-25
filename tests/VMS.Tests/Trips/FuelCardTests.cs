using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-18: fuel cards and assignments (§28).</summary>
[Collection(ApiCollection.Name)]
public sealed class FuelCardTests(ApiFactory factory)
{
    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        return (vehicles, admin);
    }

    private static object CardBody(int companyId, string? cardNumber = null, string expiry = "2027-01-01") => new
    {
        cardNumber = cardNumber ?? $"CARD-{Guid.NewGuid():N}"[..14], fuelCardCompanyId = companyId, cardHolderName = "Ali Raza", expiryDate = expiry, monthlyLimit = 50000
    };

    [Fact]
    public async Task A_fuel_card_can_be_created_and_the_number_is_always_masked()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var companyId = await vehicles.PartnerAsync("FuelCardCompany");

        var created = await admin.PostAsJsonAsync("/api/fuel-cards", CardBody(companyId, "1234567890123456"));
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var card = await created.DataAsync();
        Assert.Equal(new string('*', 12) + "3456", card.GetProperty("maskedCardNumber").GetString());
        Assert.Equal("Active", card.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, card.GetProperty("vehicleId").ValueKind);

        var list = await (await admin.GetAsync("/api/fuel-cards")).DataAsync();
        Assert.All(list.EnumerateArray(), c => Assert.DoesNotContain("1234567890123456", c.GetProperty("maskedCardNumber").GetString()));
    }

    [Fact]
    public async Task A_duplicate_card_number_is_rejected()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var companyId = await vehicles.PartnerAsync("FuelCardCompany");
        var number = $"DUP-{Guid.NewGuid():N}"[..12];
        (await admin.PostAsJsonAsync("/api/fuel-cards", CardBody(companyId, number))).EnsureSuccessStatusCode();

        var second = await admin.PostAsJsonAsync("/api/fuel-cards", CardBody(companyId, number));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task The_issuing_partner_must_hold_the_fuel_card_company_role()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var workshop = await vehicles.PartnerAsync("Workshop");
        var response = await admin.PostAsJsonAsync("/api/fuel-cards", CardBody(workshop));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Assigning_a_card_sets_its_current_vehicle_and_driver()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var companyId = await vehicles.PartnerAsync("FuelCardCompany");
        var card = await (await admin.PostAsJsonAsync("/api/fuel-cards", CardBody(companyId))).DataAsync();
        var truck = await vehicles.ActiveAsync();
        var driverId = await vehicles.DriverAsync();

        var assigned = await (await admin.PostAsJsonAsync($"/api/fuel-cards/{card.GetProperty("fuelCardId").GetInt32()}/assign",
            new { vehicleId = VehicleWorld.Id(truck), driverId, assignedFrom = "2026-01-01", reason = "Initial issue", rowVersion = card.GetProperty("rowVersion").GetString() })).DataAsync();
        Assert.Equal(VehicleWorld.Id(truck), assigned.GetProperty("vehicleId").GetInt32());
        Assert.Equal(driverId, assigned.GetProperty("driverId").GetInt32());

        var history = await (await admin.GetAsync($"/api/fuel-cards/{card.GetProperty("fuelCardId").GetInt32()}/assignments")).DataAsync();
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, history[0].GetProperty("assignedTo").ValueKind);
    }

    [Fact]
    public async Task Reassigning_closes_the_previous_row_and_leaves_exactly_one_open()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var companyId = await vehicles.PartnerAsync("FuelCardCompany");
        var card = await (await admin.PostAsJsonAsync("/api/fuel-cards", CardBody(companyId))).DataAsync();
        var cardId = card.GetProperty("fuelCardId").GetInt32();
        var truckA = await vehicles.ActiveAsync();
        var truckB = await vehicles.ActiveAsync();

        var first = await (await admin.PostAsJsonAsync($"/api/fuel-cards/{cardId}/assign",
            new { vehicleId = VehicleWorld.Id(truckA), assignedFrom = "2026-01-01", rowVersion = card.GetProperty("rowVersion").GetString() })).DataAsync();
        var second = await admin.PostAsJsonAsync($"/api/fuel-cards/{cardId}/assign",
            new { vehicleId = VehicleWorld.Id(truckB), assignedFrom = "2026-02-01", rowVersion = first.GetProperty("rowVersion").GetString() });
        Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());

        var history = (await (await admin.GetAsync($"/api/fuel-cards/{cardId}/assignments")).DataAsync()).EnumerateArray().ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal(1, history.Count(h => h.GetProperty("assignedTo").ValueKind == System.Text.Json.JsonValueKind.Null));
        var closed = history.First(h => h.GetProperty("assignedTo").ValueKind != System.Text.Json.JsonValueKind.Null);
        Assert.Equal("2026-01-31", closed.GetProperty("assignedTo").GetString());

        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM trp.FuelCardAssignments WHERE FuelCardId = @id AND AssignedTo IS NULL", ("@id", cardId)));
    }

    [Fact]
    public async Task Saving_a_card_cannot_set_status_to_expired_directly()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var companyId = await vehicles.PartnerAsync("FuelCardCompany");
        var card = await (await admin.PostAsJsonAsync("/api/fuel-cards", CardBody(companyId))).DataAsync();

        var response = await admin.PutAsJsonAsync($"/api/fuel-cards/{card.GetProperty("fuelCardId").GetInt32()}",
            new { expiryDate = "2027-01-01", status = "Expired", rowVersion = card.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var blocked = await (await admin.PutAsJsonAsync($"/api/fuel-cards/{card.GetProperty("fuelCardId").GetInt32()}",
            new { expiryDate = "2027-01-01", status = "Blocked", rowVersion = card.GetProperty("rowVersion").GetString() })).DataAsync();
        Assert.Equal("Blocked", blocked.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_nightly_job_expires_only_active_cards_past_their_expiry_date()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var companyId = await vehicles.PartnerAsync("FuelCardCompany");
        var expiredCandidate = await (await admin.PostAsJsonAsync("/api/fuel-cards", CardBody(companyId, expiry: "2020-01-01"))).DataAsync();
        var stillGood = await (await admin.PostAsJsonAsync("/api/fuel-cards", CardBody(companyId, expiry: "2027-01-01"))).DataAsync();
        var alreadyBlocked = await (await admin.PostAsJsonAsync("/api/fuel-cards", CardBody(companyId, expiry: "2020-01-01"))).DataAsync();
        await admin.PutAsJsonAsync($"/api/fuel-cards/{alreadyBlocked.GetProperty("fuelCardId").GetInt32()}",
            new { expiryDate = "2020-01-01", status = "Blocked", rowVersion = alreadyBlocked.GetProperty("rowVersion").GetString() });

        var result = await (await admin.PostAsync("/api/admin/jobs/fuel-cards/run", null)).DataAsync();
        Assert.True(result.GetProperty("expired").GetInt32() >= 1);

        var reloaded = await (await admin.GetAsync($"/api/fuel-cards/{expiredCandidate.GetProperty("fuelCardId").GetInt32()}")).DataAsync();
        Assert.Equal("Expired", reloaded.GetProperty("status").GetString());
        var untouched = await (await admin.GetAsync($"/api/fuel-cards/{stillGood.GetProperty("fuelCardId").GetInt32()}")).DataAsync();
        Assert.Equal("Active", untouched.GetProperty("status").GetString());
        var blockedReloaded = await (await admin.GetAsync($"/api/fuel-cards/{alreadyBlocked.GetProperty("fuelCardId").GetInt32()}")).DataAsync();
        Assert.Equal("Blocked", blockedReloaded.GetProperty("status").GetString());   // the job never overwrites a deliberate back-office status
    }

    [Fact]
    public async Task Fuel_card_actions_need_the_fuelcard_edit_permission()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var companyId = await vehicles.PartnerAsync("FuelCardCompany");
        var noPermission = vehicles.As("NoFuelCard", 44, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything]);
        Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.GetAsync("/api/fuel-cards")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.PostAsJsonAsync("/api/fuel-cards", CardBody(companyId))).StatusCode);
    }
}
