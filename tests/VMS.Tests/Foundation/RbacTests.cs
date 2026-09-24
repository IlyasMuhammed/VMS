using System.Net;
using System.Net.Http.Json;
using System.Web;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>
/// Stage 6 (§23B): the last-admin guard (BR-SEC-008), multi-role assignment and the effective-permission union
/// (§23B.1), data scope (§23B.4) actually applied to the Partners and Vehicles lists, and the six default role
/// templates (§23B.7). The enforcement primitives themselves (permission checks, field permissions, denial
/// logging) were built and tested in Stage 0; this covers what Stage 6 added on top of them.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RbacTests(ApiFactory factory)
{
    private const string Password = "Passw0rd!Test";

    /// <summary>A fresh tenant with a real, logged-in Tenant Admin (every permission) — the Auth module's own provisioning path, not a crafted token, since these tests exercise the DB-backed guards.</summary>
    private async Task<(Guid TenantId, HttpClient Admin)> NewTenantAsync(HttpClient superAdmin, string label)
    {
        var email = $"{label}.{Guid.NewGuid():N}@test.local";
        var response = await superAdmin.PostAsJsonAsync("/api/system/tenants", new
        {
            tenantCode = "RB" + Guid.NewGuid().ToString("N")[..8], tenantName = label + " Transport",
            adminFirstName = "Admin", adminEmail = email,
        });
        response.EnsureSuccessStatusCode();
        var data = await response.DataAsync();
        var tenantId = data.GetProperty("tenantId").GetGuid();
        var link = data.GetProperty("adminInviteLink").GetString()!;
        var token = HttpUtility.ParseQueryString(new Uri(link).Query)["token"]!;
        (await superAdmin.PostAsJsonAsync("/api/auth/accept-invite", new { token, newPassword = Password })).EnsureSuccessStatusCode();

        var client = factory.CreateClient();
        var session = await client.LoginAsync(email, Password);
        return (tenantId, client.WithToken(session.AccessToken));
    }

    private static async Task<int> CreateUserAsync(HttpClient admin, string roleId, string emailPrefix)
    {
        var response = await admin.PostAsJsonAsync("/api/users", new { firstName = "Test", lastName = "User", email = $"{emailPrefix}.{Guid.NewGuid():N}@test.local", roleId = int.Parse(roleId) });
        response.EnsureSuccessStatusCode();
        return (await response.DataAsync()).GetProperty("userId").GetInt32();
    }

    private static async Task<int> RoleIdByCodeAsync(HttpClient admin, string code)
    {
        var roles = await (await admin.GetAsync("/api/roles")).DataAsync();
        return roles.EnumerateArray().First(r => r.GetProperty("roleCode").GetString() == code).GetProperty("roleId").GetInt32();
    }

    // ── BR-SEC-008: the last-admin guard ─────────────────────────────────────────────

    [Fact]
    public async Task A_tenant_can_never_be_left_with_zero_active_administrators()
    {
        using var superAdmin = await factory.AsSuperAdminAsync();
        var (_, admin) = await NewTenantAsync(superAdmin, "LastAdmin");
        var meId = (await (await admin.GetAsync("/api/auth/me")).DataAsync()).GetProperty("userId").GetInt32();
        var staffRoleId = await RoleIdByCodeAsync(admin, "STAFF");

        // A different actor (the Super Admin — not subject to "you cannot change your own role") tries to reassign
        // the tenant's one and only administrator away: refused, or nobody would be left able to manage its roles.
        var lastOne = await superAdmin.PutAsJsonAsync($"/api/users/{meId}/role", new { roleId = staffRoleId });
        Assert.Equal(HttpStatusCode.Conflict, lastOne.StatusCode);
        Assert.Contains("nobody able to manage roles", await lastOne.MessageAsync());

        // Once a second administrator exists and is active, reassigning the first one away succeeds.
        var tenantAdminRoleId = await RoleIdByCodeAsync(admin, "TENANT_ADMIN");
        var secondAdminId = await CreateUserAsync(admin, tenantAdminRoleId.ToString(), "second");
        (await superAdmin.PatchJsonAsync($"/api/users/{secondAdminId}", new { isActive = true })).EnsureSuccessStatusCode();
        var reassign = await superAdmin.PutAsJsonAsync($"/api/users/{meId}/role", new { roleId = staffRoleId });
        Assert.True(reassign.IsSuccessStatusCode, await reassign.Content.ReadAsStringAsync());

        // And now the second one, the only administrator left, cannot be reassigned away either.
        var lastOneAgain = await superAdmin.PutAsJsonAsync($"/api/users/{secondAdminId}/role", new { roleId = staffRoleId });
        Assert.Equal(HttpStatusCode.Conflict, lastOneAgain.StatusCode);

        // Nor deactivated.
        var deactivate = await superAdmin.PatchJsonAsync($"/api/users/{secondAdminId}", new { isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
    }

    [Fact]
    public async Task Removing_a_users_only_role_that_grants_manage_roles_is_refused_when_nobody_else_holds_it()
    {
        using var superAdmin = await factory.AsSuperAdminAsync();
        var (_, admin) = await NewTenantAsync(superAdmin, "RemoveRole");
        var meId = (await (await admin.GetAsync("/api/auth/me")).DataAsync()).GetProperty("userId").GetInt32();
        var tenantAdminRoleId = await RoleIdByCodeAsync(admin, "TENANT_ADMIN");

        // The only administrator holds exactly one role (TENANT_ADMIN); it is their only one at all, so removing it is refused for that reason first.
        var removeOnly = await admin.DeleteAsync($"/api/users/{meId}/roles/{tenantAdminRoleId}");
        Assert.Equal(HttpStatusCode.BadRequest, removeOnly.StatusCode);
        Assert.Contains("at least one role", await removeOnly.MessageAsync());

        // Give them a second role first, then removing TENANT_ADMIN is the real last-admin check.
        var readOnlyRoleId = await RoleIdByCodeAsync(admin, "READ_ONLY");
        var added = await admin.PostAsJsonAsync($"/api/users/{meId}/roles", new { roleId = readOnlyRoleId });
        Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync());

        var removeAdmin = await admin.DeleteAsync($"/api/users/{meId}/roles/{tenantAdminRoleId}");
        Assert.Equal(HttpStatusCode.Conflict, removeAdmin.StatusCode);
        Assert.Contains("nobody able to manage roles", await removeAdmin.MessageAsync());
    }

    [Fact]
    public async Task The_built_in_administrator_role_can_never_be_stripped_of_manage_roles_or_deactivated()
    {
        using var superAdmin = await factory.AsSuperAdminAsync();
        var (_, admin) = await NewTenantAsync(superAdmin, "Protect");
        // TENANT_ADMIN is a global (platform) role: only a Super Admin may edit it at all (RoleGuard.EnsureCanModify) —
        // a separate, broader rule from the one this test is after, so the Super Admin is the actor here.
        var tenantAdmin = await (await superAdmin.GetAsync($"/api/roles/{await RoleIdByCodeAsync(admin, "TENANT_ADMIN")}")).DataAsync();
        var everyPermissionExceptRoleManage = tenantAdmin.GetProperty("permissionGroups").EnumerateArray()
            .SelectMany(g => g.GetProperty("permissions").EnumerateArray())
            .Where(p => p.GetProperty("code").GetString() != "ROLE_MANAGE")
            .Select(p => p.GetProperty("permissionId").GetInt32()).ToList();

        var strip = await superAdmin.PutAsJsonAsync($"/api/roles/{tenantAdmin.GetProperty("roleId").GetInt32()}/permissions", new { allowedPermissionIds = everyPermissionExceptRoleManage });
        Assert.Equal(HttpStatusCode.Conflict, strip.StatusCode);
        Assert.Contains("cannot be stripped", await strip.MessageAsync());

        var deactivate = await superAdmin.PatchAsync($"/api/roles/{tenantAdmin.GetProperty("roleId").GetInt32()}/deactivate", null);
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        Assert.Contains("cannot be deactivated", await deactivate.MessageAsync());
    }

    /// <summary>
    /// The built-in Administrator role can never lose Manage roles (proven above) and always has an active holder
    /// (BR-SEC-008's own broader guard), so a custom role can never actually be the tenant's *only* source of it —
    /// the same <c>EnsureSomeOtherRoleGrantsAdminAsync</c> check that would refuse that case also has to let a
    /// custom role's Manage roles go when the built-in one still covers it, which is what this proves.
    /// </summary>
    [Fact]
    public async Task Manage_roles_can_be_revoked_from_a_custom_role_once_another_role_still_grants_it()
    {
        using var superAdmin = await factory.AsSuperAdminAsync();
        var (_, admin) = await NewTenantAsync(superAdmin, "CustomAdmin");

        var created = await admin.PostAsJsonAsync("/api/roles", new { name = "Ops Admin " + Guid.NewGuid().ToString("N")[..6], roleCode = "OPSADMIN" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), description = "" });
        created.EnsureSuccessStatusCode();
        var customRole = await created.DataAsync();
        var roleManageId = customRole.GetProperty("permissionGroups").EnumerateArray()
            .SelectMany(g => g.GetProperty("permissions").EnumerateArray())
            .First(p => p.GetProperty("code").GetString() == "ROLE_MANAGE").GetProperty("permissionId").GetInt32();
        var saved = await admin.PutAsJsonAsync($"/api/roles/{customRole.GetProperty("roleId").GetInt32()}/permissions", new { allowedPermissionIds = new[] { roleManageId } });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());

        var stripUnused = await admin.PutAsJsonAsync($"/api/roles/{customRole.GetProperty("roleId").GetInt32()}/permissions", new { allowedPermissionIds = Array.Empty<int>() });
        Assert.True(stripUnused.IsSuccessStatusCode, await stripUnused.Content.ReadAsStringAsync());
    }

    // ── §23B.1: multiple roles, and the effective-permission union ──────────────────

    [Fact]
    public async Task A_user_can_hold_more_than_one_role_and_their_effective_permissions_are_the_union()
    {
        using var superAdmin = await factory.AsSuperAdminAsync();
        var (_, admin) = await NewTenantAsync(superAdmin, "Union");
        var readOnlyId = await RoleIdByCodeAsync(admin, "READ_ONLY");
        var opsId = await RoleIdByCodeAsync(admin, "OPERATIONS_USER");
        var userId = await CreateUserAsync(admin, readOnlyId.ToString(), "union");

        var addOps = await admin.PostAsJsonAsync($"/api/users/{userId}/roles", new { roleId = opsId, scopeType = "AllBranches" });
        Assert.True(addOps.IsSuccessStatusCode, await addOps.Content.ReadAsStringAsync());

        // Holding it twice is refused.
        var addAgain = await admin.PostAsJsonAsync($"/api/users/{userId}/roles", new { roleId = opsId });
        Assert.Equal(HttpStatusCode.Conflict, addAgain.StatusCode);

        var effective = await (await admin.GetAsync($"/api/users/{userId}/effective-permissions")).DataAsync();
        var codes = effective.EnumerateArray().Select(p => p.GetProperty("code").GetString()).ToHashSet();
        Assert.Contains("BP.EXPORT", codes);       // from Read Only
        Assert.Contains("VEH.DRIVER.ASSIGN", codes); // from Operations User
        var driverAssign = effective.EnumerateArray().First(p => p.GetProperty("code").GetString() == "VEH.DRIVER.ASSIGN");
        Assert.Contains("Operations User", driverAssign.GetProperty("grantedByRoles").EnumerateArray().Select(r => r.GetString()));

        var removed = await admin.DeleteAsync($"/api/users/{userId}/roles/{opsId}");
        Assert.True(removed.IsSuccessStatusCode);
        var after = await (await admin.GetAsync($"/api/users/{userId}/effective-permissions")).DataAsync();
        Assert.DoesNotContain(after.EnumerateArray().Select(p => p.GetProperty("code").GetString()), c => c == "VEH.DRIVER.ASSIGN");
    }

    [Fact]
    public async Task A_users_own_login_carries_the_union_of_their_roles_permissions()
    {
        using var superAdmin = await factory.AsSuperAdminAsync();
        var (_, admin) = await NewTenantAsync(superAdmin, "LoginUnion");
        var readOnlyId = await RoleIdByCodeAsync(admin, "READ_ONLY");
        var opsId = await RoleIdByCodeAsync(admin, "OPERATIONS_USER");
        var email = $"loginunion.{Guid.NewGuid():N}@test.local";
        var created = await admin.PostAsJsonAsync("/api/users", new { firstName = "Multi", lastName = "Role", email, roleId = readOnlyId });
        created.EnsureSuccessStatusCode();
        var user = await created.DataAsync();
        var token = HttpUtility.ParseQueryString(new Uri(user.GetProperty("inviteLink").GetString()!).Query)["token"]!;
        (await admin.PostAsJsonAsync("/api/auth/accept-invite", new { token, newPassword = Password })).EnsureSuccessStatusCode();
        await admin.PostAsJsonAsync($"/api/users/{user.GetProperty("userId").GetInt32()}/roles", new { roleId = opsId });

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var me = (await login.DataAsync()).GetProperty("user");
        var permissions = me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToHashSet();
        Assert.Contains("BP.EXPORT", permissions);
        Assert.Contains("VEH.DRIVER.ASSIGN", permissions);
    }

    // ── §23B.4: data scope actually applied ──────────────────────────────────────────

    [Fact]
    public async Task Own_branch_scope_hides_partners_outside_the_users_branch()
    {
        using var superAdmin = await factory.AsSuperAdminAsync();
        var (tenantId, admin) = await NewTenantAsync(superAdmin, "Scope");

        var branchA = CreateBranch(tenantId, "Branch A");
        var branchB = CreateBranch(tenantId, "Branch B");
        var cityId = (await (await admin.GetAsync("/api/lookups/CITY")).DataAsync())[0].GetProperty("id").GetInt32();
        var inA = await CreatePartnerAsync(admin, cityId, branchA);
        var inB = await CreatePartnerAsync(admin, cityId, branchB);

        var readOnlyId = await RoleIdByCodeAsync(admin, "READ_ONLY");
        var scopedUserId = await CreateUserAsync(admin, readOnlyId.ToString(), "scoped");
        var scopeSet = await admin.PutAsJsonAsync($"/api/users/{scopedUserId}/scope", new { scopeType = "OwnBranch", branchId = branchA });
        Assert.True(scopeSet.IsSuccessStatusCode, await scopeSet.Content.ReadAsStringAsync());

        var scopedClient = await LoginAsAsync(admin, scopedUserId);
        var list = await (await scopedClient.GetAsync("/api/partners?page=1&pageSize=100")).DataAsync();
        var ids = list.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetInt32()).ToHashSet();
        Assert.Contains(inA, ids);
        Assert.DoesNotContain(inB, ids);
    }

    [Fact]
    public async Task Own_vehicles_scope_shows_only_the_vehicles_the_linked_driver_is_assigned_to()
    {
        using var superAdmin = await factory.AsSuperAdminAsync();
        var (_, admin) = await NewTenantAsync(superAdmin, "OwnVehicles");
        var cityId = (await (await admin.GetAsync("/api/lookups/CITY")).DataAsync())[0].GetProperty("id").GetInt32();
        var driverPartnerId = await CreateDriverPartnerAsync(admin, cityId);

        var truckType = (await (await admin.GetAsync("/api/lookups/VEHICLE_TYPE")).DataAsync())[0].GetProperty("id").GetInt32();
        var make = (await (await admin.GetAsync("/api/lookups/MAKE")).DataAsync())[0].GetProperty("id").GetInt32();
        var mine = await CreateActiveVehicleAsync(admin, truckType, make, "OV1");
        var notMine = await CreateActiveVehicleAsync(admin, truckType, make, "OV2");
        var assign = await admin.PostAsJsonAsync($"/api/vehicles/{mine}/driver", new { driverId = driverPartnerId });
        Assert.True(assign.IsSuccessStatusCode, await assign.Content.ReadAsStringAsync());

        var driverRoleId = await RoleIdByCodeAsync(admin, "DRIVER");
        var driverUserId = await CreateUserAsync(admin, driverRoleId.ToString(), "driveruser");
        var link = await admin.PutAsJsonAsync($"/api/users/{driverUserId}/driver-link", new { partnerId = driverPartnerId });
        Assert.True(link.IsSuccessStatusCode, await link.Content.ReadAsStringAsync());
        var scopeSet = await admin.PutAsJsonAsync($"/api/users/{driverUserId}/scope", new { scopeType = "OwnVehicles" });
        Assert.True(scopeSet.IsSuccessStatusCode, await scopeSet.Content.ReadAsStringAsync());
        // A driver needs VEH.VIEW to test the list at all; give it via a second role that carries only that.
        await admin.PostAsJsonAsync($"/api/users/{driverUserId}/roles", new { roleId = await RoleIdByCodeAsync(admin, "READ_ONLY"), scopeType = "OwnVehicles" });

        var driverClient = await LoginAsAsync(admin, driverUserId);
        var list = await (await driverClient.GetAsync("/api/vehicles?page=1&pageSize=100")).DataAsync();
        var ids = list.GetProperty("items").EnumerateArray().Select(v => v.GetProperty("id").GetInt32()).ToHashSet();
        Assert.Contains(mine, ids);
        Assert.DoesNotContain(notMine, ids);
    }

    [Fact]
    public async Task Linking_a_user_to_a_partner_requires_the_driver_role()
    {
        using var superAdmin = await factory.AsSuperAdminAsync();
        var (_, admin) = await NewTenantAsync(superAdmin, "DriverLinkGuard");
        var cityId = (await (await admin.GetAsync("/api/lookups/CITY")).DataAsync())[0].GetProperty("id").GetInt32();
        var notADriver = await CreatePartnerAsync(admin, cityId, null);
        var someUserId = await CreateUserAsync(admin, (await RoleIdByCodeAsync(admin, "READ_ONLY")).ToString(), "guard");

        var response = await admin.PutAsJsonAsync($"/api/users/{someUserId}/driver-link", new { partnerId = notADriver });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Driver role", await response.MessageAsync());
    }

    // ── §23B.7: the six default role templates ───────────────────────────────────────

    [Fact]
    public void The_six_default_role_templates_are_seeded_matching_the_fsd()
    {
        factory.CreateClient();
        var codes = new[] { "TENANT_ADMIN", "FLEET_MANAGER", "FINANCE_USER", "OPERATIONS_USER", "DRIVER", "READ_ONLY" };
        foreach (var code in codes)
            Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM auth.Roles WHERE RoleCode = @c AND IsGlobal = 1", ("@c", code)));

        // Read Only never sees a cost, profit, salary or credit field.
        var fieldCodes = new[] { "BP.FIELD.SALARY.VIEW", "BP.FIELD.CREDIT.VIEW", "BP.FIELD.OPENING.VIEW", "VEH.FIELD.COST.VIEW", "VEH.FIELD.FINANCE.VIEW", "VEH.FIELD.PROFIT.VIEW" };
        foreach (var field in fieldCodes)
        {
            var granted = factory.Scalar<int>(
                @"SELECT COUNT(*) FROM auth.RolePermissions rp JOIN auth.Roles r ON r.RoleID = rp.RoleID JOIN auth.Permissions p ON p.PermissionID = rp.PermissionID
                  WHERE r.RoleCode = 'READ_ONLY' AND p.Code = @f", ("@f", field));
            Assert.Equal(0, granted);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    /// <summary>Branches have no creation endpoint (OQ-10: one branch for now) — inserted directly, the same as any other fixture the API itself cannot produce.</summary>
    private Guid CreateBranch(Guid tenantId, string name)
    {
        var id = Guid.NewGuid();
        factory.Execute(
            "INSERT INTO core.Branches (BranchId, TenantId, Code, Name, IsActive, CreatedOn) VALUES (@i, @t, @c, @n, 1, SYSUTCDATETIME())",
            ("@i", id), ("@t", tenantId), ("@c", "B" + id.ToString("N")[..8].ToUpperInvariant()), ("@n", name));
        return id;
    }

    private static async Task<int> CreatePartnerAsync(HttpClient admin, int cityId, Guid? branchId)
    {
        var body = new Dictionary<string, object?>
        {
            ["partyType"] = "Person", ["legalName"] = "Scope Test " + Guid.NewGuid().ToString("N")[..8],
            ["cnic"] = $"35202-{Random.Shared.Next(1000000, 9999999)}-1", ["primaryMobile"] = $"0300-{Random.Shared.Next(1000000, 9999999)}",
            ["cityId"] = cityId, ["addressLine"] = "1 Test Road", ["roles"] = new[] { "Workshop" }, ["branchId"] = branchId,
        };
        var response = await admin.PostAsJsonAsync("/api/partners", body);
        response.EnsureSuccessStatusCode();
        return (await response.DataAsync()).GetProperty("id").GetInt32();
    }

    private static async Task<int> CreateDriverPartnerAsync(HttpClient admin, int cityId)
    {
        var body = new
        {
            partyType = "Person", legalName = "Driver Person " + Guid.NewGuid().ToString("N")[..8],
            cnic = $"35202-{Random.Shared.Next(1000000, 9999999)}-1", primaryMobile = $"0300-{Random.Shared.Next(1000000, 9999999)}",
            cityId, addressLine = "1 Test Road", roles = new[] { "Driver" },
            driver = new { licenceNo = "LTV-" + Random.Shared.Next(100000, 999999), licenceType = "HTV", licenceExpiryDate = DateTime.UtcNow.AddYears(2).ToString("yyyy-MM-dd"), employmentType = "Contractor", monthlyRate = 60000, commissionBasis = "Percent", commissionValue = 5 },
        };
        var response = await admin.PostAsJsonAsync("/api/partners", body);
        response.EnsureSuccessStatusCode();
        return (await response.DataAsync()).GetProperty("id").GetInt32();
    }

    /// <summary>A Draft, then made Active directly (activation itself needs the acquisition/finance flow this test has no reason to set up).</summary>
    private async Task<int> CreateActiveVehicleAsync(HttpClient admin, int vehicleTypeId, int makeId, string prefix)
    {
        var response = await admin.PostAsJsonAsync("/api/vehicles", new { registrationNo = $"{prefix}-{Random.Shared.Next(1000, 9999)}", vehicleTypeId, makeId, model = "500", fuelType = "Diesel", loadCapacity = 20, capacityUnit = "Tonne" });
        response.EnsureSuccessStatusCode();
        var vehicle = await response.DataAsync();
        var id = vehicle.GetProperty("id").GetInt32();
        factory.Execute("UPDATE veh.Vehicles SET Status = 'Active', CurrentCategory = 'SelfOwned' WHERE VehicleId = @i", ("@i", id));
        return id;
    }

    private async Task<HttpClient> LoginAsAsync(HttpClient admin, int userId)
    {
        var reset = await admin.PostAsync($"/api/users/{userId}/reset-password", null);
        reset.EnsureSuccessStatusCode();
        var data = await reset.DataAsync();
        var email = data.GetProperty("email").GetString()!;
        var token = HttpUtility.ParseQueryString(new Uri(data.GetProperty("inviteLink").GetString()!).Query)["token"]!;
        (await admin.PostAsJsonAsync("/api/auth/accept-invite", new { token, newPassword = Password })).EnsureSuccessStatusCode();
        var client = factory.CreateClient();
        var session = await client.LoginAsync(email, Password);
        return client.WithToken(session.AccessToken);
    }
}
