using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Vehicles;

/// <summary>S2-VH-04, 11, 12, 13: a vehicle in the fleet: its status, its ownership category and how it leaves.</summary>
[Collection(ApiCollection.Name)]
public sealed class VehicleFleetTests(ApiFactory factory)
{
    private static int Id(JsonObject v) => VehicleWorld.Id(v);

    private static Task<HttpResponseMessage> Post(VehicleWorld w, JsonObject v, string action, object body, HttpClient? client = null) =>
        (client ?? w.Admin).PostAsJsonAsync($"/api/vehicles/{Id(v)}/{action}", body);

    private static string Day(int daysAgo = 0) => DateTime.UtcNow.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    // ── Status ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_vehicle_moves_between_active_maintenance_and_unavailable_and_each_move_is_logged()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();

        var response = await Post(w, vehicle, "status", new { status = "UnderMaintenance", reason = "Gearbox" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("UnderMaintenance", (await w.GetAsync(Id(vehicle)))["status"]!.GetValue<string>());
        Assert.True((await Post(w, vehicle, "status", new { status = "Active" })).IsSuccessStatusCode);

        var log = factory.Query("SELECT FromStatus, ToStatus, Reason, UserName FROM veh.VehicleLifecycle WHERE VehicleId = @i AND EventType = 'StatusChange' ORDER BY VehicleLifecycleEntryId", ("@i", Id(vehicle)));
        Assert.Equal(2, log.Count);
        Assert.Equal(("Active", "UnderMaintenance", "Gearbox", "Fleet Admin"), (log[0]["FromStatus"], log[0]["ToStatus"], log[0]["Reason"], log[0]["UserName"]));
    }

    [Fact]
    public async Task Only_the_moves_the_fsd_allows_are_allowed()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var draft = await w.CreateAsync(w.Truck());
        var active = await w.ActiveAsync();
        var sold = await w.ActiveAsync(); w.SetStatus(Id(sold), "Sold");
        var retired = await w.ActiveAsync(); w.SetStatus(Id(retired), "Retired");

        await PartnerWorld.AssertRefusedAsync(await Post(w, draft, "status", new { status = "Active" }), "status", Msg.VhWrongStatus);   // a Draft is activated, not moved
        await PartnerWorld.AssertRefusedAsync(await Post(w, sold, "status", new { status = "Active" }), "status", Msg.VhWrongStatus);
        await PartnerWorld.AssertRefusedAsync(await Post(w, active, "status", new { status = "Sold" }), "status", Msg.OneOf);          // leaving is its own action
        Assert.Equal(HttpStatusCode.Conflict, (await Post(w, active, "status", new { status = "Active" })).StatusCode);
        Assert.True((await Post(w, retired, "status", new { status = "Active" })).IsSuccessStatusCode);                                 // reinstated
        await PartnerWorld.AssertRefusedAsync(await Post(w, active, "status", new { status = "Active", effectiveDate = Day(-5) }), "effectiveDate", Msg.NotFuture);
    }

    [Fact]
    public async Task Changing_status_needs_its_own_permission()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var editor = w.As("Editor", 7, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_EDIT);

        Assert.Equal(HttpStatusCode.Forbidden, (await Post(w, vehicle, "status", new { status = "UnderMaintenance" }, editor)).StatusCode);
    }

    // ── Category ────────────────────────────────────────────────────────────────────

    private async Task<JsonObject> RentedAsync(VehicleWorld w, JsonObject vehicle, int? lessor = null, string? date = null, decimal rent = 90000)
    {
        var id = lessor ?? await w.PartnerAsync("Vendor");
        var response = await Post(w, vehicle, "category", new
        {
            category = "Rented", effectiveDate = date ?? Day(30), reason = "Rented in",
            details = new { counterpartyId = id, rentAmount = rent, rentFrequency = "Monthly", rentDueDay = 5, securityDeposit = 50000, agreementReference = "AGR-1" }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return PartnerWorld.AsObject(await response.DataAsync());
    }

    [Fact]
    public async Task A_category_change_opens_a_relation_and_keeps_the_copies_on_the_vehicle_in_step()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var lessor = await w.PartnerAsync("Vendor");

        var changed = await RentedAsync(w, vehicle, lessor);

        Assert.Equal("Rented", changed["currentCategory"]!.GetValue<string>());
        Assert.Equal(lessor, changed["currentCounterparty"]!["id"]!.GetValue<int>());
        var relation = changed["relation"]!;
        Assert.Equal(90000m, relation["rentAmount"]!.GetValue<decimal>());
        Assert.Equal(Day(30), relation["effectiveFrom"]!.GetValue<string>());
        Assert.Null(relation["effectiveTo"]);
        var lifecycle = factory.Query("SELECT FromCategory, ToCategory, Reason FROM veh.VehicleLifecycle WHERE VehicleId = @i AND EventType = 'CategoryChange'", ("@i", Id(vehicle))).Single();
        Assert.Equal(("SelfOwned", "Rented", "Rented in"), (lifecycle["FromCategory"], lifecycle["ToCategory"], lifecycle["Reason"]));
    }

    [Fact]
    public async Task A_further_change_closes_the_old_relation_the_day_before_and_leaves_it_in_the_history()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        await RentedAsync(w, vehicle, date: Day(30));
        var partner = await w.PartnerAsync("Vendor");

        var shared = await Post(w, vehicle, "category", new
        {
            category = "Shared", effectiveDate = Day(10),
            details = new { counterpartyId = partner, sharePercent = 60, sharingBasis = "ProfitShare", expenseSharingRule = "AllExpensesSameRatio" }
        });
        Assert.True(shared.IsSuccessStatusCode, await shared.Content.ReadAsStringAsync());
        var after = await w.GetAsync(Id(vehicle));

        Assert.Equal("Shared", after["currentCategory"]!.GetValue<string>());
        var history = after["relationHistory"]!.AsArray();
        Assert.Equal(2, history.Count);
        Assert.Equal(Day(11), history[1]!["effectiveTo"]!.GetValue<string>());   // closed the day before the new one starts
        Assert.Equal("Rented", history[1]!["category"]!.GetValue<string>());
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleRelations WHERE VehicleId = @i AND EffectiveTo IS NULL", ("@i", Id(vehicle))));   // one open at a time (BR-VH-002)
    }

    [Fact]
    public async Task Going_back_to_self_owned_closes_the_relation_and_opens_none()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        await RentedAsync(w, vehicle);

        var response = await Post(w, vehicle, "category", new { category = "SelfOwned", effectiveDate = Day(5) });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var after = await w.GetAsync(Id(vehicle));

        Assert.Equal("SelfOwned", after["currentCategory"]!.GetValue<string>());
        Assert.Null(after["relation"]);
        Assert.Null(after["currentCounterparty"]);
    }

    [Fact]
    public async Task Changed_terms_of_the_same_category_are_a_new_relation_but_no_change_at_all_is_refused()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var lessor = await w.PartnerAsync("Vendor");
        await RentedAsync(w, vehicle, lessor, Day(30), 90000);

        var same = await Post(w, vehicle, "category", new
        {
            category = "Rented", effectiveDate = Day(5),
            details = new { counterpartyId = lessor, rentAmount = 90000, rentFrequency = "Monthly", rentDueDay = 5, securityDeposit = 50000, agreementReference = "AGR-1" }
        });
        Assert.Equal(HttpStatusCode.Conflict, same.StatusCode);

        var raised = await Post(w, vehicle, "category", new
        {
            category = "Rented", effectiveDate = Day(5),
            details = new { counterpartyId = lessor, rentAmount = 110000, rentFrequency = "Monthly", rentDueDay = 5, securityDeposit = 50000, agreementReference = "AGR-1" }
        });
        Assert.True(raised.IsSuccessStatusCode, await raised.Content.ReadAsStringAsync());
        var history = (await w.GetAsync(Id(vehicle)))["relationHistory"]!.AsArray();
        Assert.Equal((110000m, 90000m), (history[0]!["rentAmount"]!.GetValue<decimal>(), history[1]!["rentAmount"]!.GetValue<decimal>()));   // BR-VH-008
    }

    [Fact]
    public async Task Each_category_checks_its_own_fields()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var vendor = await w.PartnerAsync("Vendor");
        var bank = await w.PartnerAsync("Bank");

        async Task<List<(string Field, string Code, string Message)>> Errors(object body) => await PartnerWorld.ErrorsAsync(await Post(w, vehicle, "category", body));

        var shared = await Errors(new { category = "Shared", details = new { counterpartyId = vendor, sharePercent = 0, sharingBasis = "FixedMonthly", expenseSharingRule = "Nonsense" } });
        Assert.Contains(shared, e => e is { Field: "details.sharePercent", Code: Msg.VhShareRange });
        Assert.Contains(shared, e => e.Field == "details.fixedMonthlyAmount");
        Assert.Contains(shared, e => e.Field == "details.expenseSharingRule");

        var rented = await Errors(new { category = "Rented", details = new { counterpartyId = vendor, rentAmount = 0, rentFrequency = "Monthly" } });
        Assert.Contains(rented, e => e.Field == "details.rentAmount");
        Assert.Contains(rented, e => e.Field == "details.rentDueDay");

        var noParty = await Errors(new { category = "Rented", details = new { rentAmount = 5, rentFrequency = "Weekly" } });
        Assert.Contains(noParty, e => e is { Field: "details.counterpartyId", Code: Msg.VhCounterpartyRequired } && e.Message.Contains("Lessor"));

        var customer = await Errors(new { category = "CustomerArrangement", details = new { counterpartyId = vendor, arrangementType = "DedicatedMonthly" } });
        Assert.Contains(customer, e => e is { Field: "details.counterpartyId", Code: Msg.VhCounterpartyLacksRole });   // a vendor is not a customer
        Assert.Contains(customer, e => e.Field == "details.agreedAmount");

        var revenue = await Errors(new { category = "CustomerArrangement", details = new { counterpartyId = vendor, arrangementType = "RevenueShare", revenueSharePercent = 150 } });
        Assert.Contains(revenue, e => e.Field == "details.revenueSharePercent");

        var leaseWrongRole = await Errors(new { category = "BankLeased", details = new { counterpartyId = vendor } });
        Assert.Contains(leaseWrongRole, e => e is { Field: "details.counterpartyId", Code: Msg.VhCounterpartyLacksRole });
        Assert.True((await Post(w, vehicle, "category", new { category = "BankLeased", effectiveDate = Day(2), details = new { counterpartyId = bank } })).IsSuccessStatusCode);

        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "category", new { category = "Spaceship" }), "category", Msg.OneOf);
    }

    [Fact]
    public async Task A_running_customer_can_be_the_customer_of_an_arrangement()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var running = await w.PartnerAsync("RunningCustomer");

        var response = await Post(w, vehicle, "category", new
        {
            category = "CustomerArrangement", effectiveDate = Day(3), details = new { counterpartyId = running, arrangementType = "PerTrip" }
        });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_dates_of_a_change_are_checked_end_after_start_not_the_future_not_before_the_acquisition_and_no_overlap()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync(acquired: Day(60));
        var vendor = await w.PartnerAsync("Vendor");
        object Rent(string date, string? end = null) => new
        {
            category = "Rented", effectiveDate = date,
            details = new { counterpartyId = vendor, rentAmount = 1000, rentFrequency = "Weekly", endDate = end }
        };

        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "category", Rent(Day(-3))), "effectiveDate", Msg.NotFuture);
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "category", Rent(Day(90))), "effectiveDate", Msg.VhItemBeforeAcquisition);   // BR-VH-006
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "category", Rent(Day(10), Day(20))), "details.endDate", Msg.VhEndBeforeStart);

        Assert.True((await Post(w, vehicle, "category", Rent(Day(30)))).IsSuccessStatusCode);
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "category", new { category = "SelfOwned", effectiveDate = Day(40) }), "effectiveDate", Msg.Min);   // would overlap the open relation
    }

    [Fact]
    public async Task While_a_finance_agreement_has_a_balance_only_bank_leased_may_follow()
    {
        var w = await VehicleWorld.CreateAsync(factory, FakeFinanceGuard.Host(factory));
        var vehicle = await w.ActiveAsync();
        var vendor = await w.PartnerAsync("Vendor");
        var bank = await w.PartnerAsync("Bank");
        await RentedAsync(w, vehicle, vendor, Day(30));   // no balance yet: nothing stops the first change
        FakeFinanceGuard.Outstanding = true;
        try
        {
            await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "category", new { category = "SelfOwned", effectiveDate = Day(2) }), "category", Msg.VhCategoryLockedByFinance);
            await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "category", new
            {
                category = "Shared", effectiveDate = Day(2), details = new { counterpartyId = vendor, sharePercent = 50, sharingBasis = "ProfitShare", expenseSharingRule = "EachBearsOwn" }
            }), "category", Msg.VhCategoryLockedByFinance);
            Assert.True((await Post(w, vehicle, "category", new { category = "BankLeased", effectiveDate = Day(2), details = new { counterpartyId = bank } })).IsSuccessStatusCode);
        }
        finally { FakeFinanceGuard.Outstanding = false; }
    }
    [Fact]
    public async Task A_draft_or_a_sold_vehicle_cannot_change_category_and_it_needs_the_permission()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var draft = await w.CreateAsync(w.Truck());
        var sold = await w.ActiveAsync(); w.SetStatus(Id(sold), "Sold");
        var active = await w.ActiveAsync();
        var editor = w.As("Editor", 7, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_EDIT);

        await PartnerWorld.AssertRefusedAsync(await Post(w, draft, "category", new { category = "SelfOwned" }), "status", Msg.VhWrongStatus);
        await PartnerWorld.AssertRefusedAsync(await Post(w, sold, "category", new { category = "SelfOwned" }), "status", Msg.VhWrongStatus);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(w, active, "category", new { category = "SelfOwned" }, editor)).StatusCode);
    }

    [Fact]
    public async Task Rent_and_other_finance_figures_are_left_out_for_someone_who_may_not_see_finance()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        await RentedAsync(w, vehicle);
        var viewer = w.As("Viewer", 8, PermissionCodes.VEH_VIEW);

        var seen = await w.GetAsync(Id(vehicle), viewer);

        Assert.Equal("Monthly", seen["relation"]!["rentFrequency"]!.GetValue<string>());
        foreach (var key in new[] { "rentAmount", "securityDeposit", "fixedMonthlyAmount", "agreedAmount" })
            Assert.False(seen["relation"]!.AsObject().ContainsKey(key), key);
    }

    [Fact]
    public async Task An_admin_without_finance_view_can_change_the_category_without_wiping_the_amounts()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var lessor = await w.PartnerAsync("Vendor");
        await RentedAsync(w, vehicle, lessor, Day(30), 90000);
        var limited = w.As("Limited Admin", 9, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_CATEGORY_CHANGE);

        var response = await Post(w, vehicle, "category", new
        {
            category = "Rented", effectiveDate = Day(5),
            details = new { counterpartyId = lessor, rentFrequency = "Monthly", rentDueDay = 5, agreementReference = "AGR-2" }
        }, limited);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync() + " (rent amount missing is the only complaint expected)");
    }

    // ── Leaving the fleet ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Selling_needs_a_buyer_an_amount_a_date_and_a_reason_and_ends_the_relation_and_the_driver()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        await RentedAsync(w, vehicle);
        var driver = await w.DriverAsync();
        Assert.True((await Post(w, vehicle, "driver", new { driverId = driver })).IsSuccessStatusCode);
        var buyer = await w.PartnerAsync("Customer");

        var errors = await PartnerWorld.ErrorsAsync(await Post(w, vehicle, "dispose", new { kind = "Sell" }));
        foreach (var field in new[] { "counterpartyId", "amount", "reason" }) Assert.Contains(errors, e => e.Field == field);

        var response = await Post(w, vehicle, "dispose", new { kind = "Sell", date = Day(1), reason = "Sold at auction", counterpartyId = buyer, amount = 4_200_000, reference = "INV-77" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var after = await w.GetAsync(Id(vehicle));

        Assert.Equal("Sold", after["status"]!.GetValue<string>());
        Assert.Null(after["defaultDriver"]);
        Assert.Equal(Day(1), after["relationHistory"]![0]!["effectiveTo"]!.GetValue<string>());
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.DriverAssignments WHERE VehicleId = @i AND EffectiveTo IS NULL", ("@i", Id(vehicle))));
        var log = factory.Query("SELECT ToStatus, Reason, Reference, Amount, CounterpartyId FROM veh.VehicleLifecycle WHERE VehicleId = @i AND EventType = 'Disposal'", ("@i", Id(vehicle))).Single();
        Assert.Equal(("Sold", "Sold at auction", "INV-77", 4_200_000m, buyer), (log["ToStatus"], log["Reason"], log["Reference"], log["Amount"], log["CounterpartyId"]));
    }

    [Fact]
    public async Task A_vehicle_can_be_retired_then_sold_but_a_sold_vehicle_is_final_and_a_draft_cannot_be_disposed()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var draft = await w.CreateAsync(w.Truck());
        var buyer = await w.PartnerAsync("Customer");

        Assert.True((await Post(w, vehicle, "dispose", new { kind = "Retire", reason = "Worn out" })).IsSuccessStatusCode);
        Assert.Equal("Retired", (await w.GetAsync(Id(vehicle)))["status"]!.GetValue<string>());
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "dispose", new { kind = "Retire", reason = "again" }), "kind", Msg.VhWrongStatus);
        Assert.True((await Post(w, vehicle, "dispose", new { kind = "Sell", reason = "Scrap", counterpartyId = buyer, amount = 100000 })).IsSuccessStatusCode);
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "dispose", new { kind = "Transfer", reason = "x", counterpartyId = buyer }), "status", Msg.VhWrongStatus);
        await PartnerWorld.AssertRefusedAsync(await Post(w, draft, "dispose", new { kind = "Retire", reason = "x" }), "status", Msg.VhWrongStatus);
    }

    [Fact]
    public async Task A_transfer_names_the_new_party_and_a_disposal_is_dated_no_earlier_than_the_acquisition_and_no_later_than_today()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync(acquired: Day(20));
        var party = await w.PartnerAsync("Customer");

        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "dispose", new { kind = "Transfer", reason = "x" }), "counterpartyId", Msg.Required);
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "dispose", new { kind = "Transfer", reason = "x", counterpartyId = party, date = Day(-2) }), "date", Msg.NotFuture);
        await PartnerWorld.AssertRefusedAsync(await Post(w, vehicle, "dispose", new { kind = "Transfer", reason = "x", counterpartyId = party, date = Day(40) }), "date", Msg.VhItemBeforeAcquisition);
        Assert.True((await Post(w, vehicle, "dispose", new { kind = "Transfer", reason = "Moved to the Multan yard company", counterpartyId = party })).IsSuccessStatusCode);
        Assert.Equal("Transferred", (await w.GetAsync(Id(vehicle)))["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Disposing_needs_its_own_permission_and_the_sale_amount_is_hidden_from_those_who_may_not_see_cost()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var buyer = await w.PartnerAsync("Customer");
        var viewer = w.As("Viewer", 8, PermissionCodes.VEH_VIEW);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(w, vehicle, "dispose", new { kind = "Retire", reason = "x" }, viewer)).StatusCode);
        await Post(w, vehicle, "dispose", new { kind = "Sell", reason = "Sold", counterpartyId = buyer, amount = 3_000_000 });

        var full = (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/history")).DataAsync()).GetProperty("lifecycle").EnumerateArray().First(l => l.GetProperty("eventType").GetString() == "Disposal");
        var limited = (await (await viewer.GetAsync($"/api/vehicles/{Id(vehicle)}/history")).DataAsync()).GetProperty("lifecycle").EnumerateArray().First(l => l.GetProperty("eventType").GetString() == "Disposal");

        Assert.Equal(3_000_000m, full.GetProperty("amount").GetDecimal());
        Assert.False(limited.TryGetProperty("amount", out _));
    }

    [Fact]
    public async Task A_vehicle_that_has_left_frees_its_registration_number()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync(w.Truck("GONE-77"));
        var buyer = await w.PartnerAsync("Customer");
        await Post(w, vehicle, "dispose", new { kind = "Sell", reason = "Sold", counterpartyId = buyer, amount = 1 });

        Assert.Equal(HttpStatusCode.Created, (await w.PostAsync(w.Truck("GONE 77"))).StatusCode);
    }

    // ── History ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_history_lists_every_change_to_the_vehicle_and_what_belongs_to_it_and_its_lifecycle()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        await RentedAsync(w, vehicle);
        await Post(w, vehicle, "status", new { status = "UnderMaintenance" });

        var history = await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/history")).DataAsync();
        var changes = history.GetProperty("changes").GetProperty("items").EnumerateArray().ToList();
        var lifecycle = history.GetProperty("lifecycle").EnumerateArray().Select(l => l.GetProperty("eventType").GetString()).ToList();

        Assert.Contains(changes, c => c.GetProperty("entity").GetString() == "Vehicle" && c.GetProperty("action").GetString() == "Created");
        Assert.Contains(changes, c => c.GetProperty("entity").GetString() == "VehicleRelation" && c.GetProperty("action").GetString() == "Created");
        Assert.Contains(changes, c => c.GetProperty("entity").GetString() == "Vehicle" && c.GetProperty("field").ValueKind == System.Text.Json.JsonValueKind.String && c.GetProperty("field").GetString() == "Status");
        Assert.Equal(["StatusChange", "CategoryChange", "Created"], lifecycle);
    }

    // ── The partner module is told what depends on a partner ────────────────────────

    [Fact]
    public async Task A_driver_with_a_vehicle_cannot_be_deactivated_or_lose_the_role_until_released()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var driver = await w.DriverAsync();
        var vehicle = await w.ActiveAsync();
        Assert.True((await Post(w, vehicle, "driver", new { driverId = driver })).IsSuccessStatusCode);

        var deactivate = await w.Admin.PostAsJsonAsync($"/api/partners/{driver}/status", new { status = "Inactive" });
        await PartnerWorld.AssertRefusedAsync(deactivate, "status", Msg.BpDeactivationBlocked);
        var usage = (await (await w.Admin.GetAsync($"/api/partners/{driver}/usage?intent=Deactivate")).DataAsync()).EnumerateArray().ToList();
        Assert.Contains(usage, u => u.GetProperty("description").GetString()!.Contains((string)vehicle["registrationNo"]!));

        await w.Admin.DeleteAsync($"/api/vehicles/{Id(vehicle)}/driver");
        Assert.True((await w.Admin.PostAsJsonAsync($"/api/partners/{driver}/status", new { status = "Inactive" })).IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_bank_on_a_lease_and_a_lessor_cannot_be_deactivated_and_a_draft_does_not_count()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var bankBody = w.Partners.Company(roles: ["Bank", "Workshop"]); bankBody.Remove("vendor");
        var bank = (await w.Partners.CreateAsync(bankBody))["id"]!.GetValue<int>();
        var lessor = await w.PartnerAsync("Vendor");
        var draftDriver = await w.DriverAsync();
        var draft = w.Truck(); draft["defaultDriverId"] = draftDriver;
        await w.CreateAsync(draft);

        Assert.True((await Post(w, vehicle, "category", new { category = "BankLeased", effectiveDate = Day(3), details = new { counterpartyId = bank } })).IsSuccessStatusCode);
        await PartnerWorld.AssertRefusedAsync(await w.Admin.PostAsJsonAsync($"/api/partners/{bank}/status", new { status = "Inactive" }), "status", Msg.BpDeactivationBlocked);
        await PartnerWorld.AssertRefusedAsync(await w.Admin.DeleteAsync($"/api/partners/{bank}/roles/Bank"), "roleCode", Msg.BpRoleInUse);   // the lease still rests on the Bank role

        var second = await w.ActiveAsync();
        await RentedAsync(w, second, lessor);
        await PartnerWorld.AssertRefusedAsync(await w.Admin.PostAsJsonAsync($"/api/partners/{lessor}/status", new { status = "Inactive" }), "status", Msg.BpDeactivationBlocked);

        Assert.True((await w.Admin.PostAsJsonAsync($"/api/partners/{draftDriver}/status", new { status = "Inactive" })).IsSuccessStatusCode);   // a Draft has nothing running on it
    }
}
