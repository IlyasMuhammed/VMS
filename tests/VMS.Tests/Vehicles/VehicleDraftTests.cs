using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Vehicles;

/// <summary>S2-VH-01, 02, 08, 16: saving a vehicle (as a Draft, then in the fleet), its numbers, and what must be unique.</summary>
[Collection(ApiCollection.Name)]
public sealed class VehicleDraftTests(ApiFactory factory)
{
    private static int Id(JsonObject v) => VehicleWorld.Id(v);

    // ── Creating ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_new_vehicle_is_a_draft_with_the_next_code_and_a_first_lifecycle_row()
    {
        var w = await VehicleWorld.CreateAsync(factory);

        var first = await w.CreateAsync(w.Truck("LEA-1234"));
        var second = await w.CreateAsync(w.Truck());

        Assert.Matches(@"^VH-\d{2}-0001$", first["vehicleCode"]!.GetValue<string>());
        Assert.Matches(@"^VH-\d{2}-0002$", second["vehicleCode"]!.GetValue<string>());
        Assert.Equal("Draft", first["status"]!.GetValue<string>());
        var lifecycle = Assert.Single(factory.Query("SELECT EventType, ToStatus, UserName FROM veh.VehicleLifecycle WHERE VehicleId = @i", ("@i", Id(first))));
        Assert.Equal(("Created", "Draft", "Fleet Admin"), (lifecycle["EventType"], lifecycle["ToStatus"], lifecycle["UserName"]));
    }

    [Fact]
    public async Task Vehicle_numbers_are_counted_for_each_tenant_on_its_own()
    {
        var a = await VehicleWorld.CreateAsync(factory);
        var b = await VehicleWorld.CreateAsync(factory);
        await a.CreateAsync(a.Truck());

        var firstOfB = await b.CreateAsync(b.Truck());

        Assert.EndsWith("0001", firstOfB["vehicleCode"]!.GetValue<string>());
    }

    [Fact]
    public async Task Every_missing_field_is_reported_at_once()
    {
        var w = await VehicleWorld.CreateAsync(factory);

        var response = await w.PostAsync(new JsonObject());
        var errors = await PartnerWorld.ErrorsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        foreach (var field in new[] { "registrationNo", "vehicleTypeId", "makeId", "model", "fuelType" }) Assert.Contains(errors, e => e.Field == field);
        Assert.Contains(errors, e => e is { Field: "registrationNo", Code: Msg.VhRegNoRequired });
    }

    [Fact]
    public async Task A_truck_needs_a_load_capacity_and_a_pickup_does_not()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var truck = w.Truck(); truck.Remove("loadCapacity"); truck.Remove("capacityUnit");

        var response = await w.PostAsync(truck);

        await PartnerWorld.AssertRefusedAsync(response, "loadCapacity", Msg.VhCapacityRequired);
        Assert.Contains("Truck", (await PartnerWorld.ErrorsAsync(response)).Single(e => e.Field == "loadCapacity").Message);
        Assert.Equal(HttpStatusCode.Created, (await w.PostAsync(w.Pickup())).StatusCode);
    }

    [Theory]
    [InlineData("capacityUnit", null, "loadCapacity", 5, Msg.Required)]     // a capacity needs its unit
    [InlineData("capacityUnit", "Bushel", null, null, Msg.OneOf)]
    [InlineData("tyreCount", 1, null, null, Msg.Invalid)]
    [InlineData("tyreCount", 23, null, null, Msg.Invalid)]
    [InlineData("manufacturingYear", 1949, null, null, Msg.Invalid)]
    [InlineData("manufacturingYear", 2999, null, null, Msg.Invalid)]
    [InlineData("fuelType", "Steam", null, null, Msg.OneOf)]
    [InlineData("openingOdometer", -5, "openingOdometerDate", "2026-01-01", Msg.Min)]
    public async Task A_field_out_of_range_is_refused(string field, object? value, string? other, object? otherValue, string code)
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var body = w.Pickup();
        body[field] = value is null ? null : JsonValue.Create(value);
        if (other is not null) body[other] = otherValue is null ? null : JsonValue.Create(otherValue);
        if (field == "capacityUnit" && value is null) body["loadCapacity"] = 5;

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(body), field, code);
    }

    [Fact]
    public async Task An_opening_odometer_needs_a_date_that_is_not_in_the_future_and_becomes_the_first_reading()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var noDate = w.Pickup(); noDate["openingOdometer"] = 1000;
        var future = w.Pickup(); future["openingOdometer"] = 1000; future["openingOdometerDate"] = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(noDate), "openingOdometerDate", Msg.Required);
        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(future), "openingOdometerDate", Msg.NotFuture);

        var ok = w.Pickup(); ok["openingOdometer"] = 45000; ok["openingOdometerDate"] = "2026-01-10";
        var created = await w.CreateAsync(ok);

        var reading = Assert.Single(factory.Query("SELECT Km, Source FROM veh.OdometerReadings WHERE VehicleId = @i", ("@i", Id(created))));
        Assert.Equal((45000, "VehicleCreation"), (reading["Km"], reading["Source"]));
    }

    [Fact]
    public async Task A_master_list_value_that_does_not_exist_or_is_another_tenants_is_refused()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var other = await VehicleWorld.CreateAsync(factory);
        var bad = w.Truck(); bad["vehicleTypeId"] = 999_999;
        var foreign = w.Truck(); foreign["makeId"] = other.Make;   // the other tenant's row for the same make

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(bad), "vehicleTypeId", Msg.Invalid);
        if (other.Make != w.Make) await PartnerWorld.AssertRefusedAsync(await w.PostAsync(foreign), "makeId", Msg.Invalid);
    }

    // ── Uniqueness ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_registration_number_is_stored_in_capitals_and_compared_without_spaces_or_dashes()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var existing = await w.CreateAsync(w.Truck("les-1234"));
        Assert.Equal("LES-1234", existing["registrationNo"]!.GetValue<string>());

        var response = await w.PostAsync(w.Truck("LES 1234"));

        await PartnerWorld.AssertRefusedAsync(response, "registrationNo", Msg.VhRegNoExists);
        var message = (await PartnerWorld.ErrorsAsync(response)).Single(e => e.Field == "registrationNo").Message;
        Assert.Contains(existing["vehicleCode"]!.GetValue<string>(), message);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.PostAsync(w.Truck("LES1234"))).StatusCode);
    }

    [Fact]
    public async Task A_sold_vehicles_registration_number_can_be_used_again()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var old = await w.CreateAsync(w.Truck("SOLD-1"));
        w.SetStatus(Id(old), "Sold");

        Assert.Equal(HttpStatusCode.Created, (await w.PostAsync(w.Truck("SOLD 1"))).StatusCode);
    }

    [Fact]
    public async Task Chassis_and_engine_numbers_are_unique_when_entered()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var first = w.Truck(); first["chassisNo"] = "chs-778899"; first["engineNo"] = "eng-112233";
        var existing = await w.CreateAsync(first);
        var second = w.Truck(); second["chassisNo"] = "CHS-778899"; second["engineNo"] = "ENG-112233";

        var errors = await PartnerWorld.ErrorsAsync(await w.PostAsync(second));

        Assert.Contains(errors, e => e is { Field: "chassisNo", Code: Msg.VhChassisOrEngineDuplicate } && e.Message.Contains(existing["vehicleCode"]!.GetValue<string>()));
        Assert.Contains(errors, e => e is { Field: "engineNo", Code: Msg.VhChassisOrEngineDuplicate });
        Assert.Equal(HttpStatusCode.Created, (await w.PostAsync(w.Truck())).StatusCode);   // none entered: nothing to clash
        Assert.Equal(HttpStatusCode.Created, (await w.PostAsync(w.Truck())).StatusCode);
    }

    [Fact]
    public async Task The_same_registration_in_another_tenant_is_nobodys_business()
    {
        var a = await VehicleWorld.CreateAsync(factory);
        var b = await VehicleWorld.CreateAsync(factory);
        await a.CreateAsync(a.Truck("SHARED-9"));

        Assert.Equal(HttpStatusCode.Created, (await b.PostAsync(b.Truck("SHARED-9"))).StatusCode);
    }

    [Fact]
    public async Task Six_people_saving_the_same_registration_at_the_same_moment_get_one_vehicle_and_five_refusals()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var reg = VehicleWorld.NextReg();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => w.PostAsync(w.Truck(reg))));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        var next = await w.CreateAsync(w.Truck());
        Assert.EndsWith("0002", next["vehicleCode"]!.GetValue<string>());   // the failed attempts used no numbers
    }

    // ── Partners of the vehicle ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_default_driver_fuel_card_company_and_tracker_company_must_hold_the_right_role()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var driver = await w.DriverAsync();
        var fuel = await w.PartnerAsync("FuelCardCompany");
        var tracker = await w.PartnerAsync("TrackerCompany");
        var workshop = await w.PartnerAsync("Workshop");

        var good = w.Truck(); good["defaultDriverId"] = driver; good["fuelCardCompanyId"] = fuel; good["fuelCardNumber"] = "FC-1"; good["trackerCompanyId"] = tracker;
        var created = await w.CreateAsync(good);
        Assert.Equal(driver, created["defaultDriver"]!["id"]!.GetValue<int>());
        Assert.False(string.IsNullOrEmpty(created["fuelCardCompany"]!["bpCode"]!.GetValue<string>()));

        var wrong = w.Truck(); wrong["defaultDriverId"] = workshop; wrong["fuelCardCompanyId"] = workshop; wrong["fuelCardNumber"] = "FC-2"; wrong["trackerCompanyId"] = workshop;
        var errors = await PartnerWorld.ErrorsAsync(await w.PostAsync(wrong));
        foreach (var field in new[] { "defaultDriverId", "fuelCardCompanyId", "trackerCompanyId" })
            Assert.Contains(errors, e => e.Field == field && e.Code == Msg.VhCounterpartyLacksRole);
    }

    [Fact]
    public async Task A_fuel_card_company_needs_a_card_number_and_an_inactive_partner_cannot_be_chosen()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var fuel = await w.PartnerAsync("FuelCardCompany");
        var driver = await w.DriverAsync();
        var noNumber = w.Truck(); noNumber["fuelCardCompanyId"] = fuel;
        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(noNumber), "fuelCardNumber", Msg.Required);

        await w.Admin.PostAsJsonAsync($"/api/partners/{driver}/status", new { status = "Inactive" });
        var inactive = w.Truck(); inactive["defaultDriverId"] = driver;
        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(inactive), "defaultDriverId", Msg.Invalid);   // BR-VH-020
    }

    // ── Updating ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_edit_changes_the_vehicle_and_is_audited_against_the_vehicle()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        vehicle["colour"] = "White"; vehicle["model"] = "700";

        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(vehicle)).StatusCode);
        var saved = await w.GetAsync(Id(vehicle));

        Assert.Equal(("White", "700"), (saved["colour"]!.GetValue<string>(), saved["model"]!.GetValue<string>()));
        var row = Assert.Single(factory.Query(
            "SELECT OldValue, NewValue, RootEntity, RootRecordId FROM core.AuditEntries WHERE TenantId = @t AND Entity = 'Vehicle' AND Action = 'Updated' AND Field = 'Model'", ("@t", w.Tenant)));
        Assert.Equal(("500", "700", "Vehicle", Id(vehicle).ToString()), (row["OldValue"], row["NewValue"], row["RootEntity"], row["RootRecordId"]));
    }

    [Fact]
    public async Task A_stale_save_is_refused_and_says_who_changed_the_vehicle()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var amna = w.As("Amna Malik", 10, VehicleWorld.VehiclePermissions);
        var bilal = w.As("Bilal Ahmed", 11, VehicleWorld.VehiclePermissions);
        var created = await w.CreateAsync(w.Truck());
        var hers = await w.GetAsync(Id(created), amna);
        var his = await w.GetAsync(Id(created), bilal);
        his["colour"] = "Blue";
        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(his, bilal)).StatusCode);

        hers["colour"] = "Red";
        var response = await w.PutAsync(hers, amna);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Bilal Ahmed", await response.MessageAsync());
        Assert.Equal("Blue", (await w.GetAsync(Id(created)))["colour"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_save_without_a_row_version_is_refused_and_a_sold_vehicle_cannot_be_edited()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        var withoutVersion = (JsonObject)vehicle.DeepClone(); withoutVersion.Remove("rowVersion");
        await PartnerWorld.AssertRefusedAsync(await w.PutAsync(withoutVersion), "rowVersion", Msg.Required);

        w.SetStatus(Id(vehicle), "Sold");
        var response = await w.PutAsync(await w.GetAsync(Id(vehicle)));
        await PartnerWorld.AssertRefusedAsync(response, "status", Msg.VhWrongStatus);
    }

    [Fact]
    public async Task Someone_elses_vehicle_is_not_found()
    {
        var mine = await VehicleWorld.CreateAsync(factory);
        var other = await VehicleWorld.CreateAsync(factory);
        var theirs = await other.CreateAsync(other.Truck());

        Assert.Equal(HttpStatusCode.NotFound, (await mine.Admin.GetAsync($"/api/vehicles/{Id(theirs)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mine.PutAsync(theirs)).StatusCode);
    }

    [Fact]
    public async Task The_wizards_answers_for_later_steps_are_kept_on_a_draft_and_dropped_once_the_vehicle_is_in_the_fleet()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var body = w.Truck(); body["draftData"] = "{\"step\":3,\"category\":\"Rented\"}";
        var draft = await w.CreateAsync(body);
        Assert.Equal("{\"step\":3,\"category\":\"Rented\"}", draft["draftData"]!.GetValue<string>());

        var active = await w.ActiveAsync(w.Truck());
        active["draftData"] = "{\"ignored\":true}";
        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(active)).StatusCode);
        Assert.Null((await w.GetAsync(Id(active)))["draftData"]);   // only a Draft holds it
    }

    [Fact]
    public async Task An_oversized_draft_is_refused()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var body = w.Truck(); body["draftData"] = new string('x', 210 * 1024);

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(body), "draftData", Msg.Invalid);
    }

    [Fact]
    public async Task On_a_vehicle_in_the_fleet_the_default_driver_is_changed_only_by_assigning()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var driver = await w.DriverAsync();
        var vehicle = await w.ActiveAsync();
        vehicle["defaultDriverId"] = driver;

        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(vehicle)).StatusCode);

        Assert.Null((await w.GetAsync(Id(vehicle)))["defaultDriver"]);
    }

    [Fact]
    public async Task Acquisition_details_need_the_acquisition_permission_and_are_ignored_without_it()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var manager = w.As("Fleet Manager", 5, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_CREATE, PermissionCodes.VEH_EDIT);
        var body = w.Truck(); body["acquisitionDate"] = "2026-02-01"; body["acquisitionType"] = "Purchase";

        var byManager = await w.CreateAsync((JsonObject)body.DeepClone(), manager);
        var byAdmin = await w.CreateAsync(w.Truck().Also(b => { b["acquisitionDate"] = "2026-02-01"; b["acquisitionType"] = "Purchase"; }));

        Assert.Null(byManager["acquisitionDate"]);
        Assert.Equal("2026-02-01", byAdmin["acquisitionDate"]!.GetValue<string>());
        var future = w.Truck(); future["acquisitionDate"] = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(future), "acquisitionDate", Msg.VhAcquisitionDateFuture);
    }

    // ── Fuel card (S2-VH-16) ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_fuel_card_is_on_one_vehicle_in_the_fleet_and_taking_it_needs_a_yes_that_frees_the_other()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var company = await w.PartnerAsync("FuelCardCompany");
        JsonObject Carded() { var b = w.Truck(); b["fuelCardCompanyId"] = company; b["fuelCardNumber"] = "PSO-500"; return b; }

        var a = await w.ActiveAsync(Carded());
        var b = await w.ActiveAsync(w.Truck());
        b["fuelCardCompanyId"] = company; b["fuelCardNumber"] = "PSO-500";

        var refused = await w.PutAsync(b);
        await PartnerWorld.AssertRefusedAsync(refused, "fuelCardNumber", Msg.VhFuelCardInUse);
        Assert.Contains((string)a["registrationNo"]!, (await PartnerWorld.ErrorsAsync(refused)).Single(e => e.Field == "fuelCardNumber").Message);

        b["reassignFuelCard"] = true;
        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(b)).StatusCode);
        Assert.Equal("PSO-500", (await w.GetAsync(Id(b)))["fuelCardNumber"]!.GetValue<string>());
        var freed = await w.GetAsync(Id(a));
        Assert.Null(freed["fuelCardNumber"]);
        Assert.Null(freed["fuelCardCompanyId"]);
    }

    [Fact]
    public async Task Draft_vehicles_may_share_a_fuel_card_number_because_only_the_fleet_is_judged()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var company = await w.PartnerAsync("FuelCardCompany");
        JsonObject Carded() { var b = w.Truck(); b["fuelCardCompanyId"] = company; b["fuelCardNumber"] = "DRAFT-CARD"; return b; }

        Assert.Equal(HttpStatusCode.Created, (await w.PostAsync(Carded())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await w.PostAsync(Carded())).StatusCode);
    }

    // ── Who may ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Creating_needs_create_and_viewing_needs_view()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var viewer = w.As("Viewer", 6, PermissionCodes.VEH_VIEW);
        var creator = w.As("Creator", 7, PermissionCodes.VEH_CREATE);
        var vehicle = await w.CreateAsync(w.Truck());

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/vehicles", w.Truck())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await creator.GetAsync($"/api/vehicles/{Id(vehicle)}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}", vehicle)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/vehicles/{Id(vehicle)}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/vehicles/{Id(vehicle)}")).StatusCode);
    }
}

internal static class JsonObjectExtensions
{
    /// <summary>Runs an action on the object and returns it, so a body can be built and adjusted in one expression.</summary>
    public static JsonObject Also(this JsonObject body, Action<JsonObject> action) { action(body); return body; }
}
