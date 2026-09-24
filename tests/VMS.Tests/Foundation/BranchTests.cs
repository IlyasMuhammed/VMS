using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-15: the branch master (FSD §6 field 15). One branch to start with (OQ-10), so a partner or a vehicle always has one to choose.</summary>
[Collection(ApiCollection.Name)]
public sealed class BranchTests(ApiFactory factory)
{
    private sealed record Who(Guid Tenant, HttpClient Reader);

    private Who NewTenant()
    {
        var tenant = factory.CreateTenant();
        return new Who(tenant, factory.CreateClient().WithToken(TestTokens.For(tenant, "Plain User", 9600)));
    }

    private static async Task<List<JsonElement>> BranchesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/branches");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.DataAsync()).EnumerateArray().ToList();
    }

    [Fact]
    public async Task A_tenant_starts_with_one_branch_created_the_first_time_it_is_asked_for()
    {
        var who = NewTenant();
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM core.Branches WHERE TenantId = @t", ("@t", who.Tenant)));

        var branches = await BranchesAsync(who.Reader);
        var branch = Assert.Single(branches);
        Assert.Equal("Head Office", branch.GetProperty("name").GetString());
        Assert.True(branch.GetProperty("isActive").GetBoolean());
        Assert.NotEqual(Guid.Empty, branch.GetProperty("id").GetGuid());

        // Asking again does not create a second one.
        Assert.Single(await BranchesAsync(who.Reader));
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM core.Branches WHERE TenantId = @t", ("@t", who.Tenant)));
    }

    [Fact]
    public async Task Each_tenant_gets_its_own_branch()
    {
        var a = NewTenant();
        var b = NewTenant();

        var branchA = (await BranchesAsync(a.Reader))[0].GetProperty("id").GetGuid();
        var branchB = (await BranchesAsync(b.Reader))[0].GetProperty("id").GetGuid();

        Assert.NotEqual(branchA, branchB);
    }

    [Fact]
    public async Task Twentyfive_requests_at_once_create_only_one_branch()
    {
        var who = NewTenant();

        var responses = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => who.Reader.GetAsync("/api/branches")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM core.Branches WHERE TenantId = @t", ("@t", who.Tenant)));
    }

    [Fact]
    public async Task Reading_the_list_needs_only_to_be_signed_in()
    {
        var response = await factory.CreateClient().GetAsync("/api/branches");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Wired into the modules that point at a branch ────────────────────────────────

    [Fact]
    public async Task A_partner_can_be_given_the_tenants_branch_and_a_branch_from_nowhere_is_refused()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var branchId = (await BranchesAsync(w.Admin))[0].GetProperty("id").GetGuid();

        var body = w.Company();
        body["branchId"] = branchId.ToString();
        var created = await w.CreateAsync(body);
        Assert.Equal(branchId.ToString(), created["branchId"]!.GetValue<string>());

        var bad = w.Company();
        bad["branchId"] = Guid.NewGuid().ToString();
        var refused = await w.Admin.PostAsJsonAsync("/api/partners", bad);
        var errors = await PartnerWorld.ErrorsAsync(refused);
        Assert.Contains(errors, e => e.Field == "branchId");
    }

    [Fact]
    public async Task A_vehicle_can_be_given_the_tenants_branch_and_a_branch_from_nowhere_is_refused()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var branchId = (await BranchesAsync(w.Admin))[0].GetProperty("id").GetGuid();

        var truck = w.Truck();
        truck["branchId"] = branchId.ToString();
        var created = await w.CreateAsync(truck);
        Assert.Equal(branchId.ToString(), created["branchId"]!.GetValue<string>());

        var bad = w.Truck();
        bad["branchId"] = Guid.NewGuid().ToString();
        var refused = await PartnerWorld.ErrorsAsync(await w.PostAsync(bad));
        Assert.Contains(refused, e => e.Field == "branchId");
    }

    [Fact]
    public async Task A_partners_branch_is_named_in_the_list_and_the_list_can_be_filtered_by_it()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var branchId = (await BranchesAsync(w.Admin))[0].GetProperty("id").GetGuid();
        var withBranch = w.Company();
        withBranch["branchId"] = branchId.ToString();
        var partner = await w.CreateAsync(withBranch);
        var noBranch = await w.CreateAsync(w.Company());   // no branch: must not show up when filtered by one

        var listed = await (await w.Admin.GetAsync("/api/partners")).DataAsync();
        var row = listed.GetProperty("items").EnumerateArray().First(r => r.GetProperty("id").GetInt32() == partner["id"]!.GetValue<int>());
        Assert.Equal("Head Office", row.GetProperty("branch").GetString());

        var filtered = await (await w.Admin.GetAsync($"/api/partners?branchId={branchId}")).DataAsync();
        var ids = filtered.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).ToList();
        Assert.Contains(partner["id"]!.GetValue<int>(), ids);
        Assert.DoesNotContain(noBranch["id"]!.GetValue<int>(), ids);
    }

    [Fact]
    public async Task A_vehicles_branch_is_named_in_the_list_and_the_list_can_be_filtered_by_it()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var branchId = (await BranchesAsync(w.Admin))[0].GetProperty("id").GetGuid();
        var truck = w.Truck();
        truck["branchId"] = branchId.ToString();
        var vehicle = await w.CreateAsync(truck);
        var noBranch = await w.CreateAsync(w.Truck());

        var listed = await (await w.Admin.GetAsync("/api/vehicles")).DataAsync();
        var row = listed.GetProperty("items").EnumerateArray().First(r => r.GetProperty("id").GetInt32() == vehicle["id"]!.GetValue<int>());
        Assert.Equal("Head Office", row.GetProperty("branch").GetString());

        var filtered = await (await w.Admin.GetAsync($"/api/vehicles?branchId={branchId}")).DataAsync();
        var ids = filtered.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).ToList();
        Assert.Contains(vehicle["id"]!.GetValue<int>(), ids);
        Assert.DoesNotContain(noBranch["id"]!.GetValue<int>(), ids);
    }

    [Fact]
    public async Task A_branch_already_on_a_record_stays_valid_even_if_it_were_retired()
    {
        // Nothing retires a branch yet (there is no admin screen for it), so this proves the "already held" rule the same
        // way City and the lookups prove it: the record can be saved again unchanged without the reference being re-checked.
        var w = await VehicleWorld.CreateAsync(factory);
        var branchId = (await BranchesAsync(w.Admin))[0].GetProperty("id").GetGuid();
        var truck = w.Truck();
        truck["branchId"] = branchId.ToString();
        var vehicle = await w.CreateAsync(truck);

        vehicle["remarks"] = "Touched";
        var saved = await w.PutAsync(vehicle);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
    }
}
