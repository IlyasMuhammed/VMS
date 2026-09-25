using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-07: customer invoice templates — registry and versioning (§15, AC-07, AC-08).</summary>
[Collection(ApiCollection.Name)]
public sealed class CustomerInvoiceTemplateTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<int> NewCustomerAsync(HttpClient admin) =>
        (await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "12 Mall Road" })).DataAsync())
        .GetProperty("customerId").GetInt32();

    private static async Task<long> ActiveTemplateAsync(HttpClient admin, int customerId, string name, string? effectiveFrom = null)
    {
        var created = await (await admin.PostAsJsonAsync($"/api/customers/{customerId}/invoice-templates", new { templateName = name })).DataAsync();
        var id = created.GetProperty("customerInvoiceTemplateId").GetInt64();
        (await admin.PostAsJsonAsync($"/api/customer-invoice-templates/{id}/activate", new { effectiveFrom })).EnsureSuccessStatusCode();
        return id;
    }

    [Fact]
    public async Task A_new_template_defaults_to_systemstandard_and_starts_as_draft()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var created = await (await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/invoice-templates", new { templateName = "Standard" })).DataAsync();
        Assert.Equal("Draft", created.GetProperty("status").GetString());
        Assert.Equal("SystemStandard", created.GetProperty("templateType").GetString());
        Assert.Equal(1, created.GetProperty("version").GetInt32());
        Assert.False(string.IsNullOrEmpty(created.GetProperty("templateReference").GetString()));
    }

    [Fact]
    public async Task AC_07_exactly_one_applicable_template_needs_no_selection()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        await ActiveTemplateAsync(w.Admin, customerId, "Standard");

        var applicable = await (await w.Admin.GetAsync($"/api/customers/{customerId}/invoice-templates/applicable?invoiceDate={Day()}")).DataAsync();
        Assert.False(applicable.GetProperty("requiresSelection").GetBoolean());
        Assert.Single(applicable.GetProperty("templates").EnumerateArray());
    }

    [Fact]
    public async Task AC_08_two_applicable_templates_require_selection()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        await ActiveTemplateAsync(w.Admin, customerId, "Detailed Format");
        await ActiveTemplateAsync(w.Admin, customerId, "Summary Format");

        var applicable = await (await w.Admin.GetAsync($"/api/customers/{customerId}/invoice-templates/applicable?invoiceDate={Day()}")).DataAsync();
        Assert.True(applicable.GetProperty("requiresSelection").GetBoolean());
        Assert.Equal(2, applicable.GetProperty("templates").GetArrayLength());
    }

    [Fact]
    public async Task No_applicable_template_returns_an_empty_list()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var applicable = await (await w.Admin.GetAsync($"/api/customers/{customerId}/invoice-templates/applicable?invoiceDate={Day()}")).DataAsync();
        Assert.False(applicable.GetProperty("requiresSelection").GetBoolean());
        Assert.Empty(applicable.GetProperty("templates").EnumerateArray());
    }

    [Fact]
    public async Task A_new_version_supersedes_the_active_one_only_once_it_is_itself_activated()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var v1Id = await ActiveTemplateAsync(w.Admin, customerId, "Standard");

        var draftV2 = await (await w.Admin.PostAsJsonAsync($"/api/customer-invoice-templates/{v1Id}/new-version", new { })).DataAsync();
        Assert.Equal(2, draftV2.GetProperty("version").GetInt32());
        Assert.Equal("Draft", draftV2.GetProperty("status").GetString());

        // v1 is still the only Active/applicable one while v2 is a draft.
        var stillOnlyV1 = await (await w.Admin.GetAsync($"/api/customers/{customerId}/invoice-templates/applicable?invoiceDate={Day()}")).DataAsync();
        Assert.Single(stillOnlyV1.GetProperty("templates").EnumerateArray());
        Assert.Equal(1, stillOnlyV1.GetProperty("templates")[0].GetProperty("version").GetInt32());

        var v2Id = draftV2.GetProperty("customerInvoiceTemplateId").GetInt64();
        var laterDate = Day(10);
        (await w.Admin.PostAsJsonAsync($"/api/customer-invoice-templates/{v2Id}/activate", new { effectiveFrom = laterDate })).EnsureSuccessStatusCode();

        var v1After = (await (await w.Admin.GetAsync($"/api/customers/{customerId}/invoice-templates?includeInactive=true")).DataAsync())
            .EnumerateArray().First(t => t.GetProperty("customerInvoiceTemplateId").GetInt64() == v1Id);
        Assert.Equal("Inactive", v1After.GetProperty("status").GetString());
        Assert.Equal(Day(9), v1After.GetProperty("effectiveTo").GetString());

        // §15 is explicit here, unlike §14's tax rules: "Applicable = Active and effective" — a superseded
        // template stops being applicable to *new* invoice generation even for a date inside its old range.
        // "Re-printing an old invoice uses that version" is a plain lookup of the invoice's own stored
        // CustomerInvoiceTemplateId/Version (once Invoice exists in a later CC task), not a re-resolution here.
        var afterSupersede = await (await w.Admin.GetAsync($"/api/customers/{customerId}/invoice-templates/applicable?invoiceDate={Day(5)}")).DataAsync();
        Assert.Empty(afterSupersede.GetProperty("templates").EnumerateArray());
    }

    [Fact]
    public async Task Only_one_template_can_be_the_customers_default_across_every_name()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var oneId = await ActiveTemplateAsync(w.Admin, customerId, "Detailed Format");
        var twoId = await ActiveTemplateAsync(w.Admin, customerId, "Summary Format");

        await w.Admin.PostAsync($"/api/customer-invoice-templates/{oneId}/set-default", null);
        (await w.Admin.PostAsync($"/api/customer-invoice-templates/{twoId}/set-default", null)).EnsureSuccessStatusCode();

        var list = await (await w.Admin.GetAsync($"/api/customers/{customerId}/invoice-templates")).DataAsync();
        Assert.Single(list.EnumerateArray(), t => t.GetProperty("isDefault").GetBoolean());
        Assert.True(list.EnumerateArray().First(t => t.GetProperty("customerInvoiceTemplateId").GetInt64() == twoId).GetProperty("isDefault").GetBoolean());
    }

    [Fact]
    public async Task A_draft_only_template_can_be_deactivated_and_a_deactivated_one_cannot_be_the_default()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var id = await ActiveTemplateAsync(w.Admin, customerId, "Standard");
        (await w.Admin.PostAsync($"/api/customer-invoice-templates/{id}/deactivate", null)).EnsureSuccessStatusCode();

        var setDefault = await w.Admin.PostAsync($"/api/customer-invoice-templates/{id}/set-default", null);
        Assert.Equal(HttpStatusCode.Conflict, setDefault.StatusCode);

        var applicable = await (await w.Admin.GetAsync($"/api/customers/{customerId}/invoice-templates/applicable?invoiceDate={Day()}")).DataAsync();
        Assert.Empty(applicable.GetProperty("templates").EnumerateArray());
    }

    [Fact]
    public async Task View_needs_view_permission_edit_needs_template_permission()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var viewer = w.As("Viewer", 5, PermissionCodes.TRP_CUSTOMER_VIEW);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/customers/{customerId}/invoice-templates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/customers/{customerId}/invoice-templates", new { templateName = "X" })).StatusCode);
    }
}
