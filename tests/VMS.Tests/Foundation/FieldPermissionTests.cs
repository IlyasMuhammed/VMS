using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using VMS.Shared.Authorization;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-07: a value the caller may not see is left out of the response, on the server.</summary>
[Collection(ApiCollection.Name)]
public sealed class FieldPermissionTests(ApiFactory factory)
{
    private async Task<(JsonElement Data, string Raw)> GetAsync(params string[] permissions)
    {
        using var host = factory.With<SampleFieldsController>().Build();
        using var client = host.CreateClient().WithToken(TestTokens.For("Gul Rana", 9201, permissions));

        var response = await client.GetAsync("/test/fields");
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        return (doc.RootElement.GetProperty("data").Clone(), raw);
    }

    [Fact]
    public async Task Without_any_field_permission_restricted_values_are_absent_not_zero()
    {
        var (data, raw) = await GetAsync();

        Assert.Equal("Hino 500", data.GetProperty("name").GetString());
        Assert.False(data.TryGetProperty("purchasePrice", out _));
        Assert.False(data.TryGetProperty("profit", out _));
        Assert.False(data.GetProperty("lines")[0].TryGetProperty("cost", out _));
        // The number itself is nowhere in the payload, so it cannot be read from the network tab.
        Assert.DoesNotContain("5000000", raw);
        Assert.DoesNotContain("450000", raw);
        Assert.DoesNotContain("120000", raw);
    }

    [Fact]
    public async Task Each_permission_reveals_only_its_own_fields()
    {
        var (data, _) = await GetAsync(PermissionCodes.VEH_FIELD_COST_VIEW);

        Assert.Equal(5_000_000m, data.GetProperty("purchasePrice").GetDecimal());
        Assert.Equal(450_000m, data.GetProperty("lines")[0].GetProperty("cost").GetDecimal());
        Assert.False(data.TryGetProperty("profit", out _));
    }

    [Fact]
    public async Task Holding_every_field_permission_shows_everything()
    {
        var (data, _) = await GetAsync(PermissionCodes.VEH_FIELD_COST_VIEW, PermissionCodes.VEH_FIELD_PROFIT_VIEW);

        Assert.Equal(5_000_000m, data.GetProperty("purchasePrice").GetDecimal());
        Assert.Equal(120_000m, data.GetProperty("profit").GetDecimal());
    }

    [Fact]
    public async Task The_super_admin_sees_every_field()
    {
        using var host = factory.With<SampleFieldsController>().Build();
        using var client = host.CreateClient();
        var session = await client.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        client.WithToken(session.AccessToken);

        var data = await (await client.GetAsync("/test/fields")).DataAsync();

        Assert.True(data.TryGetProperty("purchasePrice", out _));
        Assert.True(data.TryGetProperty("profit", out _));
    }

    [Fact]
    public void With_no_caller_at_all_restricted_fields_stay_hidden()
    {
        FieldPermissionContext.Current = null;
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver().WithAddedModifier(FieldPermissionRules.HideRestrictedProperties)
        };

        var json = JsonSerializer.Serialize(new SampleFieldsDto(), options);

        Assert.Contains("Hino 500", json);
        Assert.DoesNotContain("PurchasePrice", json);
        Assert.DoesNotContain("Profit", json);
    }

    [Fact]
    public void Exports_and_printouts_ask_for_the_same_hidden_list_as_the_screen()
    {
        var withCostOnly = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("permission", PermissionCodes.VEH_FIELD_COST_VIEW)], "test"));

        var hidden = FieldPermissionRules.HiddenProperties(typeof(SampleFieldsDto), withCostOnly);

        Assert.Equal(["Profit"], hidden.Select(p => p.Name));
    }
}
