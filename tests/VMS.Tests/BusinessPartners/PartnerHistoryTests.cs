using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VMS.Shared.Authorization;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.BusinessPartners;

/// <summary>S1-BPU-12 (backend): the History tab reads everything that happened to a partner, and to what belongs to it, in one query.</summary>
[Collection(ApiCollection.Name)]
public sealed class PartnerHistoryTests(ApiFactory factory)
{
    private static int Id(JsonObject partner) => partner["id"]!.GetValue<int>();

    private static async Task<System.Text.Json.JsonElement> HistoryAsync(HttpClient client, int id, string query = "")
    {
        var response = await client.GetAsync($"/api/partners/{id}/history{query}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.DataAsync();
    }

    [Fact]
    public async Task Changes_to_the_partner_and_to_its_children_all_belong_to_the_partner_history()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person();
        body["contacts"] = new JsonArray(new JsonObject { ["contactName"] = "Old Contact", ["mobile"] = "0300-1000001" });
        var partner = await w.CreateAsync(body);
        var stranger = await w.CreateAsync(w.Person());   // its changes must not appear
        partner["contacts"]![0]!["contactName"] = "New Contact";
        partner["notes"] = "A note";
        partner["addresses"] = new JsonArray(new JsonObject { ["addressType"] = "Yard", ["line1"] = "Yard 9", ["cityId"] = w.City });
        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(partner)).StatusCode);

        var history = await HistoryAsync(w.Admin, Id(partner));
        var changes = history.GetProperty("changes").GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(changes, c => c.GetProperty("entity").GetString() == "BusinessPartner" && c.GetProperty("action").GetString() == "Created");
        Assert.Contains(changes, c => c.GetProperty("entity").GetString() == "BpContact" && c.GetProperty("field").GetString() == "ContactName"
                                       && c.GetProperty("oldValue").GetString() == "Old Contact" && c.GetProperty("newValue").GetString() == "New Contact");
        Assert.Contains(changes, c => c.GetProperty("entity").GetString() == "BusinessPartner" && c.GetProperty("field").GetString() == "Notes");
        Assert.Contains(changes, c => c.GetProperty("entity").GetString() == "BpAddress" && c.GetProperty("action").GetString() == "Created");
        Assert.DoesNotContain(changes, c => c.GetProperty("recordId").GetString() == Id(stranger).ToString() && c.GetProperty("entity").GetString() == "BusinessPartner");
        Assert.All(changes, c => Assert.Equal("Admin User", c.GetProperty("userName").GetString()));
    }

    [Fact]
    public async Task The_newest_change_comes_first_and_the_list_pages()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());
        for (var n = 0; n < 3; n++)
        {
            partner = await w.GetAsync(Id(partner));
            partner["notes"] = $"note {n}";
            await w.PutAsync(partner);
        }

        var first = await HistoryAsync(w.Admin, Id(partner), "?pageSize=2");
        var page = first.GetProperty("changes");
        var items = page.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.True(page.GetProperty("totalCount").GetInt32() > 2);
        Assert.Equal("note 2", items[0].GetProperty("newValue").GetString());
        Assert.Equal("note 1", items[1].GetProperty("newValue").GetString());
    }

    [Fact]
    public async Task Role_and_status_changes_are_in_their_own_logs_newest_first()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Company(roles: ["Vendor", "Workshop"]));
        await w.Admin.DeleteAsync($"/api/partners/{Id(partner)}/roles/Vendor?reason=Stopped");
        await w.Admin.PostAsJsonAsync($"/api/partners/{Id(partner)}/status", new { status = "Inactive", reason = "Closed for the year" });

        var history = await HistoryAsync(w.Admin, Id(partner));
        var roles = history.GetProperty("roles").EnumerateArray().ToList();
        var statuses = history.GetProperty("statuses").EnumerateArray().ToList();

        Assert.Equal(3, roles.Count);   // vendor added, workshop added, vendor removed
        Assert.Equal("Removed", roles[0].GetProperty("action").GetString());
        Assert.Equal("Stopped", roles[0].GetProperty("reason").GetString());
        var status = Assert.Single(statuses);
        Assert.Equal(("Active", "Inactive", "Closed for the year"), (status.GetProperty("fromStatus").GetString(), status.GetProperty("toStatus").GetString(), status.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task The_changes_list_leaves_out_what_the_role_log_already_says_and_values_that_start_at_nothing()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person(role: "Driver");
        body["driver"] = PartnerWorld.Driver(); body["driver"]!["commissionBasis"] = "None"; body["driver"]!["commissionValue"] = null;
        var partner = await w.CreateAsync(body);
        await w.Admin.PostAsJsonAsync($"/api/partners/{Id(partner)}/roles", new { roleCode = "Workshop" });

        var changes = (await HistoryAsync(w.Admin, Id(partner))).GetProperty("changes").GetProperty("items").EnumerateArray().ToList();

        Assert.DoesNotContain(changes, c => c.GetProperty("entity").GetString() == "BusinessPartnerRole");
        Assert.DoesNotContain(changes, c => c.GetProperty("field").ValueKind == System.Text.Json.JsonValueKind.String && c.GetProperty("field").GetString() is "CommissionBasis" or "OpeningBalance");
        Assert.Contains(changes, c => c.GetProperty("field").ValueKind == System.Text.Json.JsonValueKind.String && c.GetProperty("field").GetString() == "MonthlyRate");   // a real starting value is kept
    }

    [Fact]
    public async Task A_salary_change_is_shown_as_restricted_to_someone_who_may_not_see_salaries()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person(role: "Driver"); body["driver"] = PartnerWorld.Driver();
        var partner = await w.CreateAsync(body);
        partner["driver"]!["monthlyRate"] = 82000;
        await w.PutAsync(partner);
        var reader = w.As("Reader", 2, PermissionCodes.BP_VIEW);

        var full = (await HistoryAsync(w.Admin, Id(partner))).GetProperty("changes").GetProperty("items").EnumerateArray()
            .Single(c => c.GetProperty("field").ValueKind == System.Text.Json.JsonValueKind.String && c.GetProperty("field").GetString() == "MonthlyRate" && c.GetProperty("action").GetString() == "Updated");
        var limited = (await HistoryAsync(reader, Id(partner))).GetProperty("changes").GetProperty("items").EnumerateArray()
            .Single(c => c.GetProperty("field").ValueKind == System.Text.Json.JsonValueKind.String && c.GetProperty("field").GetString() == "MonthlyRate" && c.GetProperty("action").GetString() == "Updated");

        Assert.Equal("82000", full.GetProperty("newValue").GetString());
        Assert.False(full.GetProperty("restricted").GetBoolean());
        Assert.True(limited.GetProperty("restricted").GetBoolean());
        Assert.True(limited.GetProperty("newValue").ValueKind == System.Text.Json.JsonValueKind.Null);
        Assert.True(limited.GetProperty("oldValue").ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task A_duplicate_the_user_chose_to_save_beside_is_in_the_history()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var original = await w.CreateAsync(w.Person("Yaqoob Ali Brothers"));
        var twin = w.Person("Yaqoob Ali Brothers"); twin["acknowledgedDuplicateIds"] = new JsonArray(Id(original));
        var saved = await w.CreateAsync(twin);

        var changes = (await HistoryAsync(w.Admin, Id(saved))).GetProperty("changes").GetProperty("items").EnumerateArray().ToList();

        var note = Assert.Single(changes, c => c.GetProperty("action").GetString() == "DuplicateOverridden");
        Assert.Contains(original["bpCode"]!.GetValue<string>(), note.GetProperty("newValue").GetString());
    }

    [Fact]
    public async Task The_history_needs_the_view_permission_and_is_not_readable_across_tenants()
    {
        var mine = await PartnerWorld.CreateAsync(factory);
        var other = await PartnerWorld.CreateAsync(factory);
        var theirs = await other.CreateAsync(other.Person());

        Assert.Equal(HttpStatusCode.NotFound, (await mine.Admin.GetAsync($"/api/partners/{Id(theirs)}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.As("Nobody", 9, PermissionCodes.BP_CREATE).GetAsync($"/api/partners/{Id(theirs)}/history")).StatusCode);
    }

    [Fact]
    public async Task Audit_rows_of_a_partners_children_carry_the_partner_as_their_root()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person(); body["contacts"] = new JsonArray(new JsonObject { ["contactName"] = "A", ["mobile"] = "0300-1000001" });
        var partner = await w.CreateAsync(body);

        var rows = factory.Query("SELECT Entity, RootEntity, RootRecordId FROM core.AuditEntries WHERE TenantId = @t AND Entity IN ('BpContact', 'BpAddress', 'BusinessPartnerRole')", ("@t", w.Tenant));

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(("BusinessPartner", Id(partner).ToString()), (r["RootEntity"], r["RootRecordId"])));
    }
}
