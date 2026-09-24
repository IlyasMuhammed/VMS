using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.BusinessPartners;

/// <summary>S1-BP-14, 15, 17: the partner list, the picker other screens use, and the Excel export.</summary>
[Collection(ApiCollection.Name)]
public sealed class PartnerListTests(ApiFactory factory)
{
    private static int Id(JsonObject partner) => partner["id"]!.GetValue<int>();

    private static async Task<(List<JsonElement> Items, int Total)> ListAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/partners" + query);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var data = await response.DataAsync();
        return (data.GetProperty("items").EnumerateArray().ToList(), data.GetProperty("totalCount").GetInt32());
    }

    private static List<string> Codes(IEnumerable<JsonElement> items) => items.Select(i => i.GetProperty("bpCode").GetString()!).ToList();

    // ── List ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_shows_the_tenants_partners_newest_change_first_25_to_a_page()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var other = await PartnerWorld.CreateAsync(factory);
        await other.CreateAsync(other.Person());
        for (var n = 0; n < 27; n++) await w.CreateAsync(w.Person());

        var (first, total) = await ListAsync(w.Admin);
        var (second, _) = await ListAsync(w.Admin, "?page=2");

        Assert.Equal(27, total);
        Assert.Equal(25, first.Count);
        Assert.Equal(2, second.Count);
        Assert.EndsWith("00027", first[0].GetProperty("bpCode").GetString());   // the last one saved is first
    }

    [Fact]
    public async Task Editing_a_partner_moves_it_to_the_top()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var oldest = await w.CreateAsync(w.Person());
        await w.CreateAsync(w.Person());
        oldest["notes"] = "touched";
        await w.PutAsync(oldest);

        var (items, _) = await ListAsync(w.Admin);

        Assert.Equal(oldest["bpCode"]!.GetValue<string>(), items[0].GetProperty("bpCode").GetString());
    }

    [Fact]
    public async Task Each_row_carries_its_roles_and_the_name_of_its_city()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Company(roles: ["Vendor", "Workshop"]);
        await w.CreateAsync(body);

        var (items, _) = await ListAsync(w.Admin);

        var row = Assert.Single(items);
        Assert.Equal(["Vendor", "Workshop"], row.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.False(string.IsNullOrEmpty(row.GetProperty("city").GetString()));
        Assert.Equal("Active", row.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_page_size_is_capped()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        await w.CreateAsync(w.Person());

        var data = await (await w.Admin.GetAsync("/api/partners?pageSize=100000")).DataAsync();

        Assert.Equal(100, data.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task A_search_needs_three_characters()
    {
        var w = await PartnerWorld.CreateAsync(factory);

        await PartnerWorld.AssertRefusedAsync(await w.Admin.GetAsync("/api/partners?search=ab"), "search", Msg.MinLength);
    }

    [Fact]
    public async Task The_search_finds_by_code_name_cnic_ntn_and_mobile_with_or_without_the_dashes()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var person = await w.CreateAsync(w.Person("Shahid Mehmood Sons"));
        var company = await w.CreateAsync(w.Company("Crescent Lubricants Pvt Ltd"));
        await w.CreateAsync(w.Person());

        async Task<List<string>> Find(string term) => Codes((await ListAsync(w.Admin, "?search=" + Uri.EscapeDataString(term))).Items);
        var personCode = person["bpCode"]!.GetValue<string>();
        var companyCode = company["bpCode"]!.GetValue<string>();

        Assert.Equal([personCode], await Find(personCode));
        Assert.Equal([personCode], await Find("mehmood"));                                      // partial, any case
        Assert.Equal([companyCode], await Find("lubric"));
        Assert.Equal([personCode], await Find(person["cnic"]!.GetValue<string>()));
        Assert.Equal([personCode], await Find(person["cnic"]!.GetValue<string>().Replace("-", "")));
        Assert.Equal([companyCode], await Find(company["ntn"]!.GetValue<string>()));
        Assert.Equal([personCode], await Find(person["primaryMobile"]!.GetValue<string>()));
        Assert.Equal([personCode], await Find(person["primaryMobile"]!.GetValue<string>().Replace("-", "")));
        Assert.Empty(await Find("nothing like this"));
    }

    [Fact]
    public async Task A_partner_is_still_found_by_a_name_it_used_to_have()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person("Bismillah General Store"));
        partner["legalName"] = "Al Madina General Store";
        Assert.Equal(HttpStatusCode.OK, (await w.PutAsync(partner)).StatusCode);

        var (byOld, _) = await ListAsync(w.Admin, "?search=bismillah");
        var (byNew, _) = await ListAsync(w.Admin, "?search=madina");

        Assert.Equal(Codes(byNew), Codes(byOld));
        Assert.Single(byOld);
    }

    [Fact]
    public async Task The_list_filters_by_role_status_city_and_party_type()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var workshop = await w.CreateAsync(w.Person());
        var vendor = await w.CreateAsync(w.Company(roles: "Vendor"));
        var inOtherCity = w.Person(); inOtherCity["cityId"] = w.OtherCity;
        var farAway = await w.CreateAsync(inOtherCity);
        await w.Admin.PostAsJsonAsync($"/api/partners/{Id(vendor)}/status", new { status = "Inactive" });

        Assert.Equal(2, (await ListAsync(w.Admin, "?roles=Workshop")).Total);
        Assert.Equal(3, (await ListAsync(w.Admin, "?roles=Workshop&roles=Vendor")).Total);
        Assert.Equal(1, (await ListAsync(w.Admin, "?status=Inactive")).Total);
        Assert.Equal(2, (await ListAsync(w.Admin, "?status=Active")).Total);
        Assert.Equal([farAway["bpCode"]!.GetValue<string>()], Codes((await ListAsync(w.Admin, $"?cityId={w.OtherCity}")).Items));
        Assert.Equal([vendor["bpCode"]!.GetValue<string>()], Codes((await ListAsync(w.Admin, "?partyType=Company")).Items));
        Assert.Equal(0, (await ListAsync(w.Admin, "?roles=Bank")).Total);
        Assert.NotNull(workshop);
    }

    [Fact]
    public async Task A_role_a_partner_no_longer_holds_does_not_match_the_filter()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Company(roles: ["Vendor", "Workshop"]));
        await w.Admin.DeleteAsync($"/api/partners/{Id(partner)}/roles/Vendor");

        Assert.Equal(0, (await ListAsync(w.Admin, "?roles=Vendor")).Total);
        Assert.Equal(1, (await ListAsync(w.Admin, "?roles=Workshop")).Total);
    }

    [Fact]
    public async Task The_list_sorts_on_the_columns_offered_and_ignores_any_other()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        foreach (var name in new[] { "Charlie Traders", "Alpha Motors", "Bravo Tyres" }) await w.CreateAsync(w.Person(name));

        var names = async (string sort) => (await ListAsync(w.Admin, "?sort=" + sort)).Items.Select(i => i.GetProperty("legalName").GetString()).ToList();

        Assert.Equal(["Alpha Motors", "Bravo Tyres", "Charlie Traders"], await names("legalName,asc"));
        Assert.Equal(["Charlie Traders", "Bravo Tyres", "Alpha Motors"], await names("legalName,desc"));
        Assert.Equal(["Charlie Traders", "Alpha Motors", "Bravo Tyres"], await names("bpCode,asc"));
        var response = await w.Admin.GetAsync("/api/partners?sort=Cnic;DROP TABLE x,asc");   // an unknown column is not an error and not run
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_list_needs_the_view_permission_and_shows_nothing_of_another_tenant()
    {
        var mine = await PartnerWorld.CreateAsync(factory);
        var other = await PartnerWorld.CreateAsync(factory);
        var theirs = await other.CreateAsync(other.Person("Hidden Away Holdings"));

        Assert.Equal(0, (await ListAsync(mine.Admin, "?search=hidden")).Total);
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.As("Nobody", 9, PermissionCodes.BP_CREATE).GetAsync("/api/partners")).StatusCode);
        Assert.NotNull(theirs);
    }

    /// <summary>A bank needs an email on the partner.</summary>
    private static JsonObject Bank(PartnerWorld w) { var body = w.Person(role: "Bank"); body["email"] = "bank@example.pk"; return body; }

    // ── Picker ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_picker_offers_active_partners_of_a_role_and_nobody_else()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var active = await w.CreateAsync(Bank(w));
        await w.CreateAsync(w.Person(role: "Workshop"));
        var inactive = await w.CreateAsync(Bank(w));
        var blacklisted = await w.CreateAsync(Bank(w));
        await w.Admin.PostAsJsonAsync($"/api/partners/{Id(inactive)}/status", new { status = "Inactive" });
        await w.Admin.PostAsJsonAsync($"/api/partners/{Id(blacklisted)}/status", new { status = "Blacklisted", reason = "Not to be dealt with again" });

        var picked = (await (await w.Admin.GetAsync("/api/partners/picker?role=Bank")).DataAsync()).EnumerateArray().ToList();

        var one = Assert.Single(picked);
        Assert.Equal(Id(active), one.GetProperty("id").GetInt32());
        Assert.False(string.IsNullOrEmpty(one.GetProperty("city").GetString()));
        Assert.Equal(2, (await (await w.Admin.GetAsync("/api/partners/picker")).DataAsync()).GetArrayLength());   // no role asked: every active partner
    }

    [Fact]
    public async Task The_picker_narrows_by_what_is_typed_and_stops_at_the_number_asked_for()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        foreach (var name in new[] { "Pak Tyres Centre", "Pak Lubricants", "Lahore Springs" }) await w.CreateAsync(w.Person(name));

        var pak = (await (await w.Admin.GetAsync("/api/partners/picker?search=pak")).DataAsync()).EnumerateArray().ToList();
        var capped = (await (await w.Admin.GetAsync("/api/partners/picker?take=1")).DataAsync()).EnumerateArray().ToList();

        Assert.Equal(2, pak.Count);
        Assert.Single(capped);
        Assert.Equal(["Pak Lubricants", "Pak Tyres Centre"], pak.Select(p => p.GetProperty("displayName").GetString()));   // in name order
    }

    [Fact]
    public async Task The_picker_needs_the_view_permission_and_offers_nothing_of_another_tenant()
    {
        var mine = await PartnerWorld.CreateAsync(factory);
        var other = await PartnerWorld.CreateAsync(factory);
        await other.CreateAsync(other.Person());

        Assert.Equal(0, (await (await mine.Admin.GetAsync("/api/partners/picker")).DataAsync()).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.As("Nobody", 9).GetAsync("/api/partners/picker")).StatusCode);
    }

    // ── Export ──────────────────────────────────────────────────────────────────────

    private static async Task<IXLWorksheet> ExportAsync(HttpClient client, string query = "", string? zone = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/partners/export" + query);
        if (zone is not null) request.Headers.Add("X-Time-Zone", zone);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);
        Assert.Matches(@"^business-partners-\d{8}-\d{4}\.xlsx$", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        return workbook.Worksheet(1);
    }

    private static List<string> Headers(IXLWorksheet sheet) => sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();

    [Fact]
    public async Task The_export_holds_every_partner_the_filter_matches_not_just_a_page()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        for (var n = 0; n < 30; n++) await w.CreateAsync(w.Person());
        await w.CreateAsync(w.Company(roles: "Vendor"));

        var all = await ExportAsync(w.Admin);
        var vendors = await ExportAsync(w.Admin, "?roles=Vendor&page=2&pageSize=5");   // paging is ignored: it is the whole result

        Assert.Equal(31, all.RowsUsed().Count() - 1);
        Assert.Equal(1, vendors.RowsUsed().Count() - 1);
        Assert.Equal("Vendor", vendors.Cell(2, Headers(vendors).IndexOf("Roles") + 1).GetString());
    }

    [Fact]
    public async Task The_export_leaves_out_the_opening_balance_for_someone_who_may_not_see_it()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person(); body["openingBalance"] = 12345.5; body["openingBalanceDate"] = "2026-01-01";
        await w.CreateAsync(body);
        var clerk = w.As("Clerk", 3, PermissionCodes.BP_VIEW, PermissionCodes.BP_EXPORT);

        var full = await ExportAsync(w.Admin);
        var limited = await ExportAsync(clerk);

        Assert.Contains("Opening balance", Headers(full));
        Assert.Equal(12345.5, full.Cell(2, Headers(full).IndexOf("Opening balance") + 1).GetDouble());
        Assert.DoesNotContain("Opening balance", Headers(limited));
        Assert.DoesNotContain(limited.CellsUsed(), c => c.GetString().Contains("12345"));
    }

    [Fact]
    public async Task The_export_shows_times_in_the_zone_of_the_person_exporting_and_says_which()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        await w.CreateAsync(w.Person());
        var stored = factory.Scalar<DateTime>("SELECT ModifiedOn FROM bp.BusinessPartners WHERE TenantId = @t", ("@t", w.Tenant));

        var karachi = await ExportAsync(w.Admin, zone: "Asia/Karachi");
        var utc = await ExportAsync(w.Admin, zone: "UTC");

        var column = Headers(karachi).FindIndex(h => h.StartsWith("Last modified")) + 1;
        Assert.Contains("Asia/Karachi", karachi.Cell(1, column).GetString());
        Assert.Equal(stored.AddHours(5).ToString("yyyy-MM-dd HH:mm"), karachi.Cell(2, column).GetDateTime().ToString("yyyy-MM-dd HH:mm"));
        Assert.Equal(stored.ToString("yyyy-MM-dd HH:mm"), utc.Cell(2, column).GetDateTime().ToString("yyyy-MM-dd HH:mm"));
    }

    [Fact]
    public async Task A_name_that_looks_like_a_formula_is_exported_as_text()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        await w.CreateAsync(w.Person("=HYPERLINK(\"http://evil.example\",\"x\")"));

        var sheet = await ExportAsync(w.Admin);

        var cell = sheet.Cell(2, Headers(sheet).IndexOf("Legal name") + 1);
        Assert.False(cell.HasFormula);
        Assert.Equal(XLDataType.Text, cell.DataType);
    }

    [Fact]
    public async Task Exporting_needs_the_export_permission()
    {
        var w = await PartnerWorld.CreateAsync(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await w.As("Viewer", 5, PermissionCodes.BP_VIEW).GetAsync("/api/partners/export")).StatusCode);
    }
}
