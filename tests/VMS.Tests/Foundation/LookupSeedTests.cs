using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using VMS.Shared.Authorization;
using VMS.Shared.Lookups;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-13 and S0-FND-14: the master lists of FSD §24.2 and the city / province mapping, as every tenant first sees them.</summary>
[Collection(ApiCollection.Name)]
public sealed class LookupSeedTests(ApiFactory factory)
{
    private HttpClient NewReader(out Guid tenant)
    {
        tenant = factory.CreateTenant();
        return factory.CreateClient().WithToken(TestTokens.For(tenant, "Plain User", 9600));
    }

    private static async Task<List<JsonElement>> Values(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.DataAsync()).EnumerateArray().ToList();
    }

    private static List<string> Descriptions(IEnumerable<JsonElement> values) => values.Select(v => v.GetProperty("description").GetString()!).ToList();

    // ── The lists in the FSD ────────────────────────────────────────────────────────

    /// <summary>The name in the FSD's table, and the list it becomes.</summary>
    private static readonly (string FsdName, string Type)[] Lists =
    [
        ("Vehicle Type", PlatformLookups.VehicleType),
        ("Make", PlatformLookups.Make),
        ("Body Type", PlatformLookups.BodyType),
        ("Axle Configuration", PlatformLookups.AxleConfiguration),
        ("Attached Item Type", PlatformLookups.AttachedItemType),
        ("BP Document Type", PlatformLookups.BpDocumentType),
        ("Vehicle Document Type", PlatformLookups.VehicleDocumentType),
        ("Expense Type", PlatformLookups.ExpenseType),
        ("Income Type", PlatformLookups.IncomeType),
        ("Finance Type", PlatformLookups.FinanceType),
    ];

    /// <summary>Reads §24.2 from the FSD in the repository, so the seeds are checked against the document itself.</summary>
    private static Dictionary<string, List<string>> FsdSeeds()
    {
        var root = typeof(LookupSeedTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "RepositoryRoot").Value!;

        var file = Directory.GetFiles(Path.Combine(root, "Document"), "VMS Phase 1 FSD*.md").SingleOrDefault();
        Assert.True(file is not null, "The FSD (Document/VMS Phase 1 FSD*.md) was not found.");

        var lines = File.ReadAllLines(file!);
        var start = Array.FindIndex(lines, l => l.StartsWith("### 24.2", StringComparison.Ordinal));
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("### 24.3", StringComparison.Ordinal));
        Assert.True(start >= 0 && end > start, "Section 24.2 was not found in the FSD.");

        var seeds = new Dictionary<string, List<string>>();
        foreach (var line in lines[start..end].Where(l => l.StartsWith('|')))
        {
            var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length < 2 || !Lists.Any(l => l.FsdName == cells[0])) continue;

            // "CNIC, Driving Licence, … — each with expirable flag": the note after the dash is not a value.
            var values = Regex.Replace(cells[1], @"\s+—.*$", "");
            seeds[cells[0]] = values.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
        }
        return seeds;
    }

    [Fact]
    public void The_fsd_was_read_and_names_every_list_this_test_covers()
    {
        var fsd = FsdSeeds();

        Assert.Equal(Lists.Select(l => l.FsdName).Order(), fsd.Keys.Order());
    }

    [Theory]
    [InlineData("Vehicle Type", PlatformLookups.VehicleType)]
    [InlineData("Make", PlatformLookups.Make)]
    [InlineData("Body Type", PlatformLookups.BodyType)]
    [InlineData("Axle Configuration", PlatformLookups.AxleConfiguration)]
    [InlineData("Attached Item Type", PlatformLookups.AttachedItemType)]
    [InlineData("BP Document Type", PlatformLookups.BpDocumentType)]
    [InlineData("Vehicle Document Type", PlatformLookups.VehicleDocumentType)]
    [InlineData("Expense Type", PlatformLookups.ExpenseType)]
    [InlineData("Income Type", PlatformLookups.IncomeType)]
    [InlineData("Finance Type", PlatformLookups.FinanceType)]
    public async Task A_new_tenant_starts_with_exactly_the_values_the_fsd_lists_in_the_same_order(string fsdName, string lookupType)
    {
        var expected = FsdSeeds()[fsdName];
        using var reader = NewReader(out _);

        var actual = Descriptions(await Values(reader, $"/api/lookups/{lookupType}"));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Every_seeded_value_has_a_usable_code_and_a_place_in_the_order()
    {
        using var reader = NewReader(out _);

        foreach (var (_, type) in Lists)
        {
            var values = await Values(reader, $"/api/lookups/{type}");
            Assert.All(values, v => Assert.Matches("^[A-Z0-9][A-Z0-9_]*$", v.GetProperty("code").GetString()!));
            Assert.Equal(values.Count, values.Select(v => v.GetProperty("code").GetString()).Distinct().Count());
            Assert.Equal(Enumerable.Range(1, values.Count).Select(i => i * 10), values.Select(v => v.GetProperty("sortOrder").GetInt32()));
        }
    }

    [Fact]
    public async Task Finance_type_codes_are_the_ones_business_rules_will_recognise()
    {
        using var reader = NewReader(out _);

        var codes = (await Values(reader, "/api/lookups/FINANCE_TYPE")).Select(v => v.GetProperty("code").GetString());

        Assert.Equal(["FULLY_PAID", "BANK_LEASE", "IJARAH", "LOAN"], codes);
    }

    [Fact]
    public async Task Document_types_carry_their_expirable_flag()
    {
        using var reader = NewReader(out _);

        async Task<Dictionary<string, string?>> Flags(string type) =>
            (await Values(reader, $"/api/lookups/{type}")).ToDictionary(v => v.GetProperty("code").GetString()!, v => v.GetProperty("attributes").GetProperty("expirable").GetString());

        var partner = await Flags(PlatformLookups.BpDocumentType);
        Assert.Equal("true", partner["CNIC"]);
        Assert.Equal("true", partner["DRIVING_LICENCE"]);
        Assert.Equal("false", partner["NTN_CERTIFICATE"]);
        Assert.Equal("false", partner["CHEQUE_COPY"]);

        var vehicle = await Flags(PlatformLookups.VehicleDocumentType);
        Assert.Equal("false", vehicle["REGISTRATION_BOOK"]);   // life of the vehicle
        Assert.Equal("true", vehicle["INSURANCE"]);
        Assert.Equal("true", vehicle["FITNESS"]);
        Assert.Equal("true", vehicle["ROUTE_PERMIT"]);
        Assert.Equal("true", vehicle["TOKEN_TAX"]);
        Assert.Equal("false", vehicle["PURCHASE_INVOICE"]);
    }

    [Fact]
    public async Task Lists_without_extra_fields_carry_none()
    {
        using var reader = NewReader(out _);

        var values = await Values(reader, "/api/lookups/VEHICLE_TYPE");

        Assert.All(values, v => Assert.Empty(v.GetProperty("attributes").EnumerateObject()));
    }

    // ── Provinces and cities ────────────────────────────────────────────────────────

    [Fact]
    public async Task The_seven_provinces_and_territories_are_there()
    {
        using var reader = NewReader(out _);

        var provinces = await Values(reader, "/api/lookups/PROVINCE");

        Assert.Equal(
            ["Punjab", "Sindh", "Khyber Pakhtunkhwa", "Balochistan", "Islamabad Capital Territory", "Azad Jammu and Kashmir", "Gilgit-Baltistan"],
            Descriptions(provinces));
        Assert.Equal(["PUNJAB", "SINDH", "KHYBER_PAKHTUNKHWA", "BALOCHISTAN", "ISLAMABAD_CAPITAL_TERRITORY", "AZAD_JAMMU_AND_KASHMIR", "GILGIT_BALTISTAN"],
            provinces.Select(p => p.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task Every_city_is_in_a_province_that_exists()
    {
        using var reader = NewReader(out _);
        var provinces = (await Values(reader, "/api/lookups/PROVINCE")).Select(p => p.GetProperty("code").GetString()).ToHashSet();

        var cities = await Values(reader, "/api/lookups/CITY");

        Assert.Equal(PlatformLookups.All.Single(t => t.Code == PlatformLookups.City).Seeds.Count, cities.Count);
        Assert.All(cities, c => Assert.Contains(c.GetProperty("attributes").GetProperty("province").GetString(), provinces));
        Assert.Equal(cities.Count, Descriptions(cities).Distinct().Count());
        // Every province has somewhere to send a truck, and the big freight provinces have plenty.
        foreach (var province in provinces)
            Assert.Contains(cities, c => c.GetProperty("attributes").GetProperty("province").GetString() == province);
        Assert.True(cities.Count(c => c.GetProperty("attributes").GetProperty("province").GetString() == "PUNJAB") >= 20);
        Assert.True(cities.Count(c => c.GetProperty("attributes").GetProperty("province").GetString() == "SINDH") >= 8);
    }

    [Theory]
    [InlineData("Lahore", "PUNJAB")]
    [InlineData("Rawalpindi", "PUNJAB")]
    [InlineData("Karachi", "SINDH")]
    [InlineData("Hyderabad", "SINDH")]
    [InlineData("Peshawar", "KHYBER_PAKHTUNKHWA")]
    [InlineData("Quetta", "BALOCHISTAN")]
    [InlineData("Gwadar", "BALOCHISTAN")]
    [InlineData("Islamabad", "ISLAMABAD_CAPITAL_TERRITORY")]
    [InlineData("Muzaffarabad", "AZAD_JAMMU_AND_KASHMIR")]
    [InlineData("Gilgit", "GILGIT_BALTISTAN")]
    public async Task A_city_maps_to_its_province(string city, string province)
    {
        using var reader = NewReader(out _);

        var found = (await Values(reader, "/api/lookups/CITY")).Single(c => c.GetProperty("description").GetString() == city);

        Assert.Equal(province, found.GetProperty("attributes").GetProperty("province").GetString());
    }

    [Fact]
    public async Task A_province_picker_can_ask_for_just_its_own_cities()
    {
        using var reader = NewReader(out _);

        var sindh = await Values(reader, "/api/lookups/CITY?province=SINDH");

        Assert.Contains("Karachi", Descriptions(sindh));
        Assert.DoesNotContain("Lahore", Descriptions(sindh));
        Assert.All(sindh, c => Assert.Equal("SINDH", c.GetProperty("attributes").GetProperty("province").GetString()));
        Assert.Equal(sindh.Count, (await Values(reader, "/api/lookups/CITY?province=sindh")).Count);   // codes are not case-sensitive
        Assert.Empty(await Values(reader, "/api/lookups/CITY?province=NOWHERE"));
    }

    [Fact]
    public async Task Filtering_on_a_field_the_list_does_not_have_is_refused()
    {
        using var reader = NewReader(out _);

        var response = await reader.GetAsync("/api/lookups/CITY?population=1000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("'population' is not a field of City.", await response.MessageAsync());
    }

    [Fact]
    public async Task A_tenant_can_add_its_own_city_but_only_in_a_province_that_can_be_chosen()
    {
        var tenant = factory.CreateTenant();
        using var admin = factory.CreateClient().WithToken(TestTokens.For(tenant, "Master Admin", 9601, PermissionCodes.ADM_MASTER_MANAGE));
        var code = "TOWN" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var noProvince = await admin.PostAsJsonAsync("/api/admin/lookups/CITY", new { code, description = "Nowhere" });
        var badProvince = await admin.PostAsJsonAsync("/api/admin/lookups/CITY", new { code, description = "Nowhere", attributes = new { province = "ATLANTIS" } });
        var good = await admin.PostAsJsonAsync("/api/admin/lookups/CITY", new { code, description = "New Town", attributes = new { province = "punjab" } });

        Assert.Equal(HttpStatusCode.BadRequest, noProvince.StatusCode);
        Assert.Equal("Province is required.", await noProvince.MessageAsync());
        Assert.Equal(HttpStatusCode.BadRequest, badProvince.StatusCode);
        Assert.Equal(HttpStatusCode.Created, good.StatusCode);
        Assert.Equal("PUNJAB", (await good.DataAsync()).GetProperty("attributes").GetProperty("province").GetString());
    }

    [Fact]
    public async Task A_province_with_active_cities_cannot_be_retired()
    {
        var tenant = factory.CreateTenant();
        using var admin = factory.CreateClient().WithToken(TestTokens.For(tenant, "Master Admin", 9602, PermissionCodes.ADM_MASTER_MANAGE));
        var provinces = (await Values(admin, "/api/admin/lookups/PROVINCE"));
        var gb = provinces.Single(p => p.GetProperty("code").GetString() == "GILGIT_BALTISTAN").GetProperty("id").GetInt32();

        var response = await admin.PutAsJsonAsync($"/api/admin/lookups/PROVINCE/{gb}", new { description = "Gilgit-Baltistan", sortOrder = 70, isActive = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Gilgit-Baltistan cannot be retired: 2 active City values use it. Move or retire those first.", await response.MessageAsync());
    }
}
