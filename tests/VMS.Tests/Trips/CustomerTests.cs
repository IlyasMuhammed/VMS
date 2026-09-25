using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-03: the Customer master (FSD §10) — independent of Business Partner, unique code, Draft/Active/
/// Inactive lifecycle, and the activation-checklist extension point (empty until CC-04/05/07 each register their
/// own <see cref="Services.ICustomerActivationRequirement"/>).</summary>
[Collection(ApiCollection.Name)]
public sealed class CustomerTests(ApiFactory factory)
{
    private static object Minimal(string? name = null) => new { customerName = name ?? $"Acme {Guid.NewGuid():N}", addressLine1 = "12 Mall Road" };

    [Fact]
    public async Task AC_01_a_customer_is_created_with_no_business_partner_concept_at_all()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var created = await w.Admin.PostAsJsonAsync("/api/customers", Minimal());
        created.EnsureSuccessStatusCode();
        var raw = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain("businessPartnerId", raw, StringComparison.OrdinalIgnoreCase);
        var data = await created.DataAsync();
        Assert.Equal("Draft", data.GetProperty("status").GetString());
        Assert.StartsWith("CUS-", data.GetProperty("customerCode").GetString());
    }

    [Fact]
    public async Task AC_02_the_customer_code_is_unique_case_insensitively()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var first = await w.Admin.PostAsJsonAsync("/api/customers", new { customerName = "Acme", addressLine1 = "Line 1", customerCode = "CUS-90001" });
        first.EnsureSuccessStatusCode();

        var duplicate = await w.Admin.PostAsJsonAsync("/api/customers", new { customerName = "Acme Two", addressLine1 = "Line 1", customerCode = "cus-90001" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("VAL-GEN-013", await duplicate.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AC_03_an_inactive_customer_is_hidden_from_the_picker_but_still_shows_in_search_and_by_id()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var created = await (await w.Admin.PostAsJsonAsync("/api/customers", Minimal("Widgets Ltd"))).DataAsync();
        var id = created.GetProperty("customerId").GetInt32();
        var rowVersion = created.GetProperty("rowVersion").GetString();

        // Override reason: this test is about the Active/Inactive lifecycle, not the checklist (CC-04's own tests
        // cover that) — a Draft with no contact/address needs the Admin override to reach Active at all.
        var activated = await w.Admin.PostAsJsonAsync($"/api/customers/{id}/activate", new { rowVersion, reason = "Test override" });
        activated.EnsureSuccessStatusCode();
        rowVersion = (await activated.DataAsync()).GetProperty("rowVersion").GetString();

        Assert.Contains((await (await w.Admin.GetAsync("/api/customers/picker?search=Widgets")).DataAsync()).EnumerateArray(),
            c => c.GetProperty("customerId").GetInt32() == id);

        var deactivated = await w.Admin.PostAsJsonAsync($"/api/customers/{id}/deactivate", new { reason = "No longer trading", rowVersion });
        deactivated.EnsureSuccessStatusCode();

        Assert.DoesNotContain((await (await w.Admin.GetAsync("/api/customers/picker?search=Widgets")).DataAsync()).EnumerateArray(),
            c => c.GetProperty("customerId").GetInt32() == id);

        // Still fully visible by id and in the general search/list, with an Inactive status — never hidden from history.
        var byId = await (await w.Admin.GetAsync($"/api/customers/{id}")).DataAsync();
        Assert.Equal("Inactive", byId.GetProperty("status").GetString());
        Assert.Contains((await (await w.Admin.GetAsync("/api/customers?search=Widgets")).DataAsync()).GetProperty("items").EnumerateArray(),
            c => c.GetProperty("customerId").GetInt32() == id);
    }

    [Fact]
    public async Task A_customer_defaults_to_pakistan_and_pkr_and_rejects_an_unknown_currency()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var created = await (await w.Admin.PostAsJsonAsync("/api/customers", Minimal())).DataAsync();
        Assert.Equal("PKR", created.GetProperty("currencyCode").GetString());
        Assert.True(created.GetProperty("countryId").GetInt32() > 0);

        var rejected = await w.Admin.PostAsJsonAsync("/api/customers", new { customerName = "X", addressLine1 = "Y", currencyCode = "ZZZ" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task Required_fields_are_enforced_on_create()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var response = await w.Admin.PostAsJsonAsync("/api/customers", new { customerName = "", addressLine1 = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("errors");
        Assert.True(errors.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task The_activation_checklist_now_names_all_four_items_cc_04_05_07_registered()
    {
        // CC-03 (when written) found an empty checklist — this module's ICustomerActivationRequirement extension
        // point had nothing registered yet. CC-04 added the contact and billing-address checks, CC-05 added
        // billing-configuration, CC-07 added invoice template (each task's own tests prove the full pass/fail
        // behaviour); this only re-confirms a bare customer shows all four as missing, not a stale count.
        var w = await TripsWorld.CreateAsync(factory);
        var created = await (await w.Admin.PostAsJsonAsync("/api/customers", Minimal())).DataAsync();
        var id = created.GetProperty("customerId").GetInt32();

        var check = await (await w.Admin.GetAsync($"/api/customers/{id}/activation-check")).DataAsync();
        Assert.False(check.GetProperty("canActivate").GetBoolean());
        Assert.Equal(4, check.GetProperty("missingItems").GetArrayLength());
    }

    [Fact]
    public async Task Only_a_draft_customer_can_be_activated_and_only_an_inactive_one_reactivated()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var created = await (await w.Admin.PostAsJsonAsync("/api/customers", Minimal())).DataAsync();
        var id = created.GetProperty("customerId").GetInt32();
        var rowVersion = created.GetProperty("rowVersion").GetString();

        var activated = await w.Admin.PostAsJsonAsync($"/api/customers/{id}/activate", new { rowVersion, reason = "Test override" });
        activated.EnsureSuccessStatusCode();
        rowVersion = (await activated.DataAsync()).GetProperty("rowVersion").GetString();

        Assert.Equal(HttpStatusCode.Conflict, (await w.Admin.PostAsJsonAsync($"/api/customers/{id}/activate", new { rowVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Admin.PostAsJsonAsync($"/api/customers/{id}/reactivate", new { rowVersion })).StatusCode);
    }

    [Fact]
    public async Task Deactivating_requires_a_reason()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var created = await (await w.Admin.PostAsJsonAsync("/api/customers", Minimal())).DataAsync();
        var id = created.GetProperty("customerId").GetInt32();
        var rowVersion = created.GetProperty("rowVersion").GetString();
        (await w.Admin.PostAsJsonAsync($"/api/customers/{id}/activate", new { rowVersion, reason = "Test override" })).EnsureSuccessStatusCode();
        rowVersion = (await (await w.Admin.GetAsync($"/api/customers/{id}")).DataAsync()).GetProperty("rowVersion").GetString();

        var response = await w.Admin.PostAsJsonAsync($"/api/customers/{id}/deactivate", new { rowVersion });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_stale_save_is_refused_with_the_concurrency_code_naming_the_last_editor()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var created = await (await w.Admin.PostAsJsonAsync("/api/customers", Minimal())).DataAsync();
        var id = created.GetProperty("customerId").GetInt32();
        var staleRowVersion = created.GetProperty("rowVersion").GetString();

        (await w.Admin.PutAsJsonAsync($"/api/customers/{id}", new { customerName = "Renamed", addressLine1 = "Line 1", rowVersion = staleRowVersion })).EnsureSuccessStatusCode();

        var stale = await w.Admin.PutAsJsonAsync($"/api/customers/{id}", new { customerName = "Renamed Again", addressLine1 = "Line 1", rowVersion = staleRowVersion });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var doc = await stale.Content.ReadAsStringAsync();
        Assert.Contains("CONCURRENCY_CONFLICT", doc);
    }

    [Fact]
    public async Task Only_a_never_used_draft_customer_can_be_deleted()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var draft = await (await w.Admin.PostAsJsonAsync("/api/customers", Minimal())).DataAsync();
        var draftId = draft.GetProperty("customerId").GetInt32();
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/customers/{draftId}") { Content = JsonContent.Create(new { rowVersion = draft.GetProperty("rowVersion").GetString() }) };
        (await w.Admin.SendAsync(request)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await w.Admin.GetAsync($"/api/customers/{draftId}")).StatusCode);

        var active = await (await w.Admin.PostAsJsonAsync("/api/customers", Minimal())).DataAsync();
        var activeId = active.GetProperty("customerId").GetInt32();
        (await w.Admin.PostAsJsonAsync($"/api/customers/{activeId}/activate", new { rowVersion = active.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();
        var afterActivate = await (await w.Admin.GetAsync($"/api/customers/{activeId}")).DataAsync();
        var deleteActive = new HttpRequestMessage(HttpMethod.Delete, $"/api/customers/{activeId}") { Content = JsonContent.Create(new { rowVersion = afterActivate.GetProperty("rowVersion").GetString() }) };
        Assert.Equal(HttpStatusCode.Conflict, (await w.Admin.SendAsync(deleteActive)).StatusCode);
    }

    [Fact]
    public async Task History_shows_the_activation_and_field_changes_newest_first()
    {
        // §48.1's own common pattern: "History button on every record opens audit rows (who, when, field, old →
        // new, reason)." Added this task (CC-43), reading the shared core.AuditEntries table the same way every
        // other module's own history endpoint already does.
        var w = await TripsWorld.CreateAsync(factory);
        var created = await (await w.Admin.PostAsJsonAsync("/api/customers", Minimal("History Co"))).DataAsync();
        var id = created.GetProperty("customerId").GetInt32();
        var rowVersion = created.GetProperty("rowVersion").GetString();

        var updated = await (await w.Admin.PutAsJsonAsync($"/api/customers/{id}", new { customerName = "History Co Renamed", addressLine1 = "12 Mall Road", rowVersion })).DataAsync();
        rowVersion = updated.GetProperty("rowVersion").GetString();
        (await w.Admin.PostAsJsonAsync($"/api/customers/{id}/activate", new { rowVersion, reason = "Test override" })).EnsureSuccessStatusCode();

        var history = await (await w.Admin.GetAsync($"/api/customers/{id}/history")).DataAsync();
        var changes = history.GetProperty("changes").GetProperty("items").EnumerateArray().ToList();
        Assert.True(changes.Count >= 2);
        Assert.Contains(changes, c => c.GetProperty("entity").GetString() == "Customer" && c.GetProperty("recordId").GetString() == id.ToString());
        // Newest first: the activation (a later change) is reported no later in the list than the rename.
        var activationIndex = changes.FindIndex(c => c.GetProperty("newValue").GetString() == "Active");
        var renameIndex = changes.FindIndex(c => c.GetProperty("newValue").GetString() == "History Co Renamed");
        Assert.True(activationIndex >= 0 && renameIndex >= 0 && activationIndex < renameIndex);
    }

    [Fact]
    public async Task View_needs_view_permission_edit_actions_need_edit_permission()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var viewer = w.As("Viewer", 5, PermissionCodes.TRP_CUSTOMER_VIEW);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/customers", Minimal())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/customers")).StatusCode);

        var noAccess = w.As("Nobody", 6);
        Assert.Equal(HttpStatusCode.Forbidden, (await noAccess.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noAccess.GetAsync("/api/customers/picker")).StatusCode);
    }
}
