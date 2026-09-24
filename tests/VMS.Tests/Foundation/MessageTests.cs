using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Messages;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-18 (API side): the message catalogue and the one shape every rejection takes.</summary>
[Collection(ApiCollection.Name)]
public sealed class MessageTests(ApiFactory factory)
{
    private IMessageCatalogue Catalogue()
    {
        factory.CreateClient();
        return factory.Services.GetRequiredService<IMessageCatalogue>();
    }

    // ── The catalogue is the FSD's ──────────────────────────────────────────────────

    /// <summary>Reads the message tables (§12.2 and §22.2) from the FSD in the repository.</summary>
    private static Dictionary<string, string> FsdMessages()
    {
        var root = typeof(MessageTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryRoot").Value!;
        var file = Directory.GetFiles(Path.Combine(root, "Document"), "VMS Phase 1 FSD*.md").Single();
        var messages = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(file))
        {
            var match = Regex.Match(line, @"^\| (?<id>VAL-(?:BP|VH)-\d{3}) \| (?<when>[^|]*) \| (?<text>.+) \|$");
            if (match.Success) messages[match.Groups["id"].Value] = match.Groups["text"].Value.Trim();
        }
        return messages;
    }

    [Fact]
    public void Every_message_the_fsd_lists_is_in_the_catalogue_word_for_word()
    {
        var fsd = FsdMessages();
        var catalogue = Catalogue().All();

        Assert.Equal(34, fsd.Count);   // 16 partner and 18 vehicle messages
        foreach (var (id, text) in fsd)
        {
            // The FSD writes "{Bank / Lessor / Customer / Sharing partner}" to mean "whichever of these applies";
            // the catalogue names it {Counterparty} so the code can fill it in.
            var expected = text.Replace("{Bank / Lessor / Customer / Sharing partner}", "{Counterparty}");
            Assert.True(catalogue.TryGetValue(id, out var actual), $"{id} is missing from the catalogue.");
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void The_message_ids_the_code_uses_and_the_ones_in_the_file_are_the_same_set()
    {
        var constants = typeof(Msg).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        var inFile = Catalogue().All().Keys.ToList();

        Assert.Equal(constants.Order(), inFile.Order());
        Assert.Equal(constants.Count, constants.Distinct().Count());
    }

    [Fact]
    public void Every_message_is_plain_text_with_only_simple_placeholders()
    {
        foreach (var (id, text) in Catalogue().All())
        {
            Assert.False(string.IsNullOrWhiteSpace(text), id);
            Assert.DoesNotContain("<", text);   // no markup: the screen shows it as text
            Assert.Equal(text.Count(c => c == '{'), text.Count(c => c == '}'));
        }
    }

    // ── Filling in placeholders ─────────────────────────────────────────────────────

    [Fact]
    public void Placeholders_are_filled_by_name()
    {
        var text = Catalogue().Text(Msg.BpCnicUsed, ("BPCode", "BP-26-00147"), ("LegalName", "Ali Traders"));

        Assert.Equal("This CNIC belongs to BP-26-00147 — Ali Traders. Open that record instead.", text);
    }

    [Fact]
    public void A_placeholder_used_twice_is_filled_both_times_and_extra_values_are_ignored()
    {
        Assert.Equal("a and a", MessageFormat.Format("{x} and {x}", new Dictionary<string, string?> { ["x"] = "a", ["unused"] = "b" }));
    }

    [Fact]
    public void A_missing_value_is_a_programming_error_not_a_message_with_braces_in_it()
    {
        var ex = Assert.Throws<ArgumentException>(() => Catalogue().Text(Msg.BpCnicUsed, ("BPCode", "BP-1")));

        Assert.Contains("{LegalName}", ex.Message);
    }

    [Fact]
    public void An_unknown_message_id_is_a_programming_error()
    {
        Assert.Throws<ArgumentException>(() => Catalogue().Text("VAL-XX-999"));
    }

    [Fact]
    public void Braces_that_are_not_placeholders_are_left_alone_and_a_null_value_is_empty()
    {
        Assert.Equal("Keep {this thing} and () here", MessageFormat.Format("Keep {this thing} and ({v}) here", new Dictionary<string, string?> { ["v"] = null }));
        Assert.Equal(["a", "b"], MessageFormat.Placeholders("{a} {b} {a} {not one}"));
    }

    [Fact]
    public void Values_are_written_the_same_way_in_every_culture()
    {
        var text = MessageFormat.Format("{n} at {p}", MessageFormat.Values([("n", 1234.5m), ("p", 0.25)]));

        Assert.Equal("1234.5 at 0.25", text);
    }

    // ── Serving and rewording ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_screen_can_fetch_the_catalogue_without_signing_in()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.DataAsync();
        Assert.Equal("en", data.GetProperty("locale").GetString());
        Assert.Equal("Enter CNIC as 00000-0000000-0.", data.GetProperty("messages").GetProperty("VAL-BP-002").GetString());
        Assert.Equal(Catalogue().All().Count, data.GetProperty("messages").EnumerateObject().Count());
        Assert.Contains("max-age", response.Headers.CacheControl!.ToString());
    }

    private sealed class Wording : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "vms-messages-" + Guid.NewGuid().ToString("N")[..10]);
        public Wording() => System.IO.Directory.CreateDirectory(Directory);
        public void Write(string locale, string json) => File.WriteAllText(Path.Combine(Directory, $"messages.{locale}.json"), json, new UTF8Encoding(false));
        public void Dispose() { try { System.IO.Directory.Delete(Directory, true); } catch { /* temp folder */ } }
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithWording(Wording w) =>
        factory.WithWebHostBuilder(b => b.UseSetting("Messages:Directory", w.Directory));

    private static async Task<JsonElement> Messages(HttpClient client, string locale = "en") =>
        (await (await client.GetAsync($"/api/messages?locale={locale}")).DataAsync()).GetProperty("messages");

    [Fact]
    public async Task A_message_can_be_reworded_by_a_file_without_touching_code()
    {
        using var wording = new Wording();
        wording.Write("en", """{ "VAL-BP-005": "Type the mobile number like 03XX-XXXXXXX." }""");
        using var host = WithWording(wording);
        using var client = host.CreateClient();

        var messages = await Messages(client);

        Assert.Equal("Type the mobile number like 03XX-XXXXXXX.", messages.GetProperty("VAL-BP-005").GetString());
        Assert.Equal("Enter CNIC as 00000-0000000-0.", messages.GetProperty("VAL-BP-002").GetString());   // the rest is untouched
        Assert.Equal("Type the mobile number like 03XX-XXXXXXX.", host.Services.GetRequiredService<IMessageCatalogue>().Text(Msg.BpMobileFormat));   // and the API says it too
    }

    [Fact]
    public async Task A_translation_covers_what_it_covers_and_the_rest_stays_in_english()
    {
        using var wording = new Wording();
        wording.Write("ur", """{ "VAL-BP-001": "کم از کم ایک کردار منتخب کریں۔" }""");
        using var host = WithWording(wording);
        using var client = host.CreateClient();

        var urdu = await Messages(client, "ur");
        var english = await Messages(client, "en");

        Assert.Equal("کم از کم ایک کردار منتخب کریں۔", urdu.GetProperty("VAL-BP-001").GetString());
        Assert.Equal("Enter CNIC as 00000-0000000-0.", urdu.GetProperty("VAL-BP-002").GetString());
        Assert.Equal("Select at least one role for this business partner.", english.GetProperty("VAL-BP-001").GetString());
    }

    [Fact]
    public async Task An_unknown_or_nonsense_language_falls_back_to_english()
    {
        using var client = factory.CreateClient();

        foreach (var locale in new[] { "xx", "../../etc", "en;drop", "" })
            Assert.Equal("Enter CNIC as 00000-0000000-0.", (await Messages(client, Uri.EscapeDataString(locale))).GetProperty("VAL-BP-002").GetString());
    }

    [Fact]
    public async Task A_rewording_that_would_leave_a_placeholder_unfilled_or_a_broken_file_is_ignored_not_fatal()
    {
        using var wording = new Wording();
        // {Extra} is not a value the code supplies for VAL-BP-002, so that rewording is dropped; the good one stays.
        wording.Write("en", """{ "VAL-BP-002": "Use {Extra} format", "VAL-BP-001": "Pick a role.", "VAL-NOPE-1": "unknown id" }""");
        wording.Write("fr", "{ this is not json");
        using var host = WithWording(wording);
        using var client = host.CreateClient();

        var english = await Messages(client);
        var french = await Messages(client, "fr");

        Assert.Equal("Enter CNIC as 00000-0000000-0.", english.GetProperty("VAL-BP-002").GetString());
        Assert.Equal("Pick a role.", english.GetProperty("VAL-BP-001").GetString());
        Assert.False(english.TryGetProperty("VAL-NOPE-1", out _));
        Assert.Equal("Pick a role.", french.GetProperty("VAL-BP-001").GetString());   // a broken file is skipped; English carries on
    }

    [Fact]
    public async Task A_rewording_that_uses_fewer_placeholders_is_fine()
    {
        using var wording = new Wording();
        wording.Write("en", """{ "VAL-BP-003": "That CNIC is taken by {BPCode}." }""");
        using var host = WithWording(wording);

        var text = host.Services.GetRequiredService<IMessageCatalogue>().Text(Msg.BpCnicUsed, ("BPCode", "BP-1"), ("LegalName", "Ali"));

        Assert.Equal("That CNIC is taken by BP-1.", text);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task An_edit_to_the_file_is_picked_up_without_restarting()
    {
        using var wording = new Wording();
        wording.Write("en", """{ "VAL-BP-005": "First wording." }""");
        using var host = WithWording(wording);
        using var client = host.CreateClient();
        Assert.Equal("First wording.", (await Messages(client)).GetProperty("VAL-BP-005").GetString());

        wording.Write("en", """{ "VAL-BP-005": "Second wording." }""");
        File.SetLastWriteTimeUtc(Path.Combine(wording.Directory, "messages.en.json"), DateTime.UtcNow.AddSeconds(5));

        Assert.Equal("Second wording.", (await Messages(client)).GetProperty("VAL-BP-005").GetString());
    }

    // ── One shape for every rejection ───────────────────────────────────────────────

    private async Task<(HttpResponseMessage Response, JsonElement Body)> ProbeAsync(Func<HttpClient, Task<HttpResponseMessage>> call)
    {
        using var host = factory.With<ValidationProbeController>().Build();
        using var client = host.CreateClient();
        client.WithToken((await client.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword)).AccessToken);
        var response = await call(client);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response, doc.RootElement.Clone());
    }

    [Fact]
    public async Task A_validation_error_is_a_400_with_the_field_the_id_the_text_and_the_values()
    {
        var (response, body) = await ProbeAsync(c => c.GetAsync("/test/validation/one"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("This CNIC belongs to BP-26-00147 — Ali Traders. Open that record instead.", body.GetProperty("message").GetString());
        var error = Assert.Single(body.GetProperty("errors").EnumerateArray());
        Assert.Equal("cnic", error.GetProperty("field").GetString());
        Assert.Equal("VAL-BP-003", error.GetProperty("code").GetString());
        Assert.Equal("This CNIC belongs to BP-26-00147 — Ali Traders. Open that record instead.", error.GetProperty("message").GetString());
        Assert.Equal("BP-26-00147", error.GetProperty("params").GetProperty("BPCode").GetString());
        Assert.Equal("Ali Traders", error.GetProperty("params").GetProperty("LegalName").GetString());
    }

    [Fact]
    public async Task Every_problem_is_reported_at_once_and_one_can_belong_to_the_form_as_a_whole()
    {
        var (_, body) = await ProbeAsync(c => c.GetAsync("/test/validation/many"));

        var errors = body.GetProperty("errors").EnumerateArray().ToList();
        Assert.Equal(["VAL-BP-002", "VAL-BP-005", "VAL-BP-001"], errors.Select(e => e.GetProperty("code").GetString()));
        Assert.Equal("cnic", errors[0].GetProperty("field").GetString());
        Assert.False(errors[2].TryGetProperty("field", out _));   // no field: the form as a whole
        Assert.False(errors[0].TryGetProperty("params", out _));  // nothing was filled in
    }

    [Fact]
    public async Task A_plain_rejection_keeps_its_old_shape_with_no_errors_list()
    {
        var (response, body) = await ProbeAsync(c => c.GetAsync("/test/validation/plain"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Just a message.", body.GetProperty("message").GetString());
        Assert.False(body.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task The_frameworks_own_validation_answers_in_the_same_shape_with_catalogue_wording()
    {
        var (response, body) = await ProbeAsync(c => c.PostAsJsonAsync("/test/validation/body",
            new { code = "TOOLONGCODE", email = "not-an-email", address = new { city = "Lahore" } }));   // legalName is missing

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(body.GetProperty("success").GetBoolean());
        var errors = body.GetProperty("errors").EnumerateArray().ToDictionary(e => e.GetProperty("field").GetString()!);
        Assert.Equal("VAL-GEN-001", errors["legalName"].GetProperty("code").GetString());
        Assert.Equal("Legal name is required.", errors["legalName"].GetProperty("message").GetString());
        Assert.Equal("VAL-GEN-003", errors["code"].GetProperty("code").GetString());
        Assert.Equal("Code can be at most 5 characters.", errors["code"].GetProperty("message").GetString());
        Assert.Equal("VAL-GEN-002", errors["email"].GetProperty("code").GetString());
        Assert.Equal("Please correct the highlighted fields.", body.GetProperty("message").GetString());   // several problems: a summary
    }

    [Fact]
    public async Task A_date_in_the_wrong_shape_names_the_field_and_says_what_is_expected()
    {
        // The JSON serialiser stops at the first value it cannot read, so each shape error is its own request.
        var (_, instant) = await ProbeAsync(c => c.PostAsync("/test/validation/body", new StringContent(
            """{ "legalName": "Ali", "at": "2026-10-10T14:30:00" }""", Encoding.UTF8, "application/json")));
        var (_, date) = await ProbeAsync(c => c.PostAsync("/test/validation/body", new StringContent(
            """{ "legalName": "Ali", "day": "10/10/2026" }""", Encoding.UTF8, "application/json")));

        var atError = Assert.Single(instant.GetProperty("errors").EnumerateArray());
        Assert.Equal("at", atError.GetProperty("field").GetString());
        Assert.Contains("ISO 8601 with a UTC offset", atError.GetProperty("message").GetString());   // our own converter's words
        var dayError = Assert.Single(date.GetProperty("errors").EnumerateArray());
        Assert.Equal("day", dayError.GetProperty("field").GetString());
        Assert.Equal("Day is not valid.", dayError.GetProperty("message").GetString());              // the serialiser's words replaced
        Assert.DoesNotContain("LineNumber", date.ToString());
        Assert.DoesNotContain("BytePosition", date.ToString());
    }

    [Fact]
    public async Task A_body_that_is_not_json_at_all_still_gets_a_useful_answer()
    {
        var (response, body) = await ProbeAsync(c => c.PostAsync("/test/validation/body", new StringContent("not json {", Encoding.UTF8, "application/json")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.NotEmpty(body.GetProperty("errors").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }

    [Theory]
    [InlineData("LegalName", "legalName")]
    [InlineData("Address.City", "address.city")]
    [InlineData("$.legalName", "legalName")]
    [InlineData("$.address.city", "address.city")]
    [InlineData("$", null)]
    [InlineData("", null)]
    public void Field_names_are_reported_in_the_requests_own_spelling(string key, string? expected)
    {
        Assert.Equal(expected, ModelStateErrors.FieldName(key));
    }

    [Theory]
    [InlineData("legalName", "Legal name")]
    [InlineData("address.postalCode", "Postal code")]
    [InlineData("cnic", "Cnic")]
    [InlineData(null, "This field")]
    public void A_field_name_reads_as_a_label_when_only_the_name_is_known(string? field, string expected)
    {
        Assert.Equal(expected, ModelStateErrors.Humanize(field));
    }
}
