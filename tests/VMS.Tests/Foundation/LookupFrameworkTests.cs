using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Authorization;
using VMS.Shared.Lookups;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-12: the lookup master framework: lists that start from defaults, then belong to the tenant.</summary>
[Collection(ApiCollection.Name)]
public sealed class LookupFrameworkTests(ApiFactory factory)
{
    // Two lists only tests use, covering every kind of extra field.
    private static readonly LookupTypeDefinition Shade = new("TEST_SHADE", "Test Shade", "Shades.",
        Defaults: [new("LIGHT", "Light"), new("DARK", "Dark")]);

    private static readonly LookupTypeDefinition Paint = new("TEST_PAINT", "Test Paint", "Paints.",
    [
        new("glossy", "Glossy", LookupAttributeKind.Flag),
        new("coats", "Coats", LookupAttributeKind.Number),
        new("note", "Note", LookupAttributeKind.Text),
        new("shade", "Shade", LookupAttributeKind.Lookup, LookupType: "TEST_SHADE"),
    ]);

    private static readonly LookupTypeDefinition RequiredNote = new("TEST_REQUIRED", "Test Required", "Needs a note.",
        [new("note", "Note", LookupAttributeKind.Text, Required: true)]);

    private readonly WebApplicationFactory<Program> _host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        s.AddLookupType(Shade);
        s.AddLookupType(Paint);
        s.AddLookupType(RequiredNote);
    }));

    private sealed record Who(Guid Tenant, HttpClient Admin, HttpClient Reader);

    /// <summary>A tenant of its own, with an administrator and an ordinary user.</summary>
    private Who NewTenant()
    {
        var tenant = factory.CreateTenant();
        var admin = _host.CreateClient().WithToken(TestTokens.For(tenant, "Master Admin", 9500, PermissionCodes.ADM_MASTER_MANAGE));
        var reader = _host.CreateClient().WithToken(TestTokens.For(tenant, "Plain User", 9501));
        return new Who(tenant, admin, reader);
    }

    private static string Unique(string prefix = "V") => prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.DataAsync();
    }

    private static List<string> Codes(JsonElement list) => list.EnumerateArray().Select(v => v.GetProperty("code").GetString()!).ToList();

    private static Task<HttpResponseMessage> Create(HttpClient admin, string type, object body) => admin.PostAsJsonAsync($"/api/admin/lookups/{type}", body);

    // ── Defaults ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_list_starts_from_its_defaults_the_first_time_it_is_used()
    {
        var who = NewTenant();

        var list = await Data(await who.Reader.GetAsync("/api/lookups/VEHICLE_TYPE"));

        var values = list.EnumerateArray().ToList();
        Assert.Equal(9, values.Count);
        Assert.Equal(["TRUCK", "TRAILER", "PRIME_MOVER", "TANKER", "PICKUP", "BUS", "CAR", "BIKE", "OTHER"], Codes(list));
        Assert.Equal(new[] { 10, 20, 30, 40, 50, 60, 70, 80, 90 }, values.Select(v => v.GetProperty("sortOrder").GetInt32()));
        Assert.All(values, v => Assert.True(v.GetProperty("isActive").GetBoolean()));
        Assert.Equal("Prime Mover", values[2].GetProperty("description").GetString());
    }

    [Fact]
    public async Task Any_signed_in_user_can_read_a_list_but_not_change_it()
    {
        var who = NewTenant();

        Assert.Equal(HttpStatusCode.OK, (await who.Reader.GetAsync("/api/lookups/MAKE")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await who.Reader.GetAsync("/api/admin/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await who.Reader.GetAsync("/api/admin/lookups/MAKE")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Create(who.Reader, "MAKE", new { code = "X", description = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await who.Reader.PutAsJsonAsync("/api/admin/lookups/MAKE/1", new { description = "X", sortOrder = 1, isActive = true })).StatusCode);

        using var anonymous = _host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/lookups/MAKE")).StatusCode);
    }

    [Fact]
    public async Task Every_tenant_gets_a_copy_of_its_own()
    {
        var a = NewTenant();
        var b = NewTenant();
        var listA = await Data(await a.Admin.GetAsync("/api/admin/lookups/MAKE"));
        var hino = listA.EnumerateArray().First(v => v.GetProperty("code").GetString() == "HINO");

        (await a.Admin.PutAsJsonAsync($"/api/admin/lookups/MAKE/{hino.GetProperty("id").GetInt32()}",
            new { description = "Hino Motors", sortOrder = 10, isActive = false })).EnsureSuccessStatusCode();

        Assert.DoesNotContain("HINO", Codes(await Data(await a.Reader.GetAsync("/api/lookups/MAKE"))));
        Assert.Contains("HINO", Codes(await Data(await b.Reader.GetAsync("/api/lookups/MAKE"))));
    }

    [Fact]
    public async Task What_a_tenant_changed_is_never_put_back_by_the_defaults()
    {
        var who = NewTenant();
        var list = await Data(await who.Admin.GetAsync("/api/admin/lookups/VEHICLE_TYPE"));
        var truck = list.EnumerateArray().First(v => v.GetProperty("code").GetString() == "TRUCK");
        var id = truck.GetProperty("id").GetInt32();

        (await who.Admin.PutAsJsonAsync($"/api/admin/lookups/VEHICLE_TYPE/{id}", new { description = "Heavy Truck", sortOrder = 5, isActive = false })).EnsureSuccessStatusCode();
        var again = await Data(await who.Admin.GetAsync("/api/admin/lookups/VEHICLE_TYPE"));

        Assert.Equal(9, again.GetArrayLength());   // not re-seeded
        var changed = again.EnumerateArray().First(v => v.GetProperty("code").GetString() == "TRUCK");
        Assert.Equal("Heavy Truck", changed.GetProperty("description").GetString());
        Assert.False(changed.GetProperty("isActive").GetBoolean());
        Assert.Equal("TRUCK", Codes(again)[0]);   // sort order 5 puts it first
    }

    [Fact]
    public async Task Preparing_a_list_is_not_logged_as_something_a_person_did()
    {
        var who = NewTenant();

        await who.Reader.GetAsync("/api/lookups/BODY_TYPE");

        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM core.AuditEntries WHERE TenantId = @t AND Entity = 'LookupValue'", ("@t", who.Tenant)));
    }

    [Fact]
    public async Task Many_first_requests_at_once_still_produce_one_copy_of_the_list()
    {
        var who = NewTenant();

        var responses = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => who.Reader.GetAsync("/api/lookups/EXPENSE_TYPE")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(13, factory.Scalar<int>("SELECT COUNT(*) FROM core.LookupValues WHERE TenantId = @t AND LookupType = 'EXPENSE_TYPE'", ("@t", who.Tenant)));
    }

    // ── The list of lists ───────────────────────────────────────────────────────────

    [Fact]
    public async Task The_administration_screen_gets_every_list_with_its_fields_and_counts()
    {
        var who = NewTenant();

        var types = (await Data(await who.Admin.GetAsync("/api/admin/lookups"))).EnumerateArray().ToList();

        var codes = types.Select(t => t.GetProperty("code").GetString()).ToList();
        Assert.Contains("VEHICLE_TYPE", codes);
        Assert.Contains("CITY", codes);
        Assert.Contains("TEST_PAINT", codes);   // lists a module registers appear too
        var vehicleType = types.First(t => t.GetProperty("code").GetString() == "VEHICLE_TYPE");
        Assert.Equal("Vehicle Type", vehicleType.GetProperty("name").GetString());
        Assert.Equal(9, vehicleType.GetProperty("totalCount").GetInt32());
        Assert.Equal(9, vehicleType.GetProperty("activeCount").GetInt32());

        var city = types.First(t => t.GetProperty("code").GetString() == "CITY");
        var province = Assert.Single(city.GetProperty("attributes").EnumerateArray());
        Assert.Equal("province", province.GetProperty("key").GetString());
        Assert.Equal("Lookup", province.GetProperty("kind").GetString());
        Assert.True(province.GetProperty("required").GetBoolean());
        Assert.Equal("PROVINCE", province.GetProperty("lookupType").GetString());
    }

    [Fact]
    public async Task Retiring_a_value_shows_in_the_counts()
    {
        var who = NewTenant();
        var list = await Data(await who.Admin.GetAsync("/api/admin/lookups/INCOME_TYPE"));
        var id = list[0].GetProperty("id").GetInt32();

        (await who.Admin.PutAsJsonAsync($"/api/admin/lookups/INCOME_TYPE/{id}", new { description = "Monthly hire", sortOrder = 10, isActive = false })).EnsureSuccessStatusCode();

        var types = await Data(await who.Admin.GetAsync("/api/admin/lookups"));
        var income = types.EnumerateArray().First(t => t.GetProperty("code").GetString() == "INCOME_TYPE");
        Assert.Equal(4, income.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, income.GetProperty("activeCount").GetInt32());
    }

    [Fact]
    public async Task A_list_that_does_not_exist_is_not_found_everywhere()
    {
        var who = NewTenant();

        Assert.Equal(HttpStatusCode.NotFound, (await who.Reader.GetAsync("/api/lookups/NOPE")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await who.Admin.GetAsync("/api/admin/lookups/NOPE")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Create(who.Admin, "NOPE", new { code = "X", description = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await who.Admin.PutAsJsonAsync("/api/admin/lookups/NOPE/1", new { description = "X", sortOrder = 1, isActive = true })).StatusCode);
    }

    // ── Adding and editing ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_new_value_goes_to_the_end_of_the_list_with_its_code_in_capitals()
    {
        var who = NewTenant();
        var code = Unique();

        var response = await Create(who.Admin, "BODY_TYPE", new { code = code.ToLowerInvariant(), description = "  Curtain Side  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.DataAsync();
        Assert.Equal(code, created.GetProperty("code").GetString());
        Assert.Equal("Curtain Side", created.GetProperty("description").GetString());
        Assert.Equal(70, created.GetProperty("sortOrder").GetInt32());   // six defaults, 10 to 60, so the next is 70
        Assert.True(created.GetProperty("isActive").GetBoolean());
        Assert.Equal(code, Codes(await Data(await who.Reader.GetAsync("/api/lookups/BODY_TYPE")))[^1]);
    }

    [Fact]
    public async Task A_value_can_be_placed_where_the_caller_says()
    {
        var who = NewTenant();

        var created = await Data(await Create(who.Admin, "BODY_TYPE", new { code = Unique(), description = "First of all", sortOrder = 1 }));

        Assert.Equal(1, created.GetProperty("sortOrder").GetInt32());
        Assert.Equal("First of all", (await Data(await who.Reader.GetAsync("/api/lookups/BODY_TYPE")))[0].GetProperty("description").GetString());
    }

    [Fact]
    public async Task A_code_is_unique_within_its_list_but_may_repeat_in_another()
    {
        var who = NewTenant();
        var code = Unique();
        (await Create(who.Admin, "MAKE", new { code, description = "One" })).EnsureSuccessStatusCode();

        var duplicate = await Create(who.Admin, "MAKE", new { code = code.ToLowerInvariant(), description = "Two" });
        var elsewhere = await Create(who.Admin, "BODY_TYPE", new { code, description = "Same code, other list" });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("already exists", await duplicate.MessageAsync());
        Assert.Equal(HttpStatusCode.Created, elsewhere.StatusCode);
    }

    [Theory]
    [InlineData("", "Fine")]
    [InlineData("HAS SPACE", "Fine")]
    [InlineData("dash-ed", "Fine")]
    [InlineData("_LEADING", "Fine")]
    [InlineData("THIS_CODE_IS_FAR_TOO_LONG_TO_ACCEPT", "Fine")]
    [InlineData("OKCODE", "")]
    [InlineData("OKCODE", "   ")]
    public async Task A_bad_code_or_an_empty_description_is_refused(string code, string description)
    {
        var who = NewTenant();

        var response = await Create(who.Admin, "MAKE", new { code, description });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_description_that_is_too_long_or_a_sort_order_out_of_range_is_refused()
    {
        var who = NewTenant();

        Assert.Equal(HttpStatusCode.BadRequest, (await Create(who.Admin, "MAKE", new { code = Unique(), description = new string('x', 201) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(who.Admin, "MAKE", new { code = Unique(), description = "Fine", sortOrder = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(who.Admin, "MAKE", new { code = Unique(), description = "Fine", sortOrder = 10000 })).StatusCode);
    }

    [Fact]
    public async Task A_value_can_be_renamed_reordered_and_retired_but_its_code_never_changes()
    {
        var who = NewTenant();
        var created = await Data(await Create(who.Admin, "MAKE", new { code = Unique("M"), description = "Old name" }));
        var id = created.GetProperty("id").GetInt32();
        var code = created.GetProperty("code").GetString();

        var response = await who.Admin.PutAsJsonAsync($"/api/admin/lookups/MAKE/{id}", new { code = "SNEAKY", description = "New name", sortOrder = 999, isActive = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.DataAsync();
        Assert.Equal(code, saved.GetProperty("code").GetString());   // the code in the request is ignored
        Assert.Equal("New name", saved.GetProperty("description").GetString());
        Assert.Equal(999, saved.GetProperty("sortOrder").GetInt32());
        Assert.False(saved.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task A_retired_value_can_be_brought_back()
    {
        var who = NewTenant();
        var created = await Data(await Create(who.Admin, "MAKE", new { code = Unique("M"), description = "Comes and goes" }));
        var id = created.GetProperty("id").GetInt32();
        var code = created.GetProperty("code").GetString();

        await who.Admin.PutAsJsonAsync($"/api/admin/lookups/MAKE/{id}", new { description = "Comes and goes", sortOrder = 10, isActive = false });
        Assert.DoesNotContain(code, Codes(await Data(await who.Reader.GetAsync("/api/lookups/MAKE"))));

        await who.Admin.PutAsJsonAsync($"/api/admin/lookups/MAKE/{id}", new { description = "Comes and goes", sortOrder = 10, isActive = true });
        Assert.Contains(code, Codes(await Data(await who.Reader.GetAsync("/api/lookups/MAKE"))));
    }

    [Fact]
    public async Task A_value_in_another_list_or_another_tenant_is_not_found()
    {
        var mine = NewTenant();
        var theirs = NewTenant();
        var theirValue = (await Data(await theirs.Admin.GetAsync("/api/admin/lookups/MAKE")))[0].GetProperty("id").GetInt32();
        var body = new { description = "Hijacked", sortOrder = 1, isActive = false };

        Assert.Equal(HttpStatusCode.NotFound, (await mine.Admin.PutAsJsonAsync($"/api/admin/lookups/MAKE/{theirValue}", body)).StatusCode);   // another tenant's
        var myBodyType = (await Data(await mine.Admin.GetAsync("/api/admin/lookups/BODY_TYPE")))[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await mine.Admin.PutAsJsonAsync($"/api/admin/lookups/MAKE/{myBodyType}", body)).StatusCode);   // another list's
        Assert.Equal(HttpStatusCode.NotFound, (await mine.Admin.PutAsJsonAsync("/api/admin/lookups/MAKE/999999", body)).StatusCode);
    }

    [Fact]
    public async Task What_is_changed_is_audited_with_who_and_from_what_to_what()
    {
        var who = NewTenant();
        var made = await Data(await Create(who.Admin, "MAKE", new { code = Unique("M"), description = "Before" }));
        var id = made.GetProperty("id").GetInt32();

        await who.Admin.PutAsJsonAsync($"/api/admin/lookups/MAKE/{id}", new { description = "After", sortOrder = made.GetProperty("sortOrder").GetInt32(), isActive = true });

        var rows = factory.Query(
            "SELECT Action, Field, OldValue, NewValue, UserName FROM core.AuditEntries WHERE TenantId = @t AND Entity = 'LookupValue' AND RecordId = @r ORDER BY AuditEntryID",
            ("@t", who.Tenant), ("@r", id.ToString()));
        Assert.Equal("Created", rows[0]["Action"]);
        var rename = Assert.Single(rows, r => (string?)r["Field"] == "Description");
        Assert.Equal("Before", rename["OldValue"]);
        Assert.Equal("After", rename["NewValue"]);
        Assert.Equal("Master Admin", rename["UserName"]);
    }

    // ── Extra fields ────────────────────────────────────────────────────────────────

    private static object Paint1(object? attributes = null) => new { code = Unique("P"), description = "A paint", attributes };

    [Fact]
    public async Task Extra_fields_are_stored_in_their_normal_form()
    {
        var who = NewTenant();

        var created = await Data(await Create(who.Admin, "TEST_PAINT", Paint1(new { glossy = "TRUE", coats = "3", note = "  two tone  ", shade = "dark" })));

        var attributes = created.GetProperty("attributes");
        Assert.Equal("true", attributes.GetProperty("glossy").GetString());
        Assert.Equal("3", attributes.GetProperty("coats").GetString());
        Assert.Equal("two tone", attributes.GetProperty("note").GetString());
        Assert.Equal("DARK", attributes.GetProperty("shade").GetString());
    }

    [Fact]
    public async Task A_flag_left_out_means_no_and_other_fields_left_out_are_simply_absent()
    {
        var who = NewTenant();

        var created = await Data(await Create(who.Admin, "TEST_PAINT", Paint1()));

        var attributes = created.GetProperty("attributes");
        Assert.Equal("false", attributes.GetProperty("glossy").GetString());
        Assert.False(attributes.TryGetProperty("coats", out _));
        Assert.False(attributes.TryGetProperty("shade", out _));
    }

    [Theory]
    [InlineData("glossy", "maybe")]
    [InlineData("coats", "-1")]
    [InlineData("coats", "1.5")]
    [InlineData("coats", "three")]
    [InlineData("shade", "MISSING")]
    [InlineData("colour", "red")]   // not a field of this list
    public async Task A_field_that_is_not_valid_for_its_kind_is_refused(string key, string value)
    {
        var who = NewTenant();

        var response = await Create(who.Admin, "TEST_PAINT", Paint1(new Dictionary<string, string> { [key] = value }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Long_text_and_a_missing_required_field_are_refused()
    {
        var who = NewTenant();

        Assert.Equal(HttpStatusCode.BadRequest, (await Create(who.Admin, "TEST_PAINT", Paint1(new { note = new string('x', 201) }))).StatusCode);

        var required = await Create(who.Admin, "TEST_REQUIRED", new { code = Unique(), description = "Needs a note" });
        Assert.Equal(HttpStatusCode.BadRequest, required.StatusCode);
        Assert.Equal("Note is required.", await required.MessageAsync());
        Assert.Equal(HttpStatusCode.Created, (await Create(who.Admin, "TEST_REQUIRED", new { code = Unique(), description = "Has one", attributes = new { note = "yes" } })).StatusCode);
    }

    [Fact]
    public async Task A_field_that_points_at_another_list_must_point_at_a_value_that_can_still_be_chosen()
    {
        var who = NewTenant();
        var shades = await Data(await who.Admin.GetAsync("/api/admin/lookups/TEST_SHADE"));
        var light = shades.EnumerateArray().First(s => s.GetProperty("code").GetString() == "LIGHT").GetProperty("id").GetInt32();
        (await who.Admin.PutAsJsonAsync($"/api/admin/lookups/TEST_SHADE/{light}", new { description = "Light", sortOrder = 10, isActive = false })).EnsureSuccessStatusCode();

        var response = await Create(who.Admin, "TEST_PAINT", Paint1(new { shade = "LIGHT" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);   // retired: exists, but not choosable
        Assert.Equal("'LIGHT' is not an active Test Shade.", await response.MessageAsync());
    }

    [Fact]
    public async Task A_value_that_another_list_still_uses_cannot_be_retired_until_that_is_dealt_with()
    {
        var who = NewTenant();
        var shades = await Data(await who.Admin.GetAsync("/api/admin/lookups/TEST_SHADE"));
        var dark = shades.EnumerateArray().First(s => s.GetProperty("code").GetString() == "DARK").GetProperty("id").GetInt32();
        var paint = await Data(await Create(who.Admin, "TEST_PAINT", Paint1(new { shade = "DARK" })));
        var paintId = paint.GetProperty("id").GetInt32();

        var blocked = await who.Admin.PutAsJsonAsync($"/api/admin/lookups/TEST_SHADE/{dark}", new { description = "Dark", sortOrder = 20, isActive = false });

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("Dark cannot be retired: 1 active Test Paint value uses it. Move or retire those first.", await blocked.MessageAsync());

        // Retire the paint, and the shade is free to go.
        await who.Admin.PutAsJsonAsync($"/api/admin/lookups/TEST_PAINT/{paintId}", new { description = "A paint", sortOrder = 10, isActive = false, attributes = new { shade = "DARK" } });
        var allowed = await who.Admin.PutAsJsonAsync($"/api/admin/lookups/TEST_SHADE/{dark}", new { description = "Dark", sortOrder = 20, isActive = false });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Editing_a_value_can_change_its_extra_fields()
    {
        var who = NewTenant();
        var paint = await Data(await Create(who.Admin, "TEST_PAINT", Paint1(new { coats = "2" })));
        var id = paint.GetProperty("id").GetInt32();

        var saved = await Data(await who.Admin.PutAsJsonAsync($"/api/admin/lookups/TEST_PAINT/{id}",
            new { description = "A paint", sortOrder = 10, isActive = true, attributes = new { coats = "4", glossy = "true" } }));

        Assert.Equal("4", saved.GetProperty("attributes").GetProperty("coats").GetString());
        Assert.Equal("true", saved.GetProperty("attributes").GetProperty("glossy").GetString());
    }

    // ── What other modules use ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_module_can_check_a_chosen_value_is_real_and_says_whether_it_can_still_be_chosen()
    {
        var who = NewTenant();
        var make = await Data(await Create(who.Admin, "MAKE", new { code = Unique("M"), description = "Checked" }));
        var id = make.GetProperty("id").GetInt32();
        using var probeHost = factory.With<LookupProbeController>().Build();
        using var probe = probeHost.CreateClient().WithToken(TestTokens.For(who.Tenant, "Module", 9502));

        async Task<(bool Found, bool Active)> Ask(string type, int value)
        {
            var body = JsonDocument.Parse(await probe.GetStringAsync($"/test/lookups/{type}/{value}")).RootElement;
            return (body.GetProperty("found").GetBoolean(), body.GetProperty("active").GetBoolean());
        }

        Assert.Equal((true, true), await Ask("MAKE", id));
        await who.Admin.PutAsJsonAsync($"/api/admin/lookups/MAKE/{id}", new { description = "Checked", sortOrder = 10, isActive = false });
        Assert.Equal((true, false), await Ask("MAKE", id));                 // a record made last year still finds it
        Assert.Equal((false, false), await Ask("BODY_TYPE", id));           // right id, wrong list
        Assert.Equal((false, false), await Ask("MAKE", 999_999));
        var other = NewTenant();
        var otherId = (await Data(await other.Admin.GetAsync("/api/admin/lookups/MAKE")))[0].GetProperty("id").GetInt32();
        Assert.Equal((false, false), await Ask("MAKE", otherId));           // another tenant's value
    }

    [Fact]
    public async Task One_value_can_be_fetched_retired_or_not_but_only_from_its_own_list_and_tenant()
    {
        var who = NewTenant();
        var made = await Data(await Create(who.Admin, "MAKE", new { code = Unique("M"), description = "Fetched" }));
        var id = made.GetProperty("id").GetInt32();
        await who.Admin.PutAsJsonAsync($"/api/admin/lookups/MAKE/{id}", new { description = "Fetched", sortOrder = 10, isActive = false });
        var other = NewTenant();

        var retired = await Data(await who.Reader.GetAsync($"/api/lookups/MAKE/{id}"));

        Assert.Equal("Fetched", retired.GetProperty("description").GetString());
        Assert.False(retired.GetProperty("isActive").GetBoolean());   // the picker uses this to label "(retired)"
        Assert.Equal(HttpStatusCode.NotFound, (await who.Reader.GetAsync($"/api/lookups/BODY_TYPE/{id}")).StatusCode);   // right id, wrong list
        Assert.Equal(HttpStatusCode.NotFound, (await other.Reader.GetAsync($"/api/lookups/MAKE/{id}")).StatusCode);      // another tenant's
        Assert.Equal(HttpStatusCode.NotFound, (await who.Reader.GetAsync("/api/lookups/MAKE/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await who.Reader.GetAsync($"/api/lookups/NOPE/{id}")).StatusCode);
    }

    // ── Definitions are checked at start-up ─────────────────────────────────────────

    private static string StartWith(ApiFactory factory, params LookupTypeDefinition[] extra)
    {
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            foreach (var d in extra) s.AddLookupType(d);
        }));
        var ex = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        for (var e = ex; e is not null; e = e.InnerException)
            if (e.Message.Contains("lookup definitions are invalid", StringComparison.Ordinal)) return e.Message;
        throw new Xunit.Sdk.XunitException("Start-up failed, but not because of the definitions: " + ex);
    }

    [Fact]
    public void Two_lists_with_one_code_stop_the_application_starting()
    {
        var message = StartWith(factory, new LookupTypeDefinition("VEHICLE_TYPE", "Duplicate", "Clashes with the platform's."));

        Assert.Contains("Lookup type 'VEHICLE_TYPE' is defined twice.", message);
    }

    [Fact]
    public void A_field_that_points_at_a_list_that_does_not_exist_stops_the_application_starting()
    {
        var message = StartWith(factory, new LookupTypeDefinition("TEST_DANGLING", "Dangling", "Points nowhere.",
            [new LookupAttribute("target", "Target", LookupAttributeKind.Lookup, LookupType: "NO_SUCH_LIST")]));

        Assert.Contains("unknown lookup type 'NO_SUCH_LIST'", message);
    }

    [Fact]
    public void A_default_the_list_does_not_allow_stops_the_application_starting()
    {
        var message = StartWith(factory,
            new LookupTypeDefinition("TEST_BADSEED", "Bad seeds", "Bad defaults.",
                Defaults: [new("has space", "Bad code"), new("DUP", "One"), new("DUP", "Two"), new("EXTRA", "Field it lacks", new Dictionary<string, string?> { ["nope"] = "x" })]));

        Assert.Contains("'has space' is not a valid code", message);
        Assert.Contains("'DUP' appears twice", message);
        Assert.Contains("attribute 'nope' the type does not define", message);
    }
}
