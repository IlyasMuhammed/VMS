using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Vehicles;

/// <summary>S2-VH-05, 06, 07, 14: attached items, the odometer, the default driver, and the vehicle list.</summary>
[Collection(ApiCollection.Name)]
public sealed class VehicleDetailTests(ApiFactory factory)
{
    private static int Id(JsonObject v) => VehicleWorld.Id(v);
    private static string Day(int daysAgo = 0) => DateTime.UtcNow.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    private static Task<HttpResponseMessage> Post(VehicleWorld w, JsonObject v, string path, object body, HttpClient? client = null) =>
        (client ?? w.Admin).PostAsJsonAsync($"/api/vehicles/{Id(v)}/{path}", body);

    // ── Attached items ──────────────────────────────────────────────────────────────

    private async Task<System.Text.Json.JsonElement> AttachAsync(VehicleWorld w, JsonObject vehicle, string serial = "MSKU-123456", string? date = null, decimal? cost = 350000, int? supplier = null)
    {
        var response = await Post(w, vehicle, "items", new
        {
            itemTypeId = w.ItemType, description = "40 ft container", serialNo = serial, installationDate = date ?? Day(10), cost, condition = "Used", supplierId = supplier
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.DataAsync();
    }

    [Fact]
    public async Task An_item_is_attached_listed_and_carries_its_cost_only_for_those_who_may_see_it()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var body = await w.PartnerAsync("BodyMaker");
        var item = await AttachAsync(w, vehicle, supplier: body);

        Assert.Equal("Container", item.GetProperty("itemType").GetString());
        Assert.Equal("Attached", item.GetProperty("status").GetString());
        Assert.Equal(350000m, item.GetProperty("cost").GetDecimal());
        Assert.Equal(body, item.GetProperty("supplier").GetProperty("id").GetInt32());

        var viewer = w.As("Viewer", 8, PermissionCodes.VEH_VIEW);
        var listed = (await (await viewer.GetAsync($"/api/vehicles/{Id(vehicle)}/items")).DataAsync()).EnumerateArray().Single();
        Assert.Equal("MSKU-123456", listed.GetProperty("serialNo").GetString());
        Assert.False(listed.TryGetProperty("cost", out _));
    }

    [Fact]
    public async Task A_cost_entered_by_someone_who_may_not_see_cost_is_ignored()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var manager = w.As("Manager", 5, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_ITEM_MANAGE);

        var response = await Post(w, vehicle, "items", new { itemTypeId = w.ItemType, description = "AC unit", installationDate = Day(3), cost = 99999 }, manager);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        Assert.Null(factory.Scalar<decimal?>("SELECT Cost FROM veh.VehicleAttachedItems WHERE VehicleId = @i", ("@i", Id(vehicle))));
    }

    [Fact]
    public async Task An_item_is_checked_field_by_field_and_never_before_the_vehicles_acquisition()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync(acquired: Day(30));
        var workshop = await w.PartnerAsync("Workshop");

        var errors = await PartnerWorld.ErrorsAsync(await Post(w, vehicle, "items", new { itemTypeId = 0, description = "", installationDate = Day(-3), supplierId = workshop, condition = "Broken", warrantyUntil = Day(50) }));
        foreach (var field in new[] { "itemTypeId", "description", "installationDate", "supplierId", "condition", "warrantyUntil" }) Assert.Contains(errors, e => e.Field == field);
        Assert.Contains(errors, e => e is { Field: "supplierId", Code: Msg.VhCounterpartyLacksRole });   // a workshop is neither a vendor nor a body maker

        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "items", new { itemTypeId = w.ItemType, description = "Old", installationDate = Day(60) }), "installationDate", Msg.VhItemBeforeAcquisition);
    }

    [Fact]
    public async Task A_draft_vehicle_cannot_be_given_items()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var draft = await w.CreateAsync(w.Truck());

        await PartnerWorld.AssertRefusedAsync(await Post(w, draft, "items", new { itemTypeId = w.ItemType, description = "x", installationDate = Day(1) }), "status", Msg.VhWrongStatus);
    }

    [Fact]
    public async Task A_serial_number_is_on_one_attached_item_at_a_time_and_may_return_after_detaching()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var first = await w.ActiveAsync();
        var second = await w.ActiveAsync();
        var item = await AttachAsync(w, first, "cont-77");   // stored in capitals

        var clash = await Post(w, second, "items", new { itemTypeId = w.ItemType, description = "Same box", serialNo = "CONT-77", installationDate = Day(1) });
        await PartnerWorld.AssertRefusedAsync(clash, "serialNo", Msg.VhChassisOrEngineDuplicate);
        Assert.Contains((string)first["vehicleCode"]!, (await PartnerWorld.ErrorsAsync(clash)).Single().Message);

        var detach = await Post(w, first, $"items/{item.GetProperty("id").GetInt32()}/detach", new { date = Day(2), reason = "Sold the box" });
        Assert.True(detach.IsSuccessStatusCode, await detach.Content.ReadAsStringAsync());
        Assert.Equal("Detached", (await detach.DataAsync()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Post(w, second, "items", new { itemTypeId = w.ItemType, description = "Same box", serialNo = "CONT-77", installationDate = Day(1) })).StatusCode);
    }

    [Fact]
    public async Task Detaching_needs_a_reason_a_date_between_installation_and_today_and_only_works_once()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var item = await AttachAsync(w, vehicle, date: Day(10));
        var path = $"items/{item.GetProperty("id").GetInt32()}/detach";

        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, path, new { date = Day(2) }), "reason", Msg.Required);
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, path, new { date = Day(-2), reason = "x" }), "date", Msg.NotFuture);
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, path, new { date = Day(20), reason = "x" }), "date", Msg.Min);
        Assert.True((await Post(w, vehicle, path, new { date = Day(2), reason = "Worn" })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(w, vehicle, path, new { date = Day(1), reason = "Again" })).StatusCode);
        Assert.Empty((await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/items")).DataAsync()).EnumerateArray());
        Assert.Single((await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/items?includeGone=true")).DataAsync()).EnumerateArray());
    }

    [Fact]
    public async Task A_transfer_closes_the_item_on_one_vehicle_and_opens_it_on_the_other_with_the_same_serial_number()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var from = await w.ActiveAsync();
        var to = await w.ActiveAsync();
        var item = await AttachAsync(w, from, "TRANS-1", Day(20));
        var itemId = item.GetProperty("id").GetInt32();

        var response = await Post(w, from, $"items/{itemId}/transfer", new { targetVehicleId = Id(to), date = Day(3), reason = "Moved to the second truck" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var moved = await response.DataAsync();

        Assert.Equal(Id(to), moved.GetProperty("vehicleId").GetInt32());
        Assert.Equal("TRANS-1", moved.GetProperty("serialNo").GetString());
        Assert.Equal(itemId, moved.GetProperty("transferredFromItemId").GetInt32());   // the chain can be followed back
        var old = factory.Query("SELECT Status, TransferredToVehicleId, DetachedOn FROM veh.VehicleAttachedItems WHERE VehicleAttachedItemId = @i", ("@i", itemId)).Single();
        Assert.Equal(("Transferred", Id(to)), (old["Status"], old["TransferredToVehicleId"]));
        Assert.Empty((await (await w.Admin.GetAsync($"/api/vehicles/{Id(from)}/items")).DataAsync()).EnumerateArray());
        Assert.Single((await (await w.Admin.GetAsync($"/api/vehicles/{Id(to)}/items")).DataAsync()).EnumerateArray());
    }

    [Fact]
    public async Task A_transfer_needs_another_vehicle_in_the_fleet_and_a_sensible_date()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var from = await w.ActiveAsync();
        var draft = await w.CreateAsync(w.Truck());
        var item = (await AttachAsync(w, from, "TRANS-2", Day(20))).GetProperty("id").GetInt32();
        var path = $"items/{item}/transfer";

        await PartnerWorld.AssertRefusedAsync(await Post(w, from, path, new { targetVehicleId = Id(from) }), "targetVehicleId", Msg.Invalid);
        await PartnerWorld.AssertRefusedAsync(await Post(w, from, path, new { targetVehicleId = 999_999 }), "targetVehicleId", Msg.Invalid);
        await PartnerWorld.AssertRefusedAsync(await Post(w, from, path, new { targetVehicleId = Id(draft) }), "status", Msg.VhWrongStatus);
        var other = await w.ActiveAsync();
        await PartnerWorld.AssertRefusedAsync(await Post(w, from, path, new { targetVehicleId = Id(other), date = Day(40) }), "date", Msg.Min);
    }

    [Fact]
    public async Task Items_of_another_tenant_or_another_vehicle_are_not_found_and_managing_items_needs_its_permission()
    {
        var mine = await VehicleWorld.CreateAsync(factory);
        var other = await VehicleWorld.CreateAsync(factory);
        var vehicle = await mine.ActiveAsync();
        var theirs = await other.ActiveAsync();
        var theirItem = (await AttachAsync(other, theirs, "THEIRS-1")).GetProperty("id").GetInt32();
        var myOther = await mine.ActiveAsync();
        var editor = mine.As("Editor", 7, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_EDIT);

        Assert.Equal(HttpStatusCode.NotFound, (await Post(mine, vehicle, $"items/{theirItem}/detach", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(mine, myOther, $"items/{theirItem}/detach", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(mine, vehicle, "items", new { itemTypeId = mine.ItemType, description = "x" }, editor)).StatusCode);
    }

    // ── Odometer ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_reading_can_never_be_below_the_opening_reading()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var body = w.Truck(); body["openingOdometer"] = 45000; body["openingOdometerDate"] = Day(30);
        var vehicle = await w.ActiveAsync(body);

        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "odometer", new { km = 44000, readingDate = Day(5) }), "km", Msg.VhOdometerBelowOpening);
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "odometer", new { km = 46000, readingDate = Day(40) }), "readingDate", Msg.Min);
        Assert.True((await Post(w, vehicle, "odometer", new { km = 45000, readingDate = Day(20) })).IsSuccessStatusCode);   // equal is fine
    }

    [Fact]
    public async Task Readings_run_forward_with_the_dates_and_never_back_and_a_gap_is_filled_between_its_neighbours()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        Assert.True((await Post(w, vehicle, "odometer", new { km = 1000, readingDate = Day(30) })).IsSuccessStatusCode);
        Assert.True((await Post(w, vehicle, "odometer", new { km = 3000, readingDate = Day(10) })).IsSuccessStatusCode);

        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "odometer", new { km = 900, readingDate = Day(5) }), "km", Msg.VhOdometerOutOfSequence);      // lower than an earlier one
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "odometer", new { km = 3500, readingDate = Day(20) }), "km", Msg.VhOdometerOutOfSequence);   // higher than a later one
        Assert.True((await Post(w, vehicle, "odometer", new { km = 2000, readingDate = Day(20) })).IsSuccessStatusCode);                                            // fits between them

        var list = (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/odometer")).DataAsync()).EnumerateArray().Select(r => r.GetProperty("km").GetInt32()).ToList();
        Assert.Equal([3000, 2000, 1000], list);   // newest first
    }

    [Fact]
    public async Task A_reading_is_not_in_the_future_and_a_draft_takes_none_and_the_opening_odometer_locks_once_another_reading_rests_on_it()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var draft = await w.CreateAsync(w.Truck());
        var body = w.Truck(); body["openingOdometer"] = 500; body["openingOdometerDate"] = Day(30);
        var vehicle = await w.ActiveAsync(body);

        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "odometer", new { km = 600, readingDate = Day(-1) }), "readingDate", Msg.NotFuture);
        await PartnerWorld.AssertRefusedAsync(await Post(w, draft, "odometer", new { km = 600 }), "status", Msg.VhWrongStatus);

        vehicle["openingOdometer"] = 400;
        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(vehicle)).StatusCode);   // nothing rests on it yet: it follows
        Assert.Equal(400, factory.Scalar<int>("SELECT Km FROM veh.OdometerReadings WHERE VehicleId = @i AND Source = 'VehicleCreation'", ("@i", Id(vehicle))));

        await Post(w, vehicle, "odometer", new { km = 900, readingDate = Day(5) });
        vehicle = await w.GetAsync(Id(vehicle));
        vehicle["openingOdometer"] = 300;
        await PartnerWorld.AssertRefusedAsync(await w.PutAsync(vehicle), "openingOdometer", Msg.ReadOnlyOnceSaved);
    }

    // ── Default driver ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_driver_is_assigned_and_the_vehicle_and_the_assignment_agree()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var driver = await w.DriverAsync();
        var vehicle = await w.ActiveAsync();

        var response = await Post(w, vehicle, "driver", new { driverId = driver, effectiveDate = Day(2) });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var after = PartnerWorld.AsObject(await response.DataAsync());

        Assert.Equal(driver, after["defaultDriver"]!["id"]!.GetValue<int>());
        var row = factory.Query("SELECT DriverId, EffectiveFrom, EffectiveTo FROM veh.DriverAssignments WHERE VehicleId = @i", ("@i", Id(vehicle))).Single();
        Assert.Equal(driver, row["DriverId"]);
        Assert.Null(row["EffectiveTo"]);
    }

    [Fact]
    public async Task A_driver_who_already_has_a_vehicle_needs_a_yes_and_then_moves()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var driver = await w.DriverAsync();
        var first = await w.ActiveAsync();
        var second = await w.ActiveAsync();
        await Post(w, first, "driver", new { driverId = driver });

        var asked = await Post(w, second, "driver", new { driverId = driver });
        await PartnerWorld.AssertRefusedAsync(asked, "driverId", Msg.VhDriverAlreadyAssigned);
        Assert.Contains((string)first["registrationNo"]!, (await PartnerWorld.ErrorsAsync(asked)).Single().Message);
        Assert.NotNull((await w.GetAsync(Id(first)))["defaultDriver"]);   // nothing changed until the answer

        var moved = await Post(w, second, "driver", new { driverId = driver, releaseFromOther = true });
        Assert.True(moved.IsSuccessStatusCode, await moved.Content.ReadAsStringAsync());
        Assert.Null((await w.GetAsync(Id(first)))["defaultDriver"]);
        Assert.Equal(driver, (await w.GetAsync(Id(second)))["defaultDriver"]!["id"]!.GetValue<int>());
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM veh.DriverAssignments WHERE DriverId = @d AND EffectiveTo IS NULL", ("@d", driver)));
        Assert.Equal(2, factory.Scalar<int>("SELECT COUNT(*) FROM veh.DriverAssignments WHERE DriverId = @d", ("@d", driver)));   // the first is kept as history
    }

    [Fact]
    public async Task Giving_a_vehicle_another_driver_ends_the_first_assignment_and_the_same_driver_twice_is_refused()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var a = await w.DriverAsync();
        var b = await w.DriverAsync();
        var vehicle = await w.ActiveAsync();
        await Post(w, vehicle, "driver", new { driverId = a, effectiveDate = Day(10) });

        Assert.Equal(HttpStatusCode.Conflict, (await Post(w, vehicle, "driver", new { driverId = a })).StatusCode);
        Assert.True((await Post(w, vehicle, "driver", new { driverId = b, effectiveDate = Day(2) })).IsSuccessStatusCode);

        Assert.Equal(Day(2), factory.Scalar<DateTime>("SELECT EffectiveTo FROM veh.DriverAssignments WHERE VehicleId = @i AND DriverId = @d", ("@i", Id(vehicle)), ("@d", a)).ToString("yyyy-MM-dd"));
        Assert.Equal(b, (await w.GetAsync(Id(vehicle)))["defaultDriver"]!["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task Only_an_active_driver_partner_of_the_tenant_can_be_assigned_and_a_draft_takes_no_driver()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var draft = await w.CreateAsync(w.Truck());
        var workshop = await w.PartnerAsync("Workshop");
        var driver = await w.DriverAsync();
        var other = await VehicleWorld.CreateAsync(factory);
        var foreignDriver = await other.DriverAsync();

        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "driver", new { driverId = workshop }), "driverId", Msg.VhCounterpartyLacksRole);
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "driver", new { driverId = foreignDriver }), "driverId", Msg.Invalid);
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "driver", new { driverId = driver, effectiveDate = Day(-2) }), "effectiveDate", Msg.NotFuture);
        await PartnerWorld.AssertRefusedAsync(await Post(w, draft, "driver", new { driverId = driver }), "status", Msg.VhWrongStatus);   // FR-VH-001
        await w.Admin.PostAsJsonAsync($"/api/partners/{driver}/status", new { status = "Inactive" });
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "driver", new { driverId = driver }), "driverId", Msg.Invalid);
    }

    [Fact]
    public async Task A_driver_is_released_with_a_date_and_a_reason_and_the_permission_is_needed()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var driver = await w.DriverAsync();
        var vehicle = await w.ActiveAsync();
        var editor = w.As("Editor", 7, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_EDIT);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Admin.DeleteAsync($"/api/vehicles/{Id(vehicle)}/driver")).StatusCode);   // nobody to release
        await Post(w, vehicle, "driver", new { driverId = driver, effectiveDate = Day(10) });

        Assert.Equal(HttpStatusCode.Forbidden, (await editor.DeleteAsync($"/api/vehicles/{Id(vehicle)}/driver")).StatusCode);
        var response = await w.Admin.DeleteAsync($"/api/vehicles/{Id(vehicle)}/driver?date={Day(1)}&reason=Resigned");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        Assert.Null((await w.GetAsync(Id(vehicle)))["defaultDriver"]);
        Assert.Equal("Resigned", factory.Scalar<string>("SELECT EndReason FROM veh.DriverAssignments WHERE VehicleId = @i", ("@i", Id(vehicle))));
    }

    [Fact]
    public async Task Six_vehicles_asking_for_the_same_driver_at_once_leave_him_with_one()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var driver = await w.DriverAsync();
        var vehicles = new List<JsonObject>();
        for (var n = 0; n < 6; n++) vehicles.Add(await w.ActiveAsync());

        var results = await Task.WhenAll(vehicles.Select(v => Post(w, v, "driver", new { driverId = driver, releaseFromOther = true })));

        Assert.All(results, r => Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode));
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM veh.DriverAssignments WHERE DriverId = @d AND EffectiveTo IS NULL", ("@d", driver)));
    }

    // ── The partner's linked vehicles (S1-BP-16) ────────────────────────────────────

    [Fact]
    public async Task A_partner_sees_every_vehicle_it_is_linked_to_past_and_present_and_how()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var driver = await w.DriverAsync();
        var vendor = await w.PartnerAsync("Vendor");
        var first = await w.ActiveAsync(w.Truck("LNK-1"));
        var second = await w.ActiveAsync(w.Truck("LNK-2"));

        await Post(w, first, "driver", new { driverId = driver, effectiveDate = Day(20) });
        await Post(w, second, "driver", new { driverId = driver, effectiveDate = Day(5), releaseFromOther = true });   // the first becomes history
        await Post(w, second, "category", new { category = "Rented", effectiveDate = Day(10), details = new { counterpartyId = vendor, rentAmount = 1, rentFrequency = "Weekly" } });
        await AttachAsync(w, first, "LNK-BOX", supplier: vendor);

        var asDriver = (await (await w.Admin.GetAsync($"/api/partners/{driver}/vehicles")).DataAsync()).EnumerateArray().ToList();
        Assert.Equal(["LNK-2", "LNK-1"], asDriver.Select(l => l.GetProperty("registrationNo").GetString()));   // current first
        Assert.True(asDriver[0].GetProperty("isCurrent").GetBoolean());
        Assert.False(asDriver[1].GetProperty("isCurrent").GetBoolean());
        Assert.All(asDriver, l => Assert.Equal("Driver", l.GetProperty("link").GetString()));

        var asVendor = (await (await w.Admin.GetAsync($"/api/partners/{vendor}/vehicles")).DataAsync()).EnumerateArray().Select(l => (l.GetProperty("registrationNo").GetString(), l.GetProperty("link").GetString(), l.GetProperty("detail").GetString())).ToList();
        Assert.Contains(("LNK-2", "Owner", "Rented"), asVendor);
        Assert.Contains(("LNK-1", "Supplier", "40 ft container"), asVendor);
    }

    [Fact]
    public async Task Linked_vehicles_need_the_vehicle_view_permission_and_a_partner_of_the_tenant()
    {
        var mine = await VehicleWorld.CreateAsync(factory);
        var other = await VehicleWorld.CreateAsync(factory);
        var driver = await mine.DriverAsync();
        var theirs = await other.DriverAsync();
        var partnerOnly = mine.As("Partner Viewer", 4, PermissionCodes.BP_VIEW);

        Assert.Equal(HttpStatusCode.Forbidden, (await partnerOnly.GetAsync($"/api/partners/{driver}/vehicles")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mine.Admin.GetAsync($"/api/partners/{theirs}/vehicles")).StatusCode);
        Assert.Empty((await (await mine.Admin.GetAsync($"/api/partners/{driver}/vehicles")).DataAsync()).EnumerateArray());
    }

    // ── Export (S2-VHU-16) ──────────────────────────────────────────────────────────

    private static async Task<ClosedXML.Excel.IXLWorksheet> ExportAsync(HttpClient client, string query = "", string? zone = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/vehicles/export" + query);
        if (zone is not null) request.Headers.Add("X-Time-Zone", zone);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);
        Assert.Matches(@"^vehicles-\d{8}-\d{4}\.xlsx$", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        return new ClosedXML.Excel.XLWorkbook(await response.Content.ReadAsStreamAsync()).Worksheet(1);
    }

    [Fact]
    public async Task The_export_holds_every_vehicle_the_filter_matches_not_one_page_and_reads_names_not_ids()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        for (var n = 0; n < 27; n++) await w.CreateAsync(w.Truck());
        var driver = await w.DriverAsync();
        var active = await w.ActiveAsync(w.Truck("EXP-1"));
        await Post(w, active, "driver", new { driverId = driver });

        var all = await ExportAsync(w.Admin, "?page=2&pageSize=5");   // paging is ignored
        var activeOnly = await ExportAsync(w.Admin, "?status=Active");

        Assert.Equal(28, all.RowsUsed().Count() - 1);
        Assert.Equal(1, activeOnly.RowsUsed().Count() - 1);
        var headers = activeOnly.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Equal("EXP-1", activeOnly.Cell(2, headers.IndexOf("Registration") + 1).GetString());
        Assert.Equal("Truck", activeOnly.Cell(2, headers.IndexOf("Type") + 1).GetString());
        Assert.False(string.IsNullOrEmpty(activeOnly.Cell(2, headers.IndexOf("Driver") + 1).GetString()));
    }

    [Fact]
    public async Task The_export_shows_times_in_the_exporters_zone_keeps_typed_text_as_text_and_needs_its_permission()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        await w.CreateAsync(w.Truck("=SUM(1,1)"));
        var stored = factory.Scalar<DateTime>("SELECT ModifiedOn FROM veh.Vehicles WHERE TenantId = @t", ("@t", w.Tenant));

        var karachi = await ExportAsync(w.Admin, zone: "Asia/Karachi");
        var utc = await ExportAsync(w.Admin, zone: "UTC");

        var headers = karachi.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        var when = headers.FindIndex(h => h.StartsWith("Last modified")) + 1;
        Assert.Contains("Asia/Karachi", karachi.Cell(1, when).GetString());
        Assert.Equal(stored.AddHours(5).ToString("yyyy-MM-dd HH:mm"), karachi.Cell(2, when).GetDateTime().ToString("yyyy-MM-dd HH:mm"));
        Assert.Equal(stored.ToString("yyyy-MM-dd HH:mm"), utc.Cell(2, when).GetDateTime().ToString("yyyy-MM-dd HH:mm"));
        var reg = karachi.Cell(2, headers.IndexOf("Registration") + 1);
        Assert.False(reg.HasFormula);
        Assert.Equal(ClosedXML.Excel.XLDataType.Text, reg.DataType);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.As("Viewer", 5, PermissionCodes.VEH_VIEW).GetAsync("/api/vehicles/export")).StatusCode);
    }
    // ── The list ────────────────────────────────────────────────────────────────────

    private static async Task<(List<System.Text.Json.JsonElement> Items, int Total)> ListAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/vehicles" + query);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var data = await response.DataAsync();
        return (data.GetProperty("items").EnumerateArray().ToList(), data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task The_list_pages_25_at_a_time_newest_change_first_and_shows_names_not_ids()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var other = await VehicleWorld.CreateAsync(factory);
        await other.CreateAsync(other.Truck());
        var driver = await w.DriverAsync();
        for (var n = 0; n < 26; n++) await w.CreateAsync(w.Truck());
        var last = w.Truck("LAST-1"); last["defaultDriverId"] = driver;
        await w.CreateAsync(last);

        var (first, total) = await ListAsync(w.Admin);
        var (second, _) = await ListAsync(w.Admin, "?page=2");

        Assert.Equal(27, total);
        Assert.Equal((25, 2), (first.Count, second.Count));
        Assert.Equal("LAST-1", first[0].GetProperty("registrationNo").GetString());
        Assert.Equal("Truck", first[0].GetProperty("vehicleType").GetString());
        Assert.Equal("Hino", first[0].GetProperty("make").GetString());
        Assert.Equal(driver, first[0].GetProperty("driver").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task The_search_finds_by_registration_with_or_without_dashes_chassis_engine_and_code_from_three_characters()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var body = w.Truck("KHI-4455"); body["chassisNo"] = "CHS-ZZ99887"; body["engineNo"] = "ENG-QQ11223";
        var vehicle = await w.CreateAsync(body);
        await w.CreateAsync(w.Truck());

        async Task<List<string>> Find(string term) => (await ListAsync(w.Admin, "?search=" + Uri.EscapeDataString(term))).Items.Select(i => i.GetProperty("registrationNo").GetString()!).ToList();

        Assert.Equal(["KHI-4455"], await Find("khi-4455"));
        Assert.Equal(["KHI-4455"], await Find("KHI 445"));
        Assert.Equal(["KHI-4455"], await Find("zz998"));
        Assert.Equal(["KHI-4455"], await Find("qq112"));
        Assert.Equal(["KHI-4455"], await Find(vehicle["vehicleCode"]!.GetValue<string>()));
        Assert.Empty(await Find("NOPE-000"));
        await PartnerWorld.AssertRefusedAsync(await w.Admin.GetAsync("/api/vehicles?search=ab"), "search", Msg.MinLength);
    }

    [Fact]
    public async Task The_list_filters_by_status_category_type_make_and_driver_and_sorts_on_the_columns_offered()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var draft = await w.CreateAsync(w.Pickup("BBB-2"));
        var active = await w.ActiveAsync(w.Truck("CCC-3"));
        var driver = await w.DriverAsync();
        await Post(w, active, "driver", new { driverId = driver });
        var rented = await w.ActiveAsync(w.Truck("AAA-1"));
        var vendor = await w.PartnerAsync("Vendor");
        await Post(w, rented, "category", new { category = "Rented", effectiveDate = Day(5), details = new { counterpartyId = vendor, rentAmount = 1, rentFrequency = "Weekly" } });

        Assert.Equal(1, (await ListAsync(w.Admin, "?status=Draft")).Total);
        Assert.Equal(2, (await ListAsync(w.Admin, "?status=Active")).Total);
        Assert.Equal(3, (await ListAsync(w.Admin, "?status=Active&status=Draft")).Total);
        Assert.Equal(1, (await ListAsync(w.Admin, "?category=Rented")).Total);
        Assert.Equal(2, (await ListAsync(w.Admin, $"?vehicleTypeId={w.TruckType}")).Total);
        Assert.Equal(1, (await ListAsync(w.Admin, $"?vehicleTypeId={w.PickupType}")).Total);
        Assert.Equal(3, (await ListAsync(w.Admin, $"?makeId={w.Make}")).Total);
        Assert.Equal(["CCC-3"], (await ListAsync(w.Admin, $"?driverId={driver}")).Items.Select(i => i.GetProperty("registrationNo").GetString()).ToList());
        Assert.Equal(["AAA-1", "BBB-2", "CCC-3"], (await ListAsync(w.Admin, "?sort=registrationNo,asc")).Items.Select(i => i.GetProperty("registrationNo").GetString()).ToList());
        Assert.Equal(["CCC-3", "BBB-2", "AAA-1"], (await ListAsync(w.Admin, "?sort=registrationNo,desc")).Items.Select(i => i.GetProperty("registrationNo").GetString()).ToList());
        Assert.Equal(HttpStatusCode.OK, (await w.Admin.GetAsync("/api/vehicles?sort=RegNoKey;DROP TABLE x,asc")).StatusCode);   // an unknown column is not run
        Assert.NotNull(draft);
    }

    [Fact]
    public async Task The_list_needs_the_view_permission_and_shows_nothing_of_another_tenant()
    {
        var mine = await VehicleWorld.CreateAsync(factory);
        var other = await VehicleWorld.CreateAsync(factory);
        await other.CreateAsync(other.Truck("HIDDEN-1"));

        Assert.Equal(0, (await ListAsync(mine.Admin)).Total);
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.As("Nobody", 9, PermissionCodes.VEH_CREATE).GetAsync("/api/vehicles")).StatusCode);
    }
}
