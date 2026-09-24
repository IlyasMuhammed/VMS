using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Shared.Partners;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.BusinessPartners;

/// <summary>S1-BP-06, 10, 11, 12: changing a partner, its roles and its status.</summary>
[Collection(ApiCollection.Name)]
public sealed class PartnerManageTests(ApiFactory factory)
{
    private static int Id(JsonObject partner) => partner["id"]!.GetValue<int>();

    // ── Updating ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_edit_changes_the_partner_and_the_general_tab_address_carries_over_to_the_primary_address()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());
        partner["email"] = "new@example.pk";
        partner["addressLine"] = "77 New Road";
        partner["cityId"] = w.OtherCity;

        var response = await w.PutAsync(partner);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await w.GetAsync(Id(partner));

        Assert.Equal("new@example.pk", saved["email"]!.GetValue<string>());
        Assert.Equal("77 New Road", factory.Scalar<string>("SELECT Line1 FROM bp.BpAddresses WHERE BusinessPartnerId = @i AND IsPrimary = 1", ("@i", Id(partner))));
        Assert.Equal(w.OtherCity, factory.Scalar<int>("SELECT CityId FROM bp.BpAddresses WHERE BusinessPartnerId = @i AND IsPrimary = 1", ("@i", Id(partner))));
        Assert.NotEqual(partner["rowVersion"]!.GetValue<string>(), saved["rowVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_edit_that_breaks_a_rule_is_refused_the_same_way_as_a_new_partner()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());
        partner["primaryMobile"] = "12";
        partner["legalName"] = "";

        var errors = await PartnerWorld.ErrorsAsync(await w.PutAsync(partner));

        Assert.Contains(errors, e => e is { Field: "primaryMobile", Code: Msg.BpMobileFormat });
        Assert.Contains(errors, e => e is { Field: "legalName", Code: Msg.Required });
    }

    [Fact]
    public async Task A_cnic_that_belongs_to_another_partner_is_refused_but_ones_own_is_not()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var first = await w.CreateAsync(w.Person());
        var second = await w.CreateAsync(w.Person());

        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(second)).StatusCode);   // saved with its own CNIC as it was

        second = await w.GetAsync(Id(second));
        second["cnic"] = first["cnic"]!.DeepClone();
        await PartnerWorld.AssertRefusedAsync(await w.PutAsync(second), "cnic", Msg.BpCnicUsed);
    }

    [Fact]
    public async Task A_stale_save_is_refused_and_says_who_changed_the_partner_and_when()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var amna = w.As("Amna Malik", 10, PartnerWorld.Everything);
        var bilal = w.As("Bilal Ahmed", 11, PartnerWorld.Everything);
        var created = await w.CreateAsync(w.Person());

        var amnasCopy = await w.GetAsync(Id(created), amna);
        var bilalsCopy = await w.GetAsync(Id(created), bilal);
        bilalsCopy["notes"] = "Bilal's note";
        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(bilalsCopy, bilal)).StatusCode);

        amnasCopy["notes"] = "Amna's note";
        var response = await w.PutAsync(amnasCopy, amna);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Bilal Ahmed", await response.MessageAsync());
        Assert.Equal("Bilal's note", (await w.GetAsync(Id(created)))["notes"]!.GetValue<string>());   // hers did not overwrite his
    }

    [Fact]
    public async Task Two_saves_from_the_same_starting_point_at_the_same_moment_let_one_through()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var created = await w.CreateAsync(w.Person());
        var copies = Enumerable.Range(0, 5).Select(n => { var c = (JsonObject)created.DeepClone(); c["notes"] = $"edit {n}"; return c; }).ToList();

        var responses = await Task.WhenAll(copies.Select(c => w.PutAsync(c)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task A_save_without_the_row_version_is_refused()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());
        partner.Remove("rowVersion");

        await PartnerWorld.AssertRefusedAsync(await w.PutAsync(partner), "rowVersion", Msg.Required);
    }

    [Fact]
    public async Task What_changed_is_in_the_audit_trail_with_the_old_and_new_value()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person("Old Trading Name"));
        partner["legalName"] = "New Trading Name";
        var editor = w.As("Sana Iqbal", 21, PartnerWorld.Everything);

        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(partner, editor)).StatusCode);

        var row = Assert.Single(factory.Query(
            "SELECT OldValue, NewValue, UserName FROM core.AuditEntries WHERE TenantId = @t AND Entity = 'BusinessPartner' AND RecordId = @r AND Action = 'Updated' AND Field = 'LegalName'",
            ("@t", w.Tenant), ("@r", Id(partner).ToString())));
        Assert.Equal(("Old Trading Name", "New Trading Name", "Sana Iqbal"), (row["OldValue"], row["NewValue"], row["UserName"]));
    }

    [Fact]
    public async Task Restricted_values_in_the_audit_trail_carry_the_permission_needed_to_see_them()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person(role: "Driver"); body["driver"] = PartnerWorld.Driver();
        var partner = await w.CreateAsync(body);
        partner["driver"]!["monthlyRate"] = 75000;

        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(partner)).StatusCode);

        var row = factory.Query(
            "SELECT RequiredPermission FROM core.AuditEntries WHERE TenantId = @t AND Entity = 'BpDriverDetail' AND Field = 'MonthlyRate' AND Action = 'Updated'",
            ("@t", w.Tenant)).Single();
        Assert.Equal(PermissionCodes.BP_FIELD_SALARY_VIEW, row["RequiredPermission"]);
    }

    [Fact]
    public async Task Editing_without_the_salary_permission_does_not_wipe_the_salary()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person(role: "Driver"); body["driver"] = PartnerWorld.Driver();
        var created = await w.CreateAsync(body);
        var clerk = w.As("Clerk", 3, PermissionCodes.BP_VIEW, PermissionCodes.BP_EDIT);

        var seen = await w.GetAsync(Id(created), clerk);   // the clerk's copy has no salary in it
        seen["notes"] = "Called the driver";
        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(seen, clerk)).StatusCode);

        Assert.Equal(60000m, factory.Scalar<decimal>("SELECT MonthlyRate FROM bp.BpDriverDetails WHERE BusinessPartnerId = @i", ("@i", Id(created))));

        // ...and a value the clerk sends anyway is ignored.
        seen = await w.GetAsync(Id(created), clerk);
        seen["driver"]!["monthlyRate"] = 1;
        await w.PutAsync(seen, clerk);
        Assert.Equal(60000m, factory.Scalar<decimal>("SELECT MonthlyRate FROM bp.BpDriverDetails WHERE BusinessPartnerId = @i", ("@i", Id(created))));
    }

    [Fact]
    public async Task The_opening_balance_is_entered_once_and_an_edit_cannot_change_it()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person(); body["openingBalance"] = 2500; body["openingBalanceDate"] = "2026-01-01";
        var created = await w.CreateAsync(body);
        created["openingBalance"] = 99;
        created["openingBalanceDate"] = "2026-02-02";

        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(created)).StatusCode);

        Assert.Equal(2500m, factory.Scalar<decimal>("SELECT OpeningBalance FROM bp.BusinessPartners WHERE BusinessPartnerId = @i", ("@i", Id(created))));
    }

    [Fact]
    public async Task Contacts_bank_accounts_and_addresses_are_added_changed_and_removed_together_by_the_id_they_have()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person();
        body["contacts"] = new JsonArray(
            new JsonObject { ["contactName"] = "First", ["mobile"] = "0300-1000001", ["isPrimary"] = true },
            new JsonObject { ["contactName"] = "Second", ["mobile"] = "0300-1000002" },
            new JsonObject { ["contactName"] = "Third", ["mobile"] = "0300-1000003" });
        body["bankAccounts"] = new JsonArray(new JsonObject { ["accountTitle"] = "T", ["bankName"] = "HBL", ["accountNumber"] = "111", ["isPrimary"] = true });
        var partner = await w.CreateAsync(body);
        var contacts = partner["contacts"]!.AsArray();
        var (firstId, secondId, thirdId) = (contacts[0]!["id"]!.GetValue<int>(), contacts[1]!["id"]!.GetValue<int>(), contacts[2]!["id"]!.GetValue<int>());

        // Rename the first, make the second primary (the first stops being), drop the third, add a fourth; add an address; replace the account.
        partner["contacts"] = new JsonArray(
            new JsonObject { ["id"] = firstId, ["contactName"] = "First renamed", ["mobile"] = "0300-1000001" },
            new JsonObject { ["id"] = secondId, ["contactName"] = "Second", ["mobile"] = "0300-1000002", ["isPrimary"] = true },
            new JsonObject { ["contactName"] = "Fourth", ["mobile"] = "0300-1000004" });
        partner["addresses"] = new JsonArray(new JsonObject { ["addressType"] = "Yard", ["line1"] = "Yard 9", ["cityId"] = w.OtherCity });
        partner["bankAccounts"] = new JsonArray(new JsonObject { ["accountTitle"] = "T2", ["bankName"] = "UBL", ["accountNumber"] = "222", ["isPrimary"] = true });

        var response = await w.PutAsync(partner);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var saved = await w.GetAsync(Id(partner));

        var names = saved["contacts"]!.AsArray().Select(c => c!["contactName"]!.GetValue<string>()).ToList();
        Assert.Equal(["First renamed", "Second", "Fourth"], names);
        Assert.Equal(firstId, saved["contacts"]![0]!["id"]!.GetValue<int>());   // the same row, renamed
        Assert.DoesNotContain(saved["contacts"]!.AsArray(), c => c!["id"]!.GetValue<int>() == thirdId);
        Assert.Equal(["Second"], saved["contacts"]!.AsArray().Where(c => c!["isPrimary"]!.GetValue<bool>()).Select(c => c!["contactName"]!.GetValue<string>()));
        Assert.Equal("Yard 9", saved["addresses"]![0]!["line1"]!.GetValue<string>());
        Assert.Equal("UBL", Assert.Single(saved["bankAccounts"]!.AsArray())!["bankName"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_row_id_that_belongs_to_another_partner_is_refused()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person(); body["contacts"] = new JsonArray(new JsonObject { ["contactName"] = "Mine", ["mobile"] = "0300-1000001" });
        var mine = await w.CreateAsync(body);
        var theirBody = w.Person(); theirBody["contacts"] = new JsonArray(new JsonObject { ["contactName"] = "Theirs", ["mobile"] = "0300-1000009" });
        var theirs = await w.CreateAsync(theirBody);

        mine["contacts"] = new JsonArray(new JsonObject { ["id"] = theirs["contacts"]![0]!["id"]!.DeepClone(), ["contactName"] = "Stolen", ["mobile"] = "0300-1000009" });

        Assert.Equal(HttpStatusCode.BadRequest, (await w.PutAsync(mine)).StatusCode);
        Assert.Equal("Theirs", (await w.GetAsync(Id(theirs)))["contacts"]![0]!["contactName"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_duplicate_the_user_already_answered_is_not_asked_about_on_every_later_edit()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var original = await w.CreateAsync(w.Person("Zafar Iqbal Traders"));
        var twin = w.Person("Zafar Iqbal Traders"); twin["acknowledgedDuplicateIds"] = new JsonArray(Id(original));
        var saved = await w.CreateAsync(twin);

        saved["notes"] = "Only a note changed";
        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(saved)).StatusCode);

        // Changing the name is different: it is checked again.
        saved = await w.GetAsync(Id(saved));
        saved["legalName"] = "Zafar Iqbal Trader";
        await PartnerWorld.AssertRefusedAsync(await w.PutAsync(saved), "legalName", Msg.BpSimilarName);
    }

    [Fact]
    public async Task A_merged_partner_is_read_only()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());
        factory.Execute("UPDATE bp.BusinessPartners SET Status = 'Merged' WHERE BusinessPartnerId = @i", ("@i", Id(partner)));

        Assert.Equal(HttpStatusCode.Conflict, (await w.PutAsync(partner)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Admin.PostAsJsonAsync($"/api/partners/{Id(partner)}/status", new { status = "Inactive" })).StatusCode);
    }

    [Fact]
    public async Task Party_type_cannot_change_once_something_uses_the_partner_and_can_before()
    {
        var w = await PartnerWorld.CreateAsync(factory, FakePartnerUsage.Host(factory));
        var used = await w.CreateAsync(w.Person());
        var free = await w.CreateAsync(w.Person());
        FakePartnerUsage.InUse[(Id(used), PartnerIntent.ChangePartyType)] = "Driver assigned to LEA-1234";

        foreach (var partner in new[] { used, free })
        {
            partner["partyType"] = "Company";
            partner["ntn"] = PartnerWorld.NextNtn();
            partner["email"] = "x@example.pk";
        }

        await PartnerWorld.AssertRefusedAsync(await w.PutAsync(used), "partyType", Msg.ReadOnlyOnceSaved);
        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(free)).StatusCode);
        Assert.Equal("Company", (await w.GetAsync(Id(free)))["partyType"]!.GetValue<string>());
    }

    [Fact]
    public async Task Editing_needs_the_edit_permission()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());

        Assert.Equal(HttpStatusCode.Forbidden, (await w.PutAsync(partner, w.As("Viewer", 5, PermissionCodes.BP_VIEW, PermissionCodes.BP_CREATE))).StatusCode);
    }

    [Fact]
    public async Task Someone_elses_partner_cannot_be_changed()
    {
        var mine = await PartnerWorld.CreateAsync(factory);
        var other = await PartnerWorld.CreateAsync(factory);
        var theirs = await other.CreateAsync(other.Person());
        theirs["notes"] = "hijack";

        Assert.Equal(HttpStatusCode.NotFound, (await mine.PutAsync(theirs)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mine.Admin.PostAsJsonAsync($"/api/partners/{Id(theirs)}/status", new { status = "Inactive" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mine.Admin.PostAsJsonAsync($"/api/partners/{Id(theirs)}/roles", new { roleCode = "Bank" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mine.Admin.DeleteAsync($"/api/partners/{Id(theirs)}/roles/Workshop")).StatusCode);
    }

    // ── Roles ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_role_is_added_with_its_panel_and_logged()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());

        var response = await w.Admin.PostAsJsonAsync($"/api/partners/{Id(partner)}/roles", new
        {
            roleCode = "Driver",
            reason = "Started driving for us",
            driver = PartnerWorld.Driver("KHI-5555")
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var saved = await w.GetAsync(Id(partner));

        Assert.Equal(["Driver", "Workshop"], saved["roles"]!.AsArray().Select(r => r!.GetValue<string>()));
        Assert.Equal("KHI-5555", saved["driver"]!["licenceNo"]!.GetValue<string>());
        var log = factory.Query("SELECT RoleCode, Action, Reason, UserName FROM bp.BpRoleLog WHERE BusinessPartnerId = @i AND RoleCode = 'Driver'", ("@i", Id(partner))).Single();
        Assert.Equal(("Added", "Started driving for us", "Admin User"), (log["Action"], log["Reason"], log["UserName"]));
    }

    [Fact]
    public async Task A_new_role_that_needs_a_panel_is_refused_without_one_and_a_role_already_held_is_refused()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());
        var url = $"/api/partners/{Id(partner)}/roles";

        await PartnerWorld.AssertRefusedAsync(await w.Admin.PostAsJsonAsync(url, new { roleCode = "Vendor" }), "vendor", Msg.Required);
        await PartnerWorld.AssertRefusedAsync(await w.Admin.PostAsJsonAsync(url, new { roleCode = "Workshop" }), "roleCode", Msg.AlreadyUsed);
        await PartnerWorld.AssertRefusedAsync(await w.Admin.PostAsJsonAsync(url, new { roleCode = "Pirate" }), "roleCode", Msg.OneOf);
        await PartnerWorld.AssertRefusedAsync(await w.Admin.PostAsJsonAsync(url, new { roleCode = "Bank" }), "email", Msg.BpEmailRequiredForCustomer);   // a bank needs an email on the partner
    }

    [Fact]
    public async Task A_licence_held_by_another_driver_blocks_adding_the_driver_role()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var holderBody = w.Person(role: "Driver"); holderBody["driver"] = PartnerWorld.Driver("ISB-1212");
        await w.CreateAsync(holderBody);
        var partner = await w.CreateAsync(w.Person());

        var response = await w.Admin.PostAsJsonAsync($"/api/partners/{Id(partner)}/roles", new { roleCode = "Driver", driver = PartnerWorld.Driver("ISB-1212") });

        await PartnerWorld.AssertRefusedAsync(response, "driver.licenceNo", Msg.AlreadyUsed);
    }

    private async Task<JsonObject> WithTwoRolesAsync(PartnerWorld w)
    {
        var body = w.Company(roles: ["Vendor", "Workshop"]);
        body["vendor"]!["paymentTermDays"] = 30;
        return await w.CreateAsync(body);
    }

    [Fact]
    public async Task A_role_is_removed_but_its_history_and_panel_stay_and_it_can_come_back_without_the_panel_being_sent_again()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await WithTwoRolesAsync(w);
        var id = Id(partner);

        var removed = await w.Admin.DeleteAsync($"/api/partners/{id}/roles/Vendor?reason=Stopped%20supplying");
        Assert.True(removed.IsSuccessStatusCode, await removed.Content.ReadAsStringAsync());
        var after = await w.GetAsync(id);

        Assert.Equal(["Workshop"], after["roles"]!.AsArray().Select(r => r!.GetValue<string>()));
        var vendor = after["roleHistory"]!.AsArray().Single(r => r!["roleCode"]!.GetValue<string>() == "Vendor");
        Assert.False(vendor!["isActive"]!.GetValue<bool>());
        Assert.NotNull(vendor["effectiveTo"]);
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM bp.BpVendorDetails WHERE BusinessPartnerId = @i", ("@i", id)));   // the panel is kept
        Assert.Equal("Removed", factory.Scalar<string>("SELECT TOP 1 Action FROM bp.BpRoleLog WHERE BusinessPartnerId = @i AND RoleCode = 'Vendor' ORDER BY BpRoleLogId DESC", ("@i", id)));

        var back = await w.Admin.PostAsJsonAsync($"/api/partners/{id}/roles", new { roleCode = "Vendor" });   // no panel: it already has one
        Assert.True(back.IsSuccessStatusCode, await back.Content.ReadAsStringAsync());
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM bp.BusinessPartnerRoles WHERE BusinessPartnerId = @i AND RoleCode = 'Vendor'", ("@i", id)));   // the row is reused
        Assert.Equal(30, (await w.GetAsync(id))["vendor"]!["paymentTermDays"]!.GetValue<int>());
        Assert.Equal(3, factory.Scalar<int>("SELECT COUNT(*) FROM bp.BpRoleLog WHERE BusinessPartnerId = @i AND RoleCode = 'Vendor'", ("@i", id)));   // added, removed, added
    }

    [Fact]
    public async Task The_last_role_cannot_be_removed()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());

        var response = await w.Admin.DeleteAsync($"/api/partners/{Id(partner)}/roles/Workshop");

        await PartnerWorld.AssertRefusedAsync(response, "roleCode", Msg.BpLastRole);
    }

    [Fact]
    public async Task A_role_something_is_using_cannot_be_removed_and_the_usage_says_what()
    {
        var w = await PartnerWorld.CreateAsync(factory, FakePartnerUsage.Host(factory));
        var partner = await WithTwoRolesAsync(w);
        FakePartnerUsage.InUse[(Id(partner), PartnerIntent.RemoveRole)] = "Vendor for vehicle LEA-1234";

        await PartnerWorld.AssertRefusedAsync(await w.Admin.DeleteAsync($"/api/partners/{Id(partner)}/roles/Vendor"), "roleCode", Msg.BpRoleInUse);
        Assert.Equal(2, (await w.GetAsync(Id(partner)))["roles"]!.AsArray().Count);

        var usage = (await (await w.Admin.GetAsync($"/api/partners/{Id(partner)}/usage?intent=RemoveRole&role=Vendor")).DataAsync()).EnumerateArray().Single();
        Assert.Equal("Vendor for vehicle LEA-1234", usage.GetProperty("description").GetString());
        Assert.Equal("LEA-1234", usage.GetProperty("recordCode").GetString());
    }

    [Fact]
    public async Task Roles_are_managed_only_by_someone_allowed_to()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await WithTwoRolesAsync(w);
        var editor = w.As("Editor", 7, PermissionCodes.BP_VIEW, PermissionCodes.BP_EDIT);

        Assert.Equal(HttpStatusCode.Forbidden, (await editor.DeleteAsync($"/api/partners/{Id(partner)}/roles/Vendor")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsJsonAsync($"/api/partners/{Id(partner)}/roles", new { roleCode = "Bank" })).StatusCode);
    }

    // ── Status ──────────────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> SetStatus(PartnerWorld w, JsonObject partner, string status, string? reason = null, string? effective = null, HttpClient? client = null) =>
        (client ?? w.Admin).PostAsJsonAsync($"/api/partners/{Id(partner)}/status", new { status, reason, effectiveDate = effective });

    [Fact]
    public async Task A_partner_is_made_inactive_and_active_again_and_each_change_is_logged()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());

        var off = await SetStatus(w, partner, "Inactive", "Stopped trading");
        Assert.True(off.IsSuccessStatusCode, await off.Content.ReadAsStringAsync());
        Assert.Equal("Inactive", (await w.GetAsync(Id(partner)))["status"]!.GetValue<string>());
        Assert.Equal("Stopped trading", (await w.GetAsync(Id(partner)))["statusReason"]!.GetValue<string>());

        Assert.True((await SetStatus(w, partner, "Active")).IsSuccessStatusCode);
        var saved = await w.GetAsync(Id(partner));
        Assert.Equal("Active", saved["status"]!.GetValue<string>());
        Assert.Null(saved["statusReason"]);   // a reason belongs to the status it explained

        var log = factory.Query("SELECT FromStatus, ToStatus, Reason, UserName FROM bp.BpStatusLog WHERE BusinessPartnerId = @i ORDER BY BpStatusLogId", ("@i", Id(partner)));
        Assert.Equal(2, log.Count);
        Assert.Equal(("Active", "Inactive", "Stopped trading", "Admin User"), (log[0]["FromStatus"], log[0]["ToStatus"], log[0]["Reason"], log[0]["UserName"]));
        Assert.Equal(("Inactive", "Active"), (log[1]["FromStatus"], log[1]["ToStatus"]));
    }

    [Fact]
    public async Task Blacklisting_needs_a_reason_of_at_least_ten_characters_and_can_be_lifted()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());

        await PartnerWorld.AssertRefusedAsync(await SetStatus(w, partner, "Blacklisted"), "reason", Msg.MinLength);
        await PartnerWorld.AssertRefusedAsync(await SetStatus(w, partner, "Blacklisted", "too short"), "reason", Msg.MinLength);

        Assert.True((await SetStatus(w, partner, "Blacklisted", "Disputed a large payment")).IsSuccessStatusCode);
        Assert.Equal("Blacklisted", (await w.GetAsync(Id(partner)))["status"]!.GetValue<string>());
        Assert.True((await SetStatus(w, partner, "Active", "Dispute settled")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Only_the_moves_the_fsd_allows_are_allowed()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());

        await PartnerWorld.AssertRefusedAsync(await SetStatus(w, partner, "Merged"), "status", Msg.OneOf);   // merging is its own action
        await PartnerWorld.AssertRefusedAsync(await SetStatus(w, partner, "Nonsense"), "status", Msg.OneOf);
        Assert.Equal(HttpStatusCode.Conflict, (await SetStatus(w, partner, "Active")).StatusCode);          // already active

        await SetStatus(w, partner, "Inactive");
        Assert.Equal(HttpStatusCode.Conflict, (await SetStatus(w, partner, "Blacklisted", "A long enough reason")).StatusCode);   // inactive → blacklisted is not a move
    }

    [Fact]
    public async Task The_effective_date_defaults_to_today_and_cannot_be_in_the_future()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());

        await PartnerWorld.AssertRefusedAsync(await SetStatus(w, partner, "Inactive", effective: DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd")), "effectiveDate", Msg.NotFuture);
        Assert.True((await SetStatus(w, partner, "Inactive", effective: "2026-01-15")).IsSuccessStatusCode);
        Assert.Equal(new DateTime(2026, 1, 15), factory.Scalar<DateTime>("SELECT EffectiveDate FROM bp.BpStatusLog WHERE BusinessPartnerId = @i", ("@i", Id(partner))));
    }

    [Fact]
    public async Task A_partner_something_is_using_cannot_be_made_inactive_but_can_be_blacklisted()
    {
        var w = await PartnerWorld.CreateAsync(factory, FakePartnerUsage.Host(factory));
        var partner = await w.CreateAsync(w.Person());
        FakePartnerUsage.InUse[(Id(partner), PartnerIntent.Deactivate)] = "Driver assigned to LEA-1234";

        await PartnerWorld.AssertRefusedAsync(await SetStatus(w, partner, "Inactive"), "status", Msg.BpDeactivationBlocked);
        Assert.Equal("Active", (await w.GetAsync(Id(partner)))["status"]!.GetValue<string>());
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM bp.BpStatusLog WHERE BusinessPartnerId = @i", ("@i", Id(partner))));

        Assert.True((await SetStatus(w, partner, "Blacklisted", "Fraud under investigation")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Changing_status_needs_its_own_permission()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());
        var editor = w.As("Editor", 7, PermissionCodes.BP_VIEW, PermissionCodes.BP_EDIT, PermissionCodes.BP_ROLE_MANAGE);

        Assert.Equal(HttpStatusCode.Forbidden, (await SetStatus(w, partner, "Inactive", client: editor)).StatusCode);
    }
}
