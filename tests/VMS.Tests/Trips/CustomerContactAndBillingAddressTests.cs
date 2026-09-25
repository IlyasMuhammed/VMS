using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VMS.Shared.Authorization;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-04: customer contacts (§11) and billing addresses (§12), and — because they're the first two of the
/// three tables CC-03's activation checklist waits on — the checklist actually starting to bite.</summary>
[Collection(ApiCollection.Name)]
public sealed class CustomerContactAndBillingAddressTests(ApiFactory factory)
{
    private static async Task<int> NewCustomerAsync(HttpClient admin) =>
        (await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "12 Mall Road" })).DataAsync())
        .GetProperty("customerId").GetInt32();

    [Fact]
    public async Task A_contact_needs_a_valid_mobile_and_at_most_one_active_primary()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);

        var badMobile = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/contacts", new { name = "Ali", mobile1 = "12345" });
        Assert.Equal(HttpStatusCode.BadRequest, badMobile.StatusCode);

        var first = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/contacts", new { name = "Ali", mobile1 = "0300-1234567", isPrimary = true, purpose = new[] { "Billing" } });
        first.EnsureSuccessStatusCode();
        var firstId = (await first.DataAsync()).GetProperty("contact").GetProperty("customerContactId").GetInt64();

        var second = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/contacts", new { name = "Sana", mobile1 = "03001234568", isPrimary = true });
        second.EnsureSuccessStatusCode();

        var list = await (await w.Admin.GetAsync($"/api/customers/{customerId}/contacts")).DataAsync();
        var rows = list.EnumerateArray().ToList();
        Assert.Single(rows, r => r.GetProperty("isPrimary").GetBoolean());   // moving primary to Sana un-primaried Ali
        Assert.False(rows.First(r => r.GetProperty("customerContactId").GetInt64() == firstId).GetProperty("isPrimary").GetBoolean());
    }

    [Fact]
    public async Task Deactivating_the_last_active_contact_warns_but_still_succeeds()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var created = await (await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/contacts", new { name = "Ali", mobile1 = "0300-1234567" })).DataAsync();
        var contactId = created.GetProperty("contact").GetProperty("customerContactId").GetInt64();

        var deactivated = await (await w.Admin.PostAsync($"/api/customer-contacts/{contactId}/deactivate", null)).DataAsync();
        Assert.NotEmpty(deactivated.GetProperty("warnings").EnumerateArray());

        var list = await (await w.Admin.GetAsync($"/api/customers/{customerId}/contacts")).DataAsync();
        Assert.Empty(list.EnumerateArray());   // default list hides inactive
        var withInactive = await (await w.Admin.GetAsync($"/api/customers/{customerId}/contacts?includeInactive=true")).DataAsync();
        Assert.Single(withInactive.EnumerateArray());
    }

    [Fact]
    public async Task A_billing_address_defaults_to_pakistan_and_only_one_can_be_the_default()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var cities = await (await w.Admin.GetAsync("/api/lookups/CITY")).DataAsync();
        var lahore = cities.EnumerateArray().First().GetProperty("id").GetInt32();

        var head = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/billing-addresses",
            new { addressName = "Head Office", addressLine1 = "Mall Road", cityId = lahore, isDefault = true });
        head.EnsureSuccessStatusCode();
        var headData = await head.DataAsync();
        Assert.True(headData.GetProperty("isDefault").GetBoolean());
        Assert.True(headData.GetProperty("countryId").GetInt32() > 0);

        var plant2 = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/billing-addresses",
            new { addressName = "Plant 2", addressLine1 = "Industrial Area", cityId = lahore, isDefault = true });
        plant2.EnsureSuccessStatusCode();

        var list = await (await w.Admin.GetAsync($"/api/customers/{customerId}/billing-addresses")).DataAsync();
        var rows = list.EnumerateArray().ToList();
        Assert.Single(rows, r => r.GetProperty("isDefault").GetBoolean());
        Assert.Equal("Plant 2", rows.First(r => r.GetProperty("isDefault").GetBoolean()).GetProperty("addressName").GetString());

        var duplicateName = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/billing-addresses",
            new { addressName = "Plant 2", addressLine1 = "Somewhere else", cityId = lahore });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateName.StatusCode);
    }

    [Fact]
    public async Task An_inactive_address_cannot_be_made_the_default_and_effective_to_cannot_precede_effective_from()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var cities = await (await w.Admin.GetAsync("/api/lookups/CITY")).DataAsync();
        var cityId = cities.EnumerateArray().First().GetProperty("id").GetInt32();

        var badRange = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/billing-addresses",
            new { addressName = "HO", addressLine1 = "Line 1", cityId, effectiveFrom = "2026-09-25", effectiveTo = "2026-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, badRange.StatusCode);

        var created = await (await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/billing-addresses",
            new { addressName = "HO", addressLine1 = "Line 1", cityId })).DataAsync();
        var addressId = created.GetProperty("customerBillingAddressId").GetInt64();
        await w.Admin.PostAsync($"/api/customer-billing-addresses/{addressId}/deactivate", null);

        var setDefault = await w.Admin.PostAsync($"/api/customer-billing-addresses/{addressId}/set-default", null);
        Assert.Equal(HttpStatusCode.Conflict, setDefault.StatusCode);
    }

    [Fact]
    public async Task The_activation_checklist_now_blocks_until_a_contact_and_a_default_address_both_exist()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customer = await (await w.Admin.PostAsJsonAsync("/api/customers", new { customerName = "Checklist Co", addressLine1 = "Line 1" })).DataAsync();
        var customerId = customer.GetProperty("customerId").GetInt32();
        var rowVersion = customer.GetProperty("rowVersion").GetString();

        var checkBefore = await (await w.Admin.GetAsync($"/api/customers/{customerId}/activation-check")).DataAsync();
        Assert.False(checkBefore.GetProperty("canActivate").GetBoolean());
        // All four of §10's checklist items (contact, default address, billing configuration — CC-05, invoice
        // template — CC-07) are now registered; each task's own tests cover its own item in isolation.
        Assert.Equal(4, checkBefore.GetProperty("missingItems").GetArrayLength());

        var blocked = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/activate", new { rowVersion });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        // An Admin override reason bypasses the checklist (§10).
        var overridden = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/activate", new { rowVersion, reason = "Approved by sales lead pending paperwork" });
        overridden.EnsureSuccessStatusCode();
        var afterOverride = await (await w.Admin.GetAsync($"/api/customers/{customerId}")).DataAsync();
        Assert.Equal("Active", afterOverride.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_activation_checklist_passes_once_all_four_items_exist()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customer = await (await w.Admin.PostAsJsonAsync("/api/customers", new { customerName = "Ready Co", addressLine1 = "Line 1" })).DataAsync();
        var customerId = customer.GetProperty("customerId").GetInt32();

        (await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/contacts", new { name = "Ali", mobile1 = "0300-1234567" })).EnsureSuccessStatusCode();
        var cities = await (await w.Admin.GetAsync("/api/lookups/CITY")).DataAsync();
        var cityId = cities.EnumerateArray().First().GetProperty("id").GetInt32();
        (await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/billing-addresses",
            new { addressName = "HO", addressLine1 = "Line 1", cityId, isDefault = true })).EnsureSuccessStatusCode();
        // Opening (or ever saving) the Billing Configuration tab creates its default row (§13) — CC-05's own
        // requirement checks for the row's existence, not the values in it.
        (await w.Admin.GetAsync($"/api/customers/{customerId}/billing-configuration")).EnsureSuccessStatusCode();
        // An active invoice template (§15, CC-07) — the fourth and last checklist item.
        var template = await (await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/invoice-templates", new { templateName = "Standard" })).DataAsync();
        (await w.Admin.PostAsJsonAsync($"/api/customer-invoice-templates/{template.GetProperty("customerInvoiceTemplateId").GetInt64()}/activate", new { })).EnsureSuccessStatusCode();

        var check = await (await w.Admin.GetAsync($"/api/customers/{customerId}/activation-check")).DataAsync();
        Assert.True(check.GetProperty("canActivate").GetBoolean());

        var activated = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString() });
        activated.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task View_needs_view_permission_edit_actions_need_edit_permission()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var viewer = w.As("Viewer", 5, PermissionCodes.TRP_CUSTOMER_VIEW);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/customers/{customerId}/contacts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/customers/{customerId}/contacts", new { name = "X", mobile1 = "0300-1234567" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/customers/{customerId}/billing-addresses", new { addressName = "X", addressLine1 = "Y", cityId = 1 })).StatusCode);
    }
}
