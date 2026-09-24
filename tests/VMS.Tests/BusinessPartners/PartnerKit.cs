using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Authorization;
using VMS.Shared.Partners;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.BusinessPartners;

/// <summary>A tenant of its own with a signed-in user for each set of permissions a test needs, and the bodies to send.</summary>
internal sealed class PartnerWorld
{
    private static int _counter = 1000;

    public static readonly string[] Everything =
    [
        PermissionCodes.BP_VIEW, PermissionCodes.BP_CREATE, PermissionCodes.BP_EDIT, PermissionCodes.BP_ROLE_MANAGE,
        PermissionCodes.BP_STATUS_CHANGE, PermissionCodes.BP_EXPORT,
        PermissionCodes.BP_FIELD_SALARY_VIEW, PermissionCodes.BP_FIELD_CREDIT_VIEW, PermissionCodes.BP_FIELD_OPENING_VIEW,
        PermissionCodes.DOC_VIEW, PermissionCodes.DOC_UPLOAD, PermissionCodes.DOC_RENEW, PermissionCodes.DOC_REJECT, PermissionCodes.DOC_DOWNLOAD, PermissionCodes.DOC_DOWNLOAD_SENSITIVE, PermissionCodes.DOC_REGISTER_VIEW,
    ];

    private readonly WebApplicationFactory<Program> _host;
    public ApiFactory Factory { get; }
    public Guid Tenant { get; }
    public int City { get; private set; }
    public int OtherCity { get; private set; }
    /// <summary>Someone who can do everything.</summary>
    public HttpClient Admin { get; private set; } = null!;

    private PartnerWorld(ApiFactory factory, WebApplicationFactory<Program>? host)
    {
        Factory = factory;
        _host = host ?? factory;
        Tenant = factory.CreateTenant();
    }

    public static async Task<PartnerWorld> CreateAsync(ApiFactory factory, WebApplicationFactory<Program>? host = null)
    {
        var world = new PartnerWorld(factory, host);
        world.Admin = world.As("Admin User", 1, Everything);
        var cities = (await (await world.Admin.GetAsync("/api/lookups/CITY")).DataAsync()).EnumerateArray().ToList();
        world.City = cities[0].GetProperty("id").GetInt32();
        world.OtherCity = cities[1].GetProperty("id").GetInt32();
        return world;
    }

    public HttpClient As(string name, int userId, params string[] permissions) =>
        _host.CreateClient().WithToken(TestTokens.For(Tenant, name, userId, permissions));

    // ── Bodies ──────────────────────────────────────────────────────────────────────

    private static readonly Random Rng = new();

    /// <summary>Two words of random letters: unlike any other name, so it never trips the duplicate warning by accident.</summary>
    public static string RandomName()
    {
        string Word() => new(Enumerable.Range(0, 7).Select(_ => (char)('a' + Rng.Next(26))).ToArray());
        return $"{char.ToUpper(Word()[0])}{Word()[1..]} {char.ToUpper(Word()[0])}{Word()[1..]}";
    }

    public static string NextCnic() => $"35202-{Interlocked.Increment(ref _counter):D7}-1";
    public static string NextMobile() => $"0300-{Interlocked.Increment(ref _counter):D7}";
    public static string NextNtn() => $"{Interlocked.Increment(ref _counter):D7}-8";

    /// <summary>A person who is only a workshop: nothing beyond the General tab to fill in.</summary>
    public JsonObject Person(string? name = null, string role = "Workshop") => new()
    {
        ["partyType"] = "Person",
        ["legalName"] = name ?? RandomName(),
        ["cnic"] = NextCnic(),
        ["primaryMobile"] = NextMobile(),
        ["cityId"] = City,
        ["addressLine"] = "12 Main Boulevard, Gulberg",
        ["roles"] = new JsonArray(role)
    };

    public JsonObject Company(string? name = null, params string[] roles) => new()
    {
        ["partyType"] = "Company",
        ["legalName"] = name ?? RandomName() + " Pvt Ltd",
        ["ntn"] = NextNtn(),
        ["email"] = "accounts@example.pk",
        ["primaryMobile"] = NextMobile(),
        ["cityId"] = City,
        ["addressLine"] = "Plot 4, Industrial Area",
        ["roles"] = new JsonArray(roles.Length == 0 ? [JsonValue.Create("Vendor")!] : roles.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
        ["vendor"] = new JsonObject { ["supplyCategories"] = new JsonArray("Parts") }
    };

    public static JsonObject Driver(string? licenceNo = null, string? expiry = null) => new()
    {
        ["licenceNo"] = licenceNo ?? "LTV-" + Interlocked.Increment(ref _counter),
        ["licenceType"] = "HTV",
        ["licenceExpiryDate"] = expiry ?? DateTime.UtcNow.AddYears(2).ToString("yyyy-MM-dd"),
        ["employmentType"] = "Contractor",
        ["monthlyRate"] = 60000,
        ["commissionBasis"] = "Percent",
        ["commissionValue"] = 5
    };

    // ── Calls ───────────────────────────────────────────────────────────────────────

    public Task<HttpResponseMessage> PostAsync(JsonObject body, HttpClient? client = null) => (client ?? Admin).PostAsJsonAsync("/api/partners", body);

    /// <summary>Creates a partner and returns what the API answered with, failing the test if it was refused.</summary>
    public async Task<JsonObject> CreateAsync(JsonObject body, HttpClient? client = null)
    {
        var response = await PostAsync(body, client);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return AsObject(await response.DataAsync());
    }

    public async Task<JsonObject> GetAsync(int id, HttpClient? client = null)
    {
        var response = await (client ?? Admin).GetAsync($"/api/partners/{id}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return AsObject(await response.DataAsync());
    }

    /// <summary>Saves the partner as it is now, with the changes the test makes to the body it got back.</summary>
    public async Task<HttpResponseMessage> PutAsync(JsonObject partner, HttpClient? client = null) =>
        await (client ?? Admin).PutAsJsonAsync($"/api/partners/{partner["id"]!.GetValue<int>()}", partner);

    public static JsonObject AsObject(JsonElement element) => JsonNode.Parse(element.GetRawText())!.AsObject();

    public static async Task<List<(string Field, string Code, string Message)>> ErrorsAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("errors", out var errors)
            ? errors.EnumerateArray().Select(e => (e.GetProperty("field").GetString() ?? "", e.GetProperty("code").GetString()!, e.GetProperty("message").GetString()!)).ToList()
            : [];
    }

    /// <summary>What the API refused with, as one field and one code: fails if there was any other outcome.</summary>
    public static async Task AssertRefusedAsync(HttpResponseMessage response, string field, string code)
    {
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ErrorsAsync(response);
        Assert.Contains(errors, e => e.Field == field && e.Code == code);
    }
}

/// <summary>Stands in for the modules that will point at partners: a test says which partner is "in use" for which change.</summary>
public sealed class FakePartnerUsage : IPartnerUsageCheck
{
    public static readonly ConcurrentDictionary<(int Partner, PartnerIntent Intent), string> InUse = new();

    public Task<IReadOnlyList<PartnerUsage>> FindBlockingAsync(int partnerId, PartnerIntent intent, string? roleCode, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PartnerUsage>>(InUse.TryGetValue((partnerId, intent), out var what) ? [new PartnerUsage("Vehicle", "LEA-1234", what)] : []);

    public static WebApplicationFactory<Program> Host(ApiFactory factory) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IPartnerUsageCheck, FakePartnerUsage>()));
}
