using System.Net;
using System.Net.Http.Json;
using VMS.Shared.Authorization;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-06: customer tax/deduction rules (§14) — replacement-by-same-name, overlap rejection, applicable-at-date resolution.</summary>
[Collection(ApiCollection.Name)]
public sealed class CustomerTaxRuleTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<int> NewCustomerAsync(HttpClient admin) =>
        (await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "12 Mall Road" })).DataAsync())
        .GetProperty("customerId").GetInt32();

    [Fact]
    public async Task AC_05_a_new_rule_with_the_same_tax_name_auto_inactivates_the_old_one_and_history_keeps_its_own_rate()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);

        var oldRule = await (await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules",
            new { taxName = "Income Tax WHT", taxCode = "WHT-236", taxType = "Percentage", taxPercentage = 2, calculationBasis = "GrossTripAmount", sequence = 1, effectiveFrom = Day(-200) })).DataAsync();
        var oldRuleId = oldRule.GetProperty("customerTaxRuleId").GetInt64();

        // First attempt: needs confirmation, not yet a hard error.
        var needsConfirm = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules",
            new { taxName = "Income Tax WHT", taxCode = "WHT-236", taxType = "Percentage", taxPercentage = 3, calculationBasis = "GrossTripAmount", sequence = 1, effectiveFrom = Day(0) });
        Assert.Equal((HttpStatusCode)422, needsConfirm.StatusCode);
        Assert.Contains("TAX_RULE_REPLACEMENT_CONFIRMATION", await needsConfirm.Content.ReadAsStringAsync());

        var confirmed = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules",
            new { taxName = "Income Tax WHT", taxCode = "WHT-236", taxType = "Percentage", taxPercentage = 3, calculationBasis = "GrossTripAmount", sequence = 1, effectiveFrom = Day(0), confirmReplace = true });
        confirmed.EnsureSuccessStatusCode();
        var newRule = await confirmed.DataAsync();
        Assert.Equal(oldRuleId, newRule.GetProperty("supersedesRuleId").GetInt64());

        var oldAfter = (await (await w.Admin.GetAsync($"/api/customers/{customerId}/tax-rules?includeInactive=true")).DataAsync())
            .EnumerateArray().First(r => r.GetProperty("customerTaxRuleId").GetInt64() == oldRuleId);
        Assert.Equal("Inactive", oldAfter.GetProperty("status").GetString());
        Assert.Equal(Day(-1), oldAfter.GetProperty("effectiveTo").GetString());

        // Invoices dated before the switch still resolve the 2% rule; on/after resolve the 3% one.
        var beforeSwitch = await (await w.Admin.GetAsync($"/api/customers/{customerId}/tax-rules/applicable?invoiceDate={Day(-1)}")).DataAsync();
        Assert.Equal(2, beforeSwitch.EnumerateArray().Single().GetProperty("taxPercentage").GetDecimal());

        var onSwitch = await (await w.Admin.GetAsync($"/api/customers/{customerId}/tax-rules/applicable?invoiceDate={Day(0)}")).DataAsync();
        Assert.Equal(3, onSwitch.EnumerateArray().Single().GetProperty("taxPercentage").GetDecimal());
    }

    [Fact]
    public async Task AC_06_a_new_rule_starting_on_or_before_the_current_ones_start_is_rejected()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        (await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules",
            new { taxName = "WHT", taxCode = "WHT-236", taxType = "Percentage", taxPercentage = 2, calculationBasis = "GrossTripAmount", sequence = 1, effectiveFrom = Day(-30) })).EnsureSuccessStatusCode();

        var sameDay = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules",
            new { taxName = "WHT", taxCode = "WHT-236", taxType = "Percentage", taxPercentage = 3, calculationBasis = "GrossTripAmount", sequence = 1, effectiveFrom = Day(-30) });
        Assert.Equal(HttpStatusCode.BadRequest, sameDay.StatusCode);
        Assert.Contains("VAL-TRP-010", await sameDay.Content.ReadAsStringAsync());

        var earlier = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules",
            new { taxName = "WHT", taxCode = "WHT-236", taxType = "Percentage", taxPercentage = 3, calculationBasis = "GrossTripAmount", sequence = 1, effectiveFrom = Day(-60) });
        Assert.Equal(HttpStatusCode.BadRequest, earlier.StatusCode);
    }

    [Fact]
    public async Task A_customer_can_hold_several_different_rules_active_at_once()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        (await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules",
            new { taxName = "Income Tax WHT", taxCode = "WHT-236", taxType = "Percentage", taxPercentage = 2, calculationBasis = "GrossTripAmount", sequence = 1 })).EnsureSuccessStatusCode();
        (await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules",
            new { taxName = "Sales Tax WHT", taxCode = "ST-153", taxType = "Fixed", fixedAmount = 500, calculationBasis = "InvoiceSubtotal", sequence = 2 })).EnsureSuccessStatusCode();

        var applicable = await (await w.Admin.GetAsync($"/api/customers/{customerId}/tax-rules/applicable?invoiceDate={Day()}")).DataAsync();
        Assert.Equal(2, applicable.GetArrayLength());
    }

    [Fact]
    public async Task The_rate_matching_the_tax_type_is_required_and_within_range()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);

        var missingPercentage = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules",
            new { taxName = "X", taxCode = "X1", taxType = "Percentage", calculationBasis = "GrossTripAmount", sequence = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, missingPercentage.StatusCode);

        var tooHigh = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules",
            new { taxName = "X", taxCode = "X1", taxType = "Percentage", taxPercentage = 150, calculationBasis = "GrossTripAmount", sequence = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, tooHigh.StatusCode);

        var missingFixed = await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules",
            new { taxName = "Y", taxCode = "Y1", taxType = "Fixed", calculationBasis = "GrossTripAmount", sequence = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, missingFixed.StatusCode);
    }

    [Fact]
    public async Task Updating_a_rule_changes_its_rate_and_flags_but_not_its_identity()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var created = await (await w.Admin.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules",
            new { taxName = "WHT", taxCode = "WHT-236", taxType = "Percentage", taxPercentage = 2, calculationBasis = "GrossTripAmount", sequence = 1 })).DataAsync();
        var ruleId = created.GetProperty("customerTaxRuleId").GetInt64();

        var updated = await (await w.Admin.PutAsJsonAsync($"/api/customer-tax-rules/{ruleId}",
            new { taxPercentage = 2.5, applicable = false, calculationBasis = "InvoiceSubtotal", sequence = 5, remarks = "Paused for review" })).DataAsync();
        Assert.Equal(2.5m, updated.GetProperty("taxPercentage").GetDecimal());
        Assert.False(updated.GetProperty("applicable").GetBoolean());
        Assert.Equal("InvoiceSubtotal", updated.GetProperty("calculationBasis").GetString());
        Assert.Equal("WHT", updated.GetProperty("taxName").GetString());   // identity untouched
    }

    [Fact]
    public async Task View_needs_view_permission_edit_needs_taxrule_permission()
    {
        var w = await TripsWorld.CreateAsync(factory);
        var customerId = await NewCustomerAsync(w.Admin);
        var viewer = w.As("Viewer", 5, PermissionCodes.TRP_CUSTOMER_VIEW);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/customers/{customerId}/tax-rules")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/customers/{customerId}/tax-rules",
            new { taxName = "X", taxCode = "X1", taxType = "Fixed", fixedAmount = 10, calculationBasis = "GrossTripAmount", sequence = 1 })).StatusCode);
    }
}
